using Core.Models;
using Simulation.Models;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace Simulation.Readers
{
  /// <summary>
  /// Memory-mapped ISummaryReader for a fixed size structure.
  /// </summary>
  [StructLayout(LayoutKind.Sequential, Pack = 1)]
  struct TickRecord
  {
    public long Time;
    public double Bid;
    public double Ask;
    public double Last;
    public double BidSize;
    public double AskSize;
    public double Volume;
  }

  public class MemoryMapReader : ISummaryReader
  {
    static readonly int RecordSize = Marshal.SizeOf<TickRecord>();

    public string Name { get; }

    readonly MemoryMappedFile map;
    readonly MemoryMappedViewAccessor accessor;
    readonly long count;

    long position = -1;
    Summary current;

    public Summary Current => current;

    public MemoryMapReader(string source, string name)
    {
      Name = name;

      var doc = Path.Combine(source, $"{name}.ticks");

      map = MemoryMappedFile.CreateFromFile(doc, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
      accessor = map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
      count = new FileInfo(doc).Length / RecordSize;
    }

    public bool MoveNext()
    {
      if (position++ + 1 >= count)
      {
        return false;
      }

      var index = position * RecordSize;

      accessor.Read(index, out TickRecord rec);
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
            BidSize = rec.BidSize,
            AskSize = rec.AskSize,
            Volume = rec.Volume
          }
        }
      };

      return true;
    }

    public void Dispose()
    {
      accessor?.Dispose();
      map?.Dispose();
    }
  }
}
