using Core.Models;
using MessagePack;
using MessagePack.Resolvers;
using Simulation.Models;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace Simulation.Readers
{
  /// <summary>
  /// Memory-mapped ISummaryReader with two-region layout (no MemoryPack).
  ///
  /// File format v2:
  ///   [header: magic(4) | version(4) | count(8) | varRegionOffset(8)]  // 24 bytes
  ///   [fixed records: 48 bytes each] × N
  ///   [variable region: MessagePack blobs for ticks with Dom/Order/Options]
  ///
  /// Fixed record (48 bytes):
  ///   TimeNs(8) | BidTicks(4) | AskTicks(4) | TradeTicks(4) |
  ///   BidSize(4) | AskSize(4) | TradeSize(4) |
  ///   VarOffset(4) | VarLength(4) | Flags(4) | Reserved(4)
  ///
  /// Prices stored as int ticks (price × 10000). Flags bit 0 = HasVarData.
  ///
  /// Hot path is zero-copy: fixed records are read directly from the mapped
  /// view, no deserialization. The variable region is touched ONLY when
  /// Flags.HasVarData is set (rare: Dom/Order/Options present). Dom/Order
  /// processing remains DomGrain's responsibility — the reader only
  /// materializes the Summary.
  ///
  /// Use TickFileConverter to build .ticks files from LiteDB sources (one-time).
  /// </summary>
  /// <summary>
  /// Fixed 48-byte tick record. Pack=1 guarantees no padding —
  /// the struct maps byte-for-byte onto the file layout.
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
    public int VarOffset;   // offset 32
    public int VarLength;   // offset 36
    public int Flags;       // offset 40
    public int Reserved;    // offset 44
  } // 48 bytes total

  public class MapSummaryReader : ISummaryReader
  {
    public const int Magic = 0x5449434B; // "TICK"
    public const int CurrentVersion = 2;
    const int HeaderSize = 24;
    const int RecordSize = 48;

    const int FlagHasVarData = 1;

    // 10_000x multiplier for price->ticks conversion (4 decimal places)
    const double PriceScale = 10000.0;

    static readonly MessagePackSerializerOptions MsgPackOptions = ContractlessStandardResolver.Options;

    public string Name { get; }

    readonly MemoryMappedFile mmf;
    readonly MemoryMappedViewAccessor accessor;
    readonly long count;
    readonly long varRegionOffset;

    long position = -1;
    Summary current;

    public Summary Current => current;

    public MapSummaryReader(string source, string name)
    {
      Name = name;

      var location = Path.Combine(source, $"{name}.ticks");

      mmf = MemoryMappedFile.CreateFromFile(location, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
      accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

      var magic = accessor.ReadInt32(0);
      var version = accessor.ReadInt32(4);

      count = accessor.ReadInt64(8);
      varRegionOffset = accessor.ReadInt64(16);
    }

    public bool MoveNext()
    {
      if (position + 1 >= count) return false;
      position++;

      var recordOffset = HeaderSize + position * RecordSize;

      // Direct struct copy from the mapped view — single 48-byte read,
      // no per-field BitConverter calls.
      accessor.Read(recordOffset, out TickRecord rec);

      if ((rec.Flags & FlagHasVarData) is not 0 && rec.VarLength > 0)
      {
        // Rare path: deserialize full Summary from variable region
        var sequence = new byte[rec.VarLength];
        accessor.ReadArray(varRegionOffset + rec.VarOffset, sequence, 0, rec.VarLength);
        current = MessagePackSerializer.Deserialize<Summary>(sequence, MsgPackOptions);
      }
      else
      {
        // Hot path: build minimal Summary from fixed record (no deserialization)
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
      accessor?.Dispose();
      mmf?.Dispose();
    }
  }
}
