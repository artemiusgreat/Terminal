using Core.Enums;

namespace Core.Models
{
  [GenerateSerializer]
  public record Operation
  {
    /// <summary>
    /// Size of partially filled contract
    /// </summary>
    [Id(0)] public double? Amount { get; init; }

    /// <summary>
    /// Open price 
    /// </summary>
    [Id(1)] public double? AveragePrice { get; init; }

    /// <summary>
    /// Close price 
    /// </summary>
    [Id(2)] public double? Price { get; init; }

    /// <summary>
    /// Time stamp
    /// </summary>
    [Id(3)] public long? Time { get; init; }

    /// <summary>
    /// Status of the order, e.g. Pending
    /// </summary>
    [Id(4)] public OrderStatusEnum? Status { get; init; }

    /// <summary>
    /// Instrument to buy or sell
    /// </summary>
    [Id(5)] public Instrument Instrument { get; init; }
  }
}
