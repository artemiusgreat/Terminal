using System;

namespace Core.Models
{
  [GenerateSerializer]
  public record Price
  {
    /// <summary>
    /// Bid
    /// </summary>
    [Id(0)] public double? Bid { get; init; }

    /// <summary>
    /// Ask
    /// </summary>
    [Id(1)] public double? Ask { get; init; }

    /// <summary>
    /// Volume of the bid 
    /// </summary>
    [Id(2)] public double? BidSize { get; init; }

    /// <summary>
    /// Volume of the ask
    /// </summary>
    [Id(3)] public double? AskSize { get; init; }

    /// <summary>
    /// Last price or value
    /// </summary>
    [Id(4)] public double? Last { get; init; }

    /// <summary>
    /// Instrument volume
    /// </summary>
    [Id(5)] public double? Volume { get; init; }

    /// <summary>
    /// Time stamp
    /// </summary>
    [Id(6)] public long? Time { get; init; }

    /// <summary>
    /// Reference to the complex data point
    /// </summary>
    [Id(7)] public Bar Bar { get; init; }
  }
}
