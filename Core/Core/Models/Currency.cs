using Core.Enums;
using MemoryPack;

namespace Core.Models
{
  [MemoryPackable]
  public partial record Currency
  {
    /// <summary>
    /// Currency
    /// </summary>
    public string Name { get; init; } = nameof(CurrencyEnum.USD);

    /// <summary>
    /// Long swap rate for keeping position overnight
    /// </summary>
    public double? SwapLong { get; init; } = 0;

    /// <summary>
    /// Short swap rate for keeping position overnight
    /// </summary>
    public double? SwapShort { get; init; } = 0;
  }
}
