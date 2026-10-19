using Simulation.Models;
using System;
using System.IO;

namespace Simulation.Readers
{
  /// <summary>
  /// Selects which ISummaryReader implementation to use.
  /// Set via configuration or code — no changes to the grain required.
  /// </summary>
  public enum SummaryReaderEnum
  {
    Lite,
    Fixed,
    Variable
  }

  /// <summary>
  /// Abstraction over historical tick data sources.
  /// Implementations: LiteDB (BSON documents), memory-mapped flat binary, etc.
  /// The backtester consumes summaries sequentially via MoveNext/Current —
  /// implementations are free to use whatever storage fits the access pattern.
  /// </summary>
  public interface ISummaryReader : IDisposable
  {
    /// <summary>
    /// Instrument name, e.g. "ES"
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

  public static class SummaryReaderFactory
  {
    /// <summary>
    /// Creates a reader for the given instrument, source is the data directory.
    /// ({name}.db for LiteDB, {name}.ticks for fixed, {name}.vars for variable).
    /// </summary>
    public static ISummaryReader Create(SummaryReaderEnum reader, string source, string name)
    {
      switch (reader)
      {
        case SummaryReaderEnum.Fixed: return new FixedSummaryReader(source, name);
        case SummaryReaderEnum.Variable: return new VariableSummaryReader(source, name);
        case SummaryReaderEnum.Lite: return new LiteSummaryReader(Path.Combine(source, $"{name}.db"), name);
      };

      return null;
    }
  }
}
