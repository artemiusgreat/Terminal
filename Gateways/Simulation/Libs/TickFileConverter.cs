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
  /// One-time converter: LiteDB tick database -> .ticks + .var files
  /// for FixedSummaryReader / VariableSummaryReader.
  ///
  /// Output:
  ///   {instrument}.ticks — fixed 64-byte records back-to-back, no header.
  ///                        Count = file size / 64.
  ///   {instrument}.var   — MessagePack blobs concatenated, no header.
  ///                        Offsets/lengths stored in the fixed record's
  ///                        VarOffset/VarLength fields (relative to .var start).
  ///
  /// Fixed records carry Time + Bid/Ask/Last + BidSize/AskSize + Volume.
  /// Ticks with Dom/Order/Options get FlagHasVarData and a MessagePack blob
  /// in the .var file (rare). All other ticks are zero-copy reads.
  ///
  /// Usage: TickFileConverter &lt;dataDir&gt; &lt;instrument&gt;
  ///   e.g. TickFileConverter /data/FUTS ES
  /// </summary>
  public static class TickFileConverter
  {
    const int FlagHasVarData = 1;
    const int RecordSize = 64;

    static readonly MessagePackSerializerOptions MsgPackOptions =
      ContractlessStandardResolver.Options;

    public static void Convert(string dataDir, string instrument)
    {
      var ticksPath = Path.Combine(dataDir, $"{instrument}.ticks");
      var varPath = Path.Combine(dataDir, $"{instrument}.var");
      Console.WriteLine($"Converting {instrument} -> {ticksPath} + {varPath}");

      using var reader = new LiteSummaryReader(Path.Combine(dataDir, $"{instrument}.db"), instrument);

      var tmpTicks = ticksPath + ".tmp";
      var tmpVar = varPath + ".tmp";

      // Buffer fixed records in memory (64 bytes × N; ~128MB for 2M ticks/1 day ES).
      // For larger conversions, spill to temp file.
      var fixedRecords = new List<byte[]>();
      var varBlobs = new List<byte[]>();

      long count = 0;
      long varRegionSize = 0;
      while (reader.MoveNext())
      {
        var s = reader.Current;
        var rec = new byte[RecordSize];

        var time = s.Time;
        var bid = s.Instrument?.Price?.Bid ?? 0;
        var ask = s.Instrument?.Price?.Ask ?? 0;
        var last = 0.0; // Trade price: not in Price model (reserved for future)
        var bidSize = s.Instrument?.Price?.BidSize ?? 0;
        var askSize = s.Instrument?.Price?.AskSize ?? 0;
        var volume = 0; // Trade volume: not in Price model (reserved for future)

        BitConverter.GetBytes(time).CopyTo(rec, 0);
        BitConverter.GetBytes(bid).CopyTo(rec, 8);
        BitConverter.GetBytes(ask).CopyTo(rec, 16);
        BitConverter.GetBytes(last).CopyTo(rec, 24);
        BitConverter.GetBytes(bidSize).CopyTo(rec, 32);
        BitConverter.GetBytes(askSize).CopyTo(rec, 40);
        BitConverter.GetBytes(volume).CopyTo(rec, 48);

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

        BitConverter.GetBytes(varOffset).CopyTo(rec, 52);
        BitConverter.GetBytes(varLength).CopyTo(rec, 56);
        BitConverter.GetBytes(flags).CopyTo(rec, 60);

        fixedRecords.Add(rec);
        count++;
        if (count % 1_000_000 == 0) Console.WriteLine($"  {count:N0} ticks...");
      }

      // Write .ticks (no header — raw records)
      using (var fs = new FileStream(tmpTicks, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        foreach (var rec in fixedRecords)
          fs.Write(rec, 0, rec.Length);

      // Write .var (no header — raw blobs; empty file if none)
      using (var fs = new FileStream(tmpVar, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        foreach (var blob in varBlobs)
          fs.Write(blob, 0, blob.Length);

      File.Move(tmpTicks, ticksPath, overwrite: true);
      File.Move(tmpVar, varPath, overwrite: true);

      Console.WriteLine($"Done: {count:N0} summaries, {varBlobs.Count:N0} with var data");
      Console.WriteLine($"  {ticksPath}");
      Console.WriteLine($"  {varPath}");
    }
  }
}
