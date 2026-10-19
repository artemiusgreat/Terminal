using Core.Models;
using Simulation.Models;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace Simulation.Readers
{
  /// <summary>
  /// Memory-mapped ISummaryReader for fixed 48-byte tick records.
  ///
  /// File: {name}.ticks — raw 64-byte records back-to-back, no header.
  /// Record count is derived from file size (length / 64).
  ///
  /// Fixed record (64 bytes):
  ///   Time(8) | Bid(8) | Ask(8) | Last(8) | BidSize(8) | AskSize(8) |
  ///   Volume(4) | VarOffset(4) | VarLength(4) | Flags(4)
  ///
  /// Flags bit 0 = HasVarData.
  /// VarOffset/VarLength point into the companion {name}.var file
  /// (read via VariableSummaryReader).
  ///
  /// Hot path is zero-copy: records are read directly from the mapped view
  /// as a struct, no deserialization. Ticks with Dom/Order/Options
  /// (Flags.HasVarData) delegate to VariableSummaryReader for the full
  /// MessagePack blob. Dom/Order processing remains DomGrain's job —
  /// the reader only materializes the Summary.
  ///
  /// Use TickFileConverter to build .ticks/.var files from LiteDB (one-time).
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
    public int Volume;      // offset 48
    public int VarOffset;   // offset 52 — byte offset into .var file
    public int VarLength;   // offset 56 — blob length in .var file
    public int Flags;       // offset 60
  } // 64 bytes total

  public class FixedSummaryReader : ISummaryReader
  {
    static readonly int RecordSize = Marshal.SizeOf<TickRecord>();
    const int FlagHasVarData = 1;

    public string Name { get; }

    readonly MemoryMappedFile mmf;
    readonly MemoryMappedViewAccessor accessor;
    readonly VariableSummaryReader varReader;
    readonly long count;

    long position = -1;
    Summary current;

    public Summary Current => current;

    public FixedSummaryReader(string source, string name)
    {
      Name = name;

      var ticksPath = Path.Combine(source, $"{name}.ticks");
      mmf = MemoryMappedFile.CreateFromFile(ticksPath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
      accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

      // No header — count derived from file size
      count = new FileInfo(ticksPath).Length / RecordSize;

      // Companion variable file (may not exist if no tick had var data)
      var varPath = Path.Combine(source, $"{name}.var");
      varReader = File.Exists(varPath) ? new VariableSummaryReader(varPath) : null;
    }

    public bool MoveNext()
    {
      if (position + 1 >= count) return false;
      position++;

      var recordOffset = position * RecordSize;

      // Direct struct copy from the mapped view — single 64-byte read.
      accessor.Read(recordOffset, out TickRecord rec);

      if ((rec.Flags & FlagHasVarData) is not 0 && rec.VarLength > 0 && varReader is not null)
      {
        // Rare path: full Summary from variable file
        current = varReader.ReadSummary(rec.VarOffset, rec.VarLength);
      }
      else
      {
        // Hot path: minimal Summary from fixed record (no deserialization)
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
            }
          }
        };
      }

      return true;
    }

    public void Dispose()
    {
      varReader?.Dispose();
      accessor?.Dispose();
      mmf?.Dispose();
    }
  }
}
