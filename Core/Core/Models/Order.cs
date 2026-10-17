using Core.Enums;
using System.Collections.Generic;

namespace Core.Models
{
  [GenerateSerializer]
  public record Order
  {
    /// <summary>
    /// Client order ID
    /// </summary>
    [Id(0)] public string Id { get; init; }

    /// <summary>
    /// Group
    /// </summary>
    [Id(1)] public string Descriptor { get; init; }

    /// <summary>
    /// Time stamp
    /// </summary>
    [Id(2)] public long? Time { get; init; }

    /// <summary>
    /// Contract size
    /// </summary>
    [Id(3)] public double? Amount { get; init; }

    /// <summary>
    /// Price the makes order active, e.g. limit price for stop limit order
    /// </summary>
    [Id(4)] public double? ActivationPrice { get; init; }

    /// <summary>
    /// Desired price for the order to fill, e.g. stop price for stop order and limit price for limit order
    /// </summary>
    [Id(5)] public double? Price { get; init; }

    /// <summary>
    /// Type
    /// </summary>
    [Id(6)] public OrderTypeEnum? Type { get; init; } = OrderTypeEnum.Market;

    /// <summary>
    /// Side
    /// </summary>
    [Id(7)] public OrderSideEnum? Side { get; init; }

    /// <summary>
    /// Time in force
    /// </summary>
    [Id(8)] public OrderTimeSpanEnum? TimeSpan { get; init; } = OrderTimeSpanEnum.GTC;

    /// <summary>
    /// Custom order type
    /// </summary>
    [Id(9)] public InstructionEnum? Instruction { get; init; }

    /// <summary>
    /// Account
    /// </summary>
    [Id(10)] public Account Account { get; init; }

    /// <summary>
    /// Balance
    /// </summary>
    [Id(11)] public Balance Balance { get; init; } = new();

    /// <summary>
    /// Transaction
    /// </summary>
    [Id(12)] public Operation Operation { get; init; }

    /// <summary>
    /// List of related orders in the hierarchy
    /// </summary>
    [Id(13)] public List<Order> Orders { get; init; } = [];
  }
}
