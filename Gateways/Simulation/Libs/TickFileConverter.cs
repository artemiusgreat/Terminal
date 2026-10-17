using MemoryPack;
using Simulation.Models;
using System;
using System.IO;

namespace Simulation.Tools
{
  /// <summary>
  /// One-time converter: LiteDB tick database -> flat binary .ticks file
  /// for MmapSummaryReader. Each Summary is MemoryPack-serialized with a
  /// 4-byte length prefix. Run once per instrument, then backtests can use
  /// SummaryReaderKind.MemoryMapped.
  ///
  /// Usage: TickFileConverter &lt;dataDir&gt; &lt;instrument&gt;
  ///   e.g. TickFileConverter /data/FUTS ES
  /// </summary>
  public static class TickFileConverter
  {
    public static void Convert(string dataDir, string instrument)
    {
      var outPath = Path.Combine(dataDir, $"{instrument}.ticks");
      Console.WriteLine($"Converting {instrument} -> {outPath}");

      using var reader = new LiteDbSummaryReader(Path.Combine(dataDir, $"{instrument}.db"), instrument);

      var tmpPath = outPath + ".tmp";
      using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
      using (var bw = new BinaryWriter(fs))
      {
        // Header: magic(4) | version(4) | count(8) — count rewritten at end
        bw.Write(MmapSummaryReader.Magic);
        bw.Write(MmapSummaryReader.CurrentVersion);
        bw.Write(0L); // placeholder count

        long count = 0;
        while (reader.MoveNext())
        {
          var bytes = MemoryPackSerializer.Serialize(reader.Current);
          bw.Write(bytes.Length);
          bw.Write(bytes);
          count++;
          if (count % 1_000_000 == 0) Console.WriteLine($"  {count:N0} ticks...");
        }

        // Rewrite count
        fs.Seek(8, SeekOrigin.Begin);
        bw.Write(count);
        Console.WriteLine($"Done: {count:N0} summaries -> {outPath}");
      }

      File.Move(tmpPath, outPath, overwrite: true);
    }
  }
}
