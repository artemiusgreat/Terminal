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

    /// <summary>Bar timeframe (minutes) for bar-building strategies. Set by the pipeline in Setup().</summary>
    public int BarMinutes { get; set; } = 1;

    /// <summary>Bar length in nanoseconds (BarMinutes * 60e9).</summary>
    public long BarNs => BarMinutes * 60000000000L;

    /// <summary>Tradable instruments by symbol. Set by the pipeline in Setup().</summary>
    public IReadOnlyDictionary<string, Instrument> Instruments { get; set; } =
      new Dictionary<string, Instrument>();

    /// <summary>
    /// Topstep gate: while true, OpenPosition is refused (returns false).
    /// Set by the pipeline during the 16:00-18:00 ET halt and after the
    /// $1000 daily stop triggers. Closing is never blocked.
    /// </summary>
    public bool EntriesBlocked { get; set; }

    /// <summary>Requests pipeline shutdown (idempotent). Set by the pipeline in Setup().</summary>
    public Func<Task> RequestShutdownAsync { get; set; } = () => Task.CompletedTask;

    /// <summary>The gateway. Only valid after Setup().</summary>
    public IGateway Adapter => GetAdapter();

    public static readonly TimeZoneInfo EtZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    /// <summary>Seconds since midnight ET for a unix-nanosecond timestamp.</summary>
    public static double EtSecondsOfDay(long timeNs) =>
      TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(timeNs / 1000000L), EtZone).TimeOfDay.TotalSeconds;

    /// <summary>Opens a market position, optionally with braced SL/TP.
    /// Returns false when the Topstep gate blocks entries (nothing sent).</summary>
    public virtual async Task<bool> OpenPosition(string symbol, OrderSideEnum side, double amount = 1, double? sl = null, double? tp = null)
    {
      if (EntriesBlocked) return false;

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
      return true;
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
