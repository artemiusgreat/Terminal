using Core.Enums;

namespace Core.Models
{
  [Immutable]
  [GenerateSerializer]
  public record PriceCriteria : Criteria
  {
    /// <summary>
    /// Duration
    /// </summary>
    [Id(8)] public int? Frame { get; init; }

    /// <summary>
    /// Duration
    /// </summary>
    [Id(9)] public int? Duration { get; init; }

    /// <summary>
    /// Price type - bars, trades, ticks
    /// </summary>
    [Id(10)] public PriceTypeEnum PriceType { get; init; }

    /// <summary>
    /// Period type - day, hour, minute
    /// </summary>
    [Id(11)] public string FrameType { get; init; }

    /// <summary>
    /// Duration type - year, month, day
    /// </summary>
    [Id(12)] public string DurationType { get; init; }
  }
}
