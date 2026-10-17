using Core.Enums;

namespace Core.Models
{
  [GenerateSerializer]
  public record Currency
  {
    /// <summary>
    /// Currency
    /// </summary>
    [Id(0)] public string Name { get; init; } = nameof(CurrencyEnum.USD);

    /// <summary>
    /// Long swap rate for keeping position overnight
    /// </summary>
    [Id(1)] public double? SwapLong { get; init; } = 0;

    /// <summary>
    /// Short swap rate for keeping position overnight
    /// </summary>
    [Id(2)] public double? SwapShort { get; init; } = 0;
  }
}
