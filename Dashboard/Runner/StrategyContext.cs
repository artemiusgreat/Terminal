using Core.Conventions;
using Core.Enums;
using Core.Models;
using Simulation;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Runner
{
  /// <summary>
  /// Services a strategy needs from the pipeline. Registered as a singleton;
  /// the pipeline (<see cref="Strategy"/>) fills in the gateway, instruments
  /// and shutdown hook in <c>Setup()</c>, before any tick is delivered.
  /// </summary>
  public class StrategyContext
  {
    /// <summary>Gateway accessor. Set by the pipeline in Setup().</summary>
    public Func<IGateway> GetAdapter { get; set; } =
      () => throw new InvalidOperationException("StrategyContext not initialized: Strategy.Setup() has not run.");

    /// <summary>Tradable instruments by symbol. Set by the pipeline in Setup().</summary>
    public IReadOnlyDictionary<string, Instrument> Instruments { get; set; } =
      new Dictionary<string, Instrument>();

    /// <summary>Requests pipeline shutdown (idempotent). Set by the pipeline in Setup().</summary>
    public Func<Task> RequestShutdownAsync { get; set; } = () => Task.CompletedTask;

    /// <summary>The gateway. Only valid after Setup().</summary>
    public IGateway Adapter => GetAdapter();

    public static readonly TimeZoneInfo EtZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    /// <summary>Seconds since midnight ET for a unix-nanosecond timestamp.</summary>
    public static double EtSecondsOfDay(long timeNs) =>
      TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(timeNs / 1000000L), EtZone).TimeOfDay.TotalSeconds;

    /// <summary>Opens a market position, optionally with braced SL/TP.</summary>
    public virtual async Task OpenPosition(string symbol, OrderSideEnum side, double amount = 1, double? sl = null, double? tp = null)
    {
      var asset = Instruments[symbol];
      var order = new Order
      {
        Side = side,
        Amount = amount,
        Type = OrderTypeEnum.Market,
        Operation = new() { Instrument = asset }
      };

      if (sl is not null)
      {
        order = order with
        {
          Orders = [..order.Orders,
            new()
            {
              Price = sl,
              Amount = amount,
              Type = OrderTypeEnum.Stop,
              Instruction = InstructionEnum.Brace,
              Side = side is OrderSideEnum.Long ? OrderSideEnum.Short : OrderSideEnum.Long,
              Operation = new() { Instrument = asset }
            }
          ]
        };
      }

      if (tp is not null)
      {
        order = order with
        {
          Orders = [..order.Orders,
            new()
            {
              Price = tp,
              Amount = amount,
              Type = OrderTypeEnum.Limit,
              Instruction = InstructionEnum.Brace,
              Side = side is OrderSideEnum.Long ? OrderSideEnum.Short : OrderSideEnum.Long,
              Operation = new() { Instrument = asset }
            }
          ]
        };
      }

      await Adapter.SendOrder(order);
    }

    /// <summary>Closes all open positions at market.</summary>
    public virtual async Task ClosePosition()
    {
      var positions = await Adapter.GetPositions(new() { Source = true });

      foreach (var position in positions.Data)
      {
        var order = new Order
        {
          Amount = position.Operation.Amount,
          Side = position.Side is OrderSideEnum.Long ? OrderSideEnum.Short : OrderSideEnum.Long,
          Type = OrderTypeEnum.Market,
          Operation = new()
          {
            Instrument = position.Operation.Instrument
          }
        };

        await Adapter.SendOrder(order);
      }
    }
  }
}
