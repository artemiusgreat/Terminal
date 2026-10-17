using MessagePack;
using MessagePack.Resolvers;
using Simulation.Models;
using System;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace Simulation
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
  public class MmapSummaryReader : ISummaryReader
  {
    public const int Magic = 0x5449434B; // "TICK"
    public const int CurrentVersion = 2;
    const int HeaderSize = 24;
    const int RecordSize = 48;

    const int FlagHasVarData = 1;

    // 10_000x multiplier for price->ticks conversion (4 decimal places)
    const double PriceScale = 10000.0;

    static readonly MessagePackSerializerOptions MsgPackOptions =
      ContractlessStandardResolver.Options;

    public string Name { get; }

    readonly MemoryMappedFile mmf;
    readonly MemoryMappedViewAccessor accessor;
    readonly long count;
    readonly long varRegionOffset;

    long position = -1;
    Summary current;
    bool disposed;

    // Reusable buffer for fixed record reads (48 bytes)
    readonly byte[] recordBuf = new byte[RecordSize];

    public Summary Current => current;

    public MmapSummaryReader(string source, string instrumentName)
    {
      Name = instrumentName;
      var path = Path.Combine(source, $"{instrumentName}.ticks");
      if (!File.Exists(path))
        throw new FileNotFoundException($"Tick file not found: {path}. Run TickFileConverter first.", path);

      mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
      accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

      var magic = accessor.ReadInt32(0);
      var version = accessor.ReadInt32(4);
      if (magic != Magic)
        throw new InvalidDataException($"Bad magic in {path}: 0x{magic:X8}");
      if (version != CurrentVersion)
        throw new InvalidDataException($"Unsupported version {version} in {path} (expected {CurrentVersion})");

      count = accessor.ReadInt64(8);
      varRegionOffset = accessor.ReadInt64(16);
    }

    public bool MoveNext()
    {
      if (position + 1 >= count) return false;
      position++;

      var recordOffset = HeaderSize + position * RecordSize;
      accessor.ReadArray(recordOffset, recordBuf, 0, RecordSize);

      var timeNs = BitConverter.ToInt64(recordBuf, 0);
      var bidTicks = BitConverter.ToInt32(recordBuf, 8);
      var askTicks = BitConverter.ToInt32(recordBuf, 12);
      var tradeTicks = BitConverter.ToInt32(recordBuf, 16);
      var bidSize = BitConverter.ToInt32(recordBuf, 20);
      var askSize = BitConverter.ToInt32(recordBuf, 24);
      var tradeSize = BitConverter.ToInt32(recordBuf, 28);
      var varOffset = BitConverter.ToInt32(recordBuf, 32);
      var varLength = BitConverter.ToInt32(recordBuf, 36);
      var flags = BitConverter.ToInt32(recordBuf, 40);
      // bytes 44-47: reserved

      if ((flags & FlagHasVarData) != 0 && varLength > 0)
      {
        // Rare path: deserialize full Summary from variable region
        var blob = new byte[varLength];
        accessor.ReadArray(varRegionOffset + varOffset, blob, 0, varLength);
        current = MessagePackSerializer.Deserialize<Summary>(blob, MsgPackOptions);
      }
      else
      {
        // Hot path: build minimal Summary from fixed record (no deserialization)
        current = new Summary
        {
          Time = timeNs,
          Instrument = new Core.Models.Instrument
          {
            Name = Name,
            Price = new Core.Models.Price
            {
              Time = timeNs,
              Bid = bidTicks / PriceScale,
              Ask = askTicks / PriceScale,
              BidSize = bidSize,
              AskSize = askSize,
            }
          }
        };
      }

      return true;
    }

    public void Dispose()
    {
      if (disposed) return;
      disposed = true;
      accessor?.Dispose();
      mmf?.Dispose();
    }
  }
}
