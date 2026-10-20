using Core.Enums;
using Core.Models;
using MessagePack;
using MessagePack.Resolvers;
using Simulation.Models;
using Simulation.Readers;
using System;
using System.IO;

namespace Simulation.Tools
{
  /// <summary>
  /// One-time converter: LiteDB MBO stream -> .ticks + .vars files.
  ///
  /// LiteDB contains raw MBO orders (Summary.Order). The converter replays
  /// them through DomBook (same aggregation as DomGrain) and writes a
  /// tick record ONLY on DomAction.Trade, using the aggregated Instrument
  /// quote (best Bid/Ask + sizes from the book, Last/Volume from the trade).
  ///
  /// Output (independent datasets, same tick sequence):
  ///   {instrument}.ticks — fixed 56-byte records back-to-back, no header.
  ///                        Price-only view for FixedSummaryReader.
  ///   {instrument}.vars  — length-prefixed MessagePack blobs, no header:
  ///                        [length: int32][blob]... Full-fidelity view
  ///                        for VariableSummaryReader.
  ///
  /// Usage: TickFileConverter &lt;dataDir&gt; &lt;instrument&gt;
  ///   e.g. TickFileConverter /data/FUTS ES
  /// </summary>
  public static class TickFileConverter
  {
    static readonly MessagePackSerializerOptions MsgPackOptions =
      ContractlessStandardResolver.Options;

    public static void Convert(string dataDir, string instrument)
    {
      var ticksPath = Path.Combine(dataDir, $"{instrument}.ticks");
      var varsPath = Path.Combine(dataDir, $"{instrument}.vars");
      Console.WriteLine($"Converting {instrument} -> {ticksPath} + {varsPath}");

      using var reader = new LiteReader(Path.Combine(dataDir, $"{instrument}.db"), instrument);

      var tmpTicks = ticksPath + ".tmp";
      var tmpVars = varsPath + ".tmp";

      long count = 0;      // trade ticks written
      long mboCount = 0;   // MBO records processed

      // StepSize for book price keys — from first instrument, default 0.01
      double stepSize = 0.01;
      DomBook book = null;

      using (var ticksFs = new FileStream(tmpTicks, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
      using (var varsFs = new FileStream(tmpVars, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
      using (var ticksBw = new BinaryWriter(ticksFs))
      using (var varsBw = new BinaryWriter(varsFs))
      {
        while (reader.MoveNext())
        {
          var s = reader.Current;
          var order = s.Order;

          if (order is null)
            continue;

          mboCount++;

          // Lazily init book with StepSize from first available instrument
          if (book is null)
          {
            stepSize = s.Instrument?.StepSize ?? 0.01;
            book = new DomBook(stepSize);
            Console.WriteLine($"  StepSize: {stepSize}");
          }

          if (order.Action == DomAction.Trade)
          {
            // Aggregate quote from book state + trade
            var quote = book.Trade(order);
            var price = quote.Price;

            // Fixed record: Time + Bid/Ask/Last + BidSize/AskSize + Volume
            ticksBw.Write(price.Time ?? s.Time);
            ticksBw.Write(price.Bid ?? 0);
            ticksBw.Write(price.Ask ?? 0);
            ticksBw.Write(price.Last ?? 0);
            ticksBw.Write(price.BidSize ?? 0);
            ticksBw.Write(price.AskSize ?? 0);
            ticksBw.Write(price.Volume ?? 0);
            /*
            // Variable blob: full Summary as MessagePack, length-prefixed
            var blob = MessagePackSerializer.Serialize(s, MsgPackOptions);
            varsBw.Write(blob.Length);
            varsBw.Write(blob);
            */
            count++;
            if (count % 1_000_000 == 0) Console.WriteLine($"  {count:N0} trades...");
          }
          else
          {
            // Update book state (Store/Update/Remove/Clear)
            book.SendOrder(order);
          }
        }
      }

      File.Move(tmpTicks, ticksPath, overwrite: true);
      File.Move(tmpVars, varsPath, overwrite: true);

      Console.WriteLine($"Done: {mboCount:N0} MBO records -> {count:N0} trade ticks");
      Console.WriteLine($"  {ticksPath}");
      Console.WriteLine($"  {varsPath}");
    }
  }
}
