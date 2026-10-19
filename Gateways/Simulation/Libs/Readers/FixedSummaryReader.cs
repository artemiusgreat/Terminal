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
  /// File: {name}.ticks — raw 48-byte records back-to-back, no header.
  /// Record count is derived from file size (length / 48).
  ///
  /// Fixed record (48 bytes):
  ///   TimeNs(8) | BidTicks(4) | AskTicks(4) | TradeTicks(4) |
  ///   BidSize(4) | AskSize(4) | TradeSize(4) |
  ///   VarOffset(4) | VarLength(4) | Flags(4) | Reserved(4)
  ///
  /// Prices stored as int ticks (price × 10000). Flags bit 0 = HasVarData.
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
    public long TimeNs;     // offset 0
    public int BidTicks;    // offset 8
    public int AskTicks;    // offset 12
    public int TradeTicks;  // offset 16
    public int BidSize;     // offset 20
    public int AskSize;     // offset 24
    public int TradeSize;   // offset 28
    public int VarOffset;   // offset 32 — byte offset into .var file
    public int VarLength;   // offset 36 — blob length in .var file
    public int Flags;       // offset 40
    public int Reserved;    // offset 44
  } // 48 bytes total

  public class FixedSummaryReader : ISummaryReader
  {
    public const int RecordSize = 48;
    const int FlagHasVarData = 1;

    // 10_000x multiplier for price->ticks conversion (4 decimal places)
    const double PriceScale = 10000.0;

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

      // Direct struct copy from the mapped view — single 48-byte read.
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
          Time = rec.TimeNs,
          Instrument = new Instrument
          {
            Name = Name,
            Price = new Price
            {
              Time = rec.TimeNs,
              Bid = rec.BidTicks / PriceScale,
              Ask = rec.AskTicks / PriceScale,
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
