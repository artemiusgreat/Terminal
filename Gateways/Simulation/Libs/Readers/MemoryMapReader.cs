using Core.Models;
using Simulation.Models;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace Simulation.Readers
{
  /// <summary>
  /// Memory-mapped ISummaryReader for fixed 56-byte tick records.
  ///
  /// File: {name}.ticks — raw 56-byte records back-to-back, no header.
  /// Record count is derived from file size (length / 56).
  ///
  /// Fixed record (56 bytes):
  ///   Time(8) | Bid(8) | Ask(8) | Last(8) | BidSize(8) | AskSize(8) |
  ///   Volume(8)
  ///
  /// Price-only view: no Dom/Order/Options. For full-fidelity history
  /// use VariableMapReader ({name}.vars) instead. The two readers
  /// are independent — neither calls the other.
  ///
  /// Hot path is zero-copy: records are read directly from the mapped view
  /// as a struct, no deserialization.
  ///
  /// Use TickFileConverter to build .ticks files from LiteDB (one-time).
  /// </summary>
  [StructLayout(LayoutKind.Sequential, Pack = 1)]
  struct TickRecord
  {
    public long Time;       // offset 0
    public double Bid;      // offset 8
    public double Ask;      // offset 16
    public double Last;     // offset 24
    public double BidSize;  // offset 32
    public double AskSize;  // offset 40
    public double Volume;   // offset 48
  } // 56 bytes total

  public class MemoryMapReader : ISummaryReader
  {
    static readonly int RecordSize = Marshal.SizeOf<TickRecord>();

    public string Name { get; }

    readonly MemoryMappedFile mmf;
    readonly MemoryMappedViewAccessor accessor;
    readonly long count;

    long position = -1;
    Summary current;

    public Summary Current => current;

    public MemoryMapReader(string source, string name)
    {
      Name = name;

      var ticksPath = Path.Combine(source, $"{name}.ticks");
      mmf = MemoryMappedFile.CreateFromFile(ticksPath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
      accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

      // No header — count derived from file size
      count = new FileInfo(ticksPath).Length / RecordSize;
    }

    public bool MoveNext()
    {
      if (position + 1 >= count) return false;
      position++;

      var recordOffset = position * RecordSize;

      // Direct struct copy from the mapped view — single 56-byte read.
      accessor.Read(recordOffset, out TickRecord rec);

      current = new Summary
      {
        Time = rec.Time,
        Instrument = new Instrument
        {
          Name = Name,
          Price = new Price
          {
            Time = rec.Time,
            Bid = rec.Bid,
            Ask = rec.Ask,
            Last = rec.Last,
            BidSize = rec.BidSize,
            AskSize = rec.AskSize,
            Volume = rec.Volume,
          }
        }
      };

      return true;
    }

    public void Dispose()
    {
      accessor?.Dispose();
      mmf?.Dispose();
    }
  }
}
