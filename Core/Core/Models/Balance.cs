namespace Core.Models
{
  [GenerateSerializer]
  public record Balance
  {
    /// <summary>
    /// Current PnL
    /// </summary>
    [Id(0)] public double? Current { get; init; }

    /// <summary>
    /// Min possible PnL in account's currency
    /// </summary>
    [Id(1)] public double? Min { get; init; }

    /// <summary>
    /// Max possible PnL in account's currency
    /// </summary>
    [Id(2)] public double? Max { get; init; }
  }
}
