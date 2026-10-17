using Simulation.Models;
using System;

namespace Simulation
{
  /// <summary>
  /// Abstraction over historical tick data sources.
  /// Implementations: LiteDB (BSON documents), memory-mapped flat binary, etc.
  /// The backtester consumes summaries sequentially via MoveNext/Current —
  /// implementations are free to use whatever storage fits the access pattern.
  /// </summary>
  public interface ISummaryReader : IDisposable
  {
    /// <summary>
    /// Instrument name (e.g. "ES")
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Current summary. Valid after MoveNext() returns true.
    /// </summary>
    Summary Current { get; }

    /// <summary>
    /// Advance to the next summary. Returns false at end of stream.
    /// </summary>
    bool MoveNext();
  }

  /// <summary>
  /// Selects which ISummaryReader implementation to use.
  /// Set via configuration or code — no changes to the grain required.
  /// </summary>
  public enum SummaryReaderKind
  {
    LiteDb,
    MemoryMapped
  }

  public static class SummaryReaderFactory
  {
    /// <summary>
    /// Creates a reader for the given instrument.
    /// source is the data directory; the file name is resolved per kind
    /// ({name}.db for LiteDB, {name}.ticks for memory-mapped binary).
    /// </summary>
    public static ISummaryReader Create(SummaryReaderKind kind, string source, string instrumentName)
      => kind switch
      {
        SummaryReaderKind.MemoryMapped => new MmapSummaryReader(source, instrumentName),
        _ => new LiteDbSummaryReader(System.IO.Path.Combine(source, $"{instrumentName}.db"), instrumentName),
      };
  }
}
