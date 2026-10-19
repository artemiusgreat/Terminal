using MessagePack;
using MessagePack.Resolvers;
using Simulation.Models;
using Simulation.Readers;
using System;
using System.Collections.Generic;
using System.IO;

namespace Simulation.Tools
{
  /// <summary>
  /// One-time converter: LiteDB tick database -> two-region .ticks file
  /// for MmapSummaryReader v2 (no MemoryPack).
  ///
  /// Layout:
  ///   [header: magic(4) | version(4) | count(8) | varRegionOffset(8)]
  ///   [fixed 48-byte records] × N
  ///   [variable region: MessagePack blobs]
  ///
  /// Fixed records carry Time + Bid/Ask/Trade (as int ticks ×10000) + sizes.
  /// Ticks with Dom/Order/Options get FlagHasVarData and a MessagePack blob
  /// in the variable region (rare). All other ticks are zero-copy.
  ///
  /// Usage: TickFileConverter &lt;dataDir&gt; &lt;instrument&gt;
  ///   e.g. TickFileConverter /data/FUTS ES
  /// </summary>
  public static class TickFileConverter
  {
    const double PriceScale = 10000.0;
    const int FlagHasVarData = 1;
    const int RecordSize = 48;
    const int HeaderSize = 24;

    static readonly MessagePackSerializerOptions MsgPackOptions =
      ContractlessStandardResolver.Options;

    public static void Convert(string dataDir, string instrument)
    {
      var outPath = Path.Combine(dataDir, $"{instrument}.ticks");
      Console.WriteLine($"Converting {instrument} -> {outPath} (v2 two-region)");

      using var reader = new LiteSummaryReader(Path.Combine(dataDir, $"{instrument}.db"), instrument);

      var tmpPath = outPath + ".tmp";
      using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
      using (var bw = new BinaryWriter(fs))
      {
        // Header: magic(4) | version(4) | count(8) | varRegionOffset(8)
        // count and varRegionOffset rewritten at end
        bw.Write(MapSummaryReader.Magic);
        bw.Write(MapSummaryReader.CurrentVersion);
        bw.Write(0L); // placeholder count
        bw.Write(0L); // placeholder varRegionOffset

        // Buffer fixed records in memory, then write var region
        // (For 21GB source, fixed records are ~48 bytes × N; N for 1 day ES
        //  is ~2M ticks = ~96MB — acceptable. For larger, spill to temp file.)
        var fixedRecords = new List<byte[]>();
        var varBlobs = new List<byte[]>();
        var varOffsets = new List<int>(); // parallel to fixedRecords, -1 if none

        long count = 0;
        long varRegionSize = 0;
        while (reader.MoveNext())
        {
          var s = reader.Current;
          var rec = new byte[RecordSize];

          var timeNs = s.Time;
          var bid = s.Instrument?.Price?.Bid ?? 0;
          var ask = s.Instrument?.Price?.Ask ?? 0;
          var bidSize = (int)(s.Instrument?.Price?.BidSize ?? 0);
          var askSize = (int)(s.Instrument?.Price?.AskSize ?? 0);

          // Trade data: not in Price model; use 0 (reserved for future)
          var tradeTicks = 0;
          var tradeSize = 0;

          BitConverter.GetBytes(timeNs).CopyTo(rec, 0);
          BitConverter.GetBytes((int)(bid * PriceScale)).CopyTo(rec, 8);
          BitConverter.GetBytes((int)(ask * PriceScale)).CopyTo(rec, 12);
          BitConverter.GetBytes(tradeTicks).CopyTo(rec, 16);
          BitConverter.GetBytes(bidSize).CopyTo(rec, 20);
          BitConverter.GetBytes(askSize).CopyTo(rec, 24);
          BitConverter.GetBytes(tradeSize).CopyTo(rec, 28);

          // Check for variable data (Dom, Order, Options)
          var hasVar = s.Dom != null || s.Order != null ||
                       (s.Options != null && s.Options.Count > 0);
          int varOffset = -1;
          int varLength = 0;
          int flags = 0;

          if (hasVar)
          {
            var blob = MessagePackSerializer.Serialize(s, MsgPackOptions);
            varOffset = (int)varRegionSize;
            varLength = blob.Length;
            varBlobs.Add(blob);
            varRegionSize += blob.Length;
            flags |= FlagHasVarData;
          }

          BitConverter.GetBytes(varOffset).CopyTo(rec, 32);
          BitConverter.GetBytes(varLength).CopyTo(rec, 36);
          BitConverter.GetBytes(flags).CopyTo(rec, 40);
          // bytes 44-47: reserved (zero)

          fixedRecords.Add(rec);
          varOffsets.Add(varOffset); // not strictly needed, kept for clarity

          count++;
          if (count % 1_000_000 == 0) Console.WriteLine($"  {count:N0} ticks...");
        }

        // Write fixed records
        foreach (var rec in fixedRecords)
          bw.Write(rec);

        // Write var region, record its offset
        var varRegionOffset = HeaderSize + count * RecordSize;
        foreach (var blob in varBlobs)
          bw.Write(blob);

        // Rewrite header: count and varRegionOffset
        fs.Seek(8, SeekOrigin.Begin);
        bw.Write(count);
        bw.Write(varRegionOffset);

        Console.WriteLine($"Done: {count:N0} summaries, {varBlobs.Count:N0} with var data -> {outPath}");
      }

      File.Move(tmpPath, outPath, overwrite: true);
    }
  }
}
