using MessagePack;
using MessagePack.Resolvers;
using Simulation.Models;
using Simulation.Readers;
using System;
using System.IO;

namespace Simulation.Tools
{
  /// <summary>
  /// One-time converter: LiteDB tick database -> .ticks + .vars files.
  ///
  /// Output (independent datasets, same tick sequence):
  ///   {instrument}.ticks — fixed 52-byte records back-to-back, no header.
  ///                        Price-only view for FixedSummaryReader.
  ///   {instrument}.vars  — length-prefixed MessagePack blobs, no header:
  ///                        [length: int32][blob]... Full-fidelity view
  ///                        for VariableSummaryReader (includes Dom/Order).
  ///
  /// Usage: TickFileConverter &lt;dataDir&gt; &lt;instrument&gt;
  ///   e.g. TickFileConverter /data/FUTS ES
  /// </summary>
  public static class TickFileConverter
  {
    const int RecordSize = 52;

    static readonly MessagePackSerializerOptions MsgPackOptions =
      ContractlessStandardResolver.Options;

    public static void Convert(string dataDir, string instrument)
    {
      var ticksPath = Path.Combine(dataDir, $"{instrument}.ticks");
      var varsPath = Path.Combine(dataDir, $"{instrument}.vars");
      Console.WriteLine($"Converting {instrument} -> {ticksPath} + {varsPath}");

      using var reader = new LiteSummaryReader(Path.Combine(dataDir, $"{instrument}.db"), instrument);

      var tmpTicks = ticksPath + ".tmp";
      var tmpVars = varsPath + ".tmp";

      long count = 0;
      using (var ticksFs = new FileStream(tmpTicks, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
      using (var varsFs = new FileStream(tmpVars, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
      using (var ticksBw = new BinaryWriter(ticksFs))
      using (var varsBw = new BinaryWriter(varsFs))
      {
        while (reader.MoveNext())
        {
          var s = reader.Current;

          // Fixed record: Time + Bid/Ask/Last + BidSize/AskSize + Volume
          var time = s.Time;
          var bid = s.Instrument?.Price?.Bid ?? 0;
          var ask = s.Instrument?.Price?.Ask ?? 0;
          var last = 0.0; // Trade price: not in Price model (reserved for future)
          var bidSize = s.Instrument?.Price?.BidSize ?? 0;
          var askSize = s.Instrument?.Price?.AskSize ?? 0;
          var volume = 0; // Trade volume: not in Price model (reserved for future)

          ticksBw.Write(time);
          ticksBw.Write(bid);
          ticksBw.Write(ask);
          ticksBw.Write(last);
          ticksBw.Write(bidSize);
          ticksBw.Write(askSize);
          ticksBw.Write(volume);

          // Variable blob: full Summary as MessagePack, length-prefixed
          var blob = MessagePackSerializer.Serialize(s, MsgPackOptions);
          varsBw.Write(blob.Length);
          varsBw.Write(blob);

          count++;
          if (count % 1_000_000 == 0) Console.WriteLine($"  {count:N0} ticks...");
        }
      }

      File.Move(tmpTicks, ticksPath, overwrite: true);
      File.Move(tmpVars, varsPath, overwrite: true);

      Console.WriteLine($"Done: {count:N0} summaries");
      Console.WriteLine($"  {ticksPath}");
      Console.WriteLine($"  {varsPath}");
    }
  }
}
