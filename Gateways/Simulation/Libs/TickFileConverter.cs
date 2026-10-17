using Simulation;
using Simulation.Models;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Simulation.Tools
{
  /// <summary>
  /// One-time converter: LiteDB tick database -> flat binary .ticks file
  /// for MmapSummaryReader. Run once per instrument, then backtests can
  /// use SummaryReaderKind.MemoryMapped.
  ///
  /// Usage: TickFileConverter <dataDir> <instrument> [tickSize]
  ///   e.g. TickFileConverter /data/FUTS ES 0.25
  /// </summary>
  public static class TickFileConverter
  {
    public static void Convert(string dataDir, string instrument, double tickSize = 0.25)
    {
      var outPath = Path.Combine(dataDir, $"{instrument}.ticks");
      Console.WriteLine($"Converting {instrument} -> {outPath}");

      // First pass: read all summaries via the LiteDB reader, collect records
      using var reader = new LiteDbSummaryReader(Path.Combine(dataDir, $"{instrument}.db"), instrument);

      var tmpPath = outPath + ".tmp";
      using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
      using (var bw = new BinaryWriter(fs))
      {
        // Placeholder header (32 bytes) — rewritten at the end
        bw.Write(new byte[Marshal.SizeOf<TickFileHeader>()]);

        long count = 0;
        while (reader.MoveNext())
        {
          var s = reader.Current;
          var rec = ToRecord(s, tickSize);
          WriteRecord(bw, rec);
          count++;
          if (count % 1_000_000 == 0) Console.WriteLine($"  {count:N0} ticks...");
        }

        // Rewrite header with actual count
        fs.Seek(0, SeekOrigin.Begin);
        WriteHeader(bw, count, tickSize);
        Console.WriteLine($"Done: {count:N0} ticks -> {outPath}");
      }

      File.Move(tmpPath, outPath, overwrite: true);
    }

    static TickRecord ToRecord(Summary s, double tickSize)
    {
      // Extract top-of-book from Dom; trade price from Instrument if present
      long bestBidKey = 0, bestAskKey = 0;
      int bidSize = 0, askSize = 0;
      if (s.Dom != null)
      {
        if (s.Dom.Bids.Count > 0)
        {
          bestBidKey = s.Dom.Bids.Keys.First(); // Bids sorted descending
          bidSize = s.Dom.Bids[bestBidKey].Count;
        }
        if (s.Dom.Asks.Count > 0)
        {
          bestAskKey = s.Dom.Asks.Keys.First(); // Asks sorted ascending
          askSize = s.Dom.Asks[bestAskKey].Count;
        }
      }

      // Dom keys are price * 1e8 (see MmapSummaryReader); convert back to ticks
      int bidTicks = bestBidKey != 0 ? (int)(bestBidKey / 1e8 / tickSize) : 0;
      int askTicks = bestAskKey != 0 ? (int)(bestAskKey / 1e8 / tickSize) : 0;

      return new TickRecord
      {
        TimeNs = s.Time,
        BidTicks = bidTicks,
        AskTicks = askTicks,
        BidSize = bidSize,
        AskSize = askSize,
        TradeTicks = 0, // LiteDB Summary rarely carries trade ticks separately; extend as needed
        TradeSize = 0,
        Flags = 0,
      };
    }

    static void WriteHeader(BinaryWriter bw, long count, double tickSize)
    {
      bw.Write(MmapSummaryReader.Magic);
      bw.Write(MmapSummaryReader.CurrentVersion);
      bw.Write(Marshal.SizeOf<TickRecord>());
      bw.Write(count);
      bw.Write(tickSize);
      bw.Write(0); // reserved
    }

    static void WriteRecord(BinaryWriter bw, TickRecord r)
    {
      bw.Write(r.TimeNs);
      bw.Write(r.BidTicks);
      bw.Write(r.AskTicks);
      bw.Write(r.BidSize);
      bw.Write(r.AskSize);
      bw.Write(r.TradeTicks);
      bw.Write(r.TradeSize);
      bw.Write(r.Flags);
    }
  }
}
