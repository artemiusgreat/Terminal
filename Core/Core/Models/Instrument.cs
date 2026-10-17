using Core.Enums;

namespace Core.Models
{
  [GenerateSerializer]
  public record Instrument
  {
    /// <summary>
    /// ID
    /// </summary>
    [Id(0)] public string Id { get; init; }

    /// <summary>
    /// Name
    /// </summary>
    [Id(1)] public string Name { get; init; }

    /// <summary>
    /// Exchange
    /// </summary>
    [Id(2)] public string Exchange { get; init; }

    /// <summary>
    /// Commission
    /// </summary>
    [Id(3)] public double? Commission { get; init; } = 0;

    /// <summary>
    /// Contract size for 1 trading lot in currencies and futures
    /// </summary>
    [Id(4)] public double? ContractSize { get; init; } = 1;

    /// <summary>
    /// Contract size
    /// </summary>
    [Id(5)] public double? Leverage { get; init; } = 1;

    /// <summary>
    /// Minimum price change 
    /// </summary>
    [Id(6)] public double? StepSize { get; init; } = 0.01;

    /// <summary>
    /// Price change within one tick
    /// </summary>
    [Id(7)] public double? StepValue { get; init; } = 0.01;

    /// <summary>
    /// Instrument type
    /// </summary>
    [Id(8)] public InstrumentEnum? Type { get; init; }

    /// <summary>
    /// Current price data 
    /// </summary>
    [Id(9)] public Price Price { get; init; }

    /// <summary>
    /// Undelying symbol
    /// </summary>
    [Id(10)] public Instrument Basis { get; init; }

    /// <summary>
    /// Base currency contract
    /// </summary>
    [Id(11)] public Currency Currency { get; init; } = new Currency { Name = nameof(CurrencyEnum.USD) };

    /// <summary>
    /// Options and futures specification
    /// </summary>
    [Id(12)] public Derivative Derivative { get; init; }
  }
}
