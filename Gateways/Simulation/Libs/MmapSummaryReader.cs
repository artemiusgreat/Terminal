using Core.Models;
using Simulation.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace Simulation
{
  /// <summary>
  /// Binary tick file format (version 1).
  /// Header (32 bytes) followed by fixed-size records.
  /// Designed for sequential replay: O(1) seek, zero deserialization.
  /// </summary>
  [StructLayout(LayoutKind.Sequential, Pack = 1)]
  public struct TickRecord
  {
    public long TimeNs;     // 8: nanoseconds since Unix epoch
    public int BidTicks;    // 4: best bid in ticks (int)
    public int AskTicks;    // 4: best ask in ticks
    public int BidSize;     // 4
    public int AskSize;     // 4
    public int TradeTicks;  // 4: last trade price in ticks (0 = no trade on this tick)
    public int TradeSize;   // 4
    public int Flags;       // 4: bit 0 = has trade; reserved for future use
    // Total: 40 bytes
  }

  [StructLayout(LayoutKind.Sequential, Pack = 1)]
  public struct TickFileHeader
  {
    public int Magic;       // 4: 0x5449434B ("TICK")
    public int Version;     // 4: 1
    public int RecordSize;  // 4: sizeof(TickRecord) = 40
    public long Count;      // 8: number of records
    public double TickSize; // 8: e.g. 0.25 for ES (to convert ticks -> price)
    public int Reserved;    // 4: padding to 32 bytes
  }

  /// <summary>
  /// Memory-mapped ISummaryReader. Reads fixed-size binary tick records
  /// with zero deserialization — bytes are cast directly to structs.
  /// Reconstructs a minimal Summary (Time + top-of-book Dom + trade Instrument)
  /// sufficient for tick-based strategies that don't need full MBO depth.
  ///
  /// Use TickFileConverter to build .ticks files from LiteDB sources (one-time).
  /// </summary>
  public class MmapSummaryReader : ISummaryReader
  {
    public const int Magic = 0x5449434B;
    public const int CurrentVersion = 1;

    public string Name { get; }

    readonly MemoryMappedFile mmf;
    readonly MemoryMappedViewAccessor accessor;
    readonly long count;
    readonly double tickSize;
    readonly TickRecord[] reusable = new TickRecord[1];

    long position = -1;
    Summary current;
    bool disposed;

    public Summary Current => current;

    public MmapSummaryReader(string source, string instrumentName)
    {
      Name = instrumentName;
      // source is a directory; binary files are named {instrument}.ticks
      var path = Path.Combine(source, $"{instrumentName}.ticks");
      if (!File.Exists(path))
        throw new FileNotFoundException($"Tick file not found: {path}. Run TickFileConverter first.", path);

      mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
      accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

      TickFileHeader header;
      accessor.Read(0, out header);
      if (header.Magic != Magic)
        throw new InvalidDataException($"Bad magic in {path}: 0x{header.Magic:X8}");
      if (header.Version != CurrentVersion)
        throw new InvalidDataException($"Unsupported version {header.Version} in {path}");
      if (header.RecordSize != Marshal.SizeOf<TickRecord>())
        throw new InvalidDataException($"Record size mismatch in {path}");

      count = header.Count;
      tickSize = header.TickSize != 0 ? header.TickSize : 0.25;
    }

    public bool MoveNext()
    {
      if (position + 1 >= count) return false;
      position++;

      long offset = Marshal.SizeOf<TickFileHeader>() + position * Marshal.SizeOf<TickRecord>();
      TickRecord rec;
      accessor.Read(offset, out rec);

      current = ToSummary(rec);
      return true;
    }

    Summary ToSummary(TickRecord rec)
    {
      var bid = rec.BidTicks * tickSize;
      var ask = rec.AskTicks * tickSize;

      // Minimal top-of-book Dom (strategies needing full MBO depth should use LiteDB)
      var dom = new Dom
      {
        Bids = new SortedDictionary<long, LinkedList<DomOrder>>(
          Comparer<long>.Create((x, y) => y.CompareTo(x))),
        Asks = new SortedDictionary<long, LinkedList<DomOrder>>(),
      };
      if (rec.BidTicks != 0)
        dom.Bids[(long)(bid * 1e8)] = new LinkedList<DomOrder>();
      if (rec.AskTicks != 0)
        dom.Asks[(long)(ask * 1e8)] = new LinkedList<DomOrder>();

      Instrument instrument = null;
      if ((rec.Flags & 1) != 0 && rec.TradeTicks != 0)
      {
        instrument = new Instrument
        {
          Name = Name,
          // Price carried via StepValue-adjacent fields; strategies read bid/ask from Dom
        };
      }

      return new Summary
      {
        Time = rec.TimeNs,
        Dom = (rec.BidTicks != 0 || rec.AskTicks != 0) ? dom : null,
        Instrument = instrument,
      };
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
