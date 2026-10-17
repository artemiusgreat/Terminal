using System;

namespace Core.Models
{
  [Immutable]
  [GenerateSerializer]
  public record Criteria
  {
    /// <summary>
    /// Count
    /// </summary>
    [Id(0)] public int? Count { get; init; }

    /// <summary>
    /// Bypass cache
    /// </summary>
    [Id(1)] public bool Source { get; init; }

    /// <summary>
    /// Start date
    /// </summary>
    [Id(2)] public double? MinPrice { get; init; }

    /// <summary>
    /// End date
    /// </summary>
    [Id(3)] public double? MaxPrice { get; init; }

    /// <summary>
    /// Start date
    /// </summary>
    [Id(4)] public DateTime? MinDate { get; init; }

    /// <summary>
    /// End date
    /// </summary>
    [Id(5)] public DateTime? MaxDate { get; init; }

    /// <summary>
    /// Account
    /// </summary>
    [Id(6)] public Account Account { get; init; }

    /// <summary>
    /// Instrument
    /// </summary>
    [Id(7)] public Instrument Instrument { get; init; }
  }
}
