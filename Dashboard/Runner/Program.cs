using Canvas.Core.Services;
using Core.Conventions;
using Core.Enums;
using Core.Groups;
using Core.Models;
using Core.Services;
using Estimator.Models;
using Estimator.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Providers;
using Orleans.Serialization;
using Simulation;
using Simulation.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Runner.Strategies.CarverFastMr;
using Runner.Strategies.ConnorsPctB;
using Runner.Strategies.KeltnerMr;
using Runner.Strategies.LeadLag;
using Runner.Strategies.Ou;
using Runner.Strategies.Supertrend;
using Runner.Strategies.TsMomentum;
using Runner.Strategies.WilliamsR;

namespace Runner
{
  internal class Program
  {
    static async Task Main(string[] args)
    {
      var builder = WebApplication.CreateBuilder(args);

      builder.Configuration
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
        .AddEnvironmentVariables()
        .AddCommandLine(args);

      var setup = builder.Configuration;

      builder.Host.UseOrleans(orleans =>
      {
        orleans.UseLocalhostClustering();
        orleans.AddMemoryGrainStorageAsDefault();
        orleans.AddMemoryStreams<DefaultMemoryMessageBodySerializer>(nameof(Message));
        orleans.AddMemoryGrainStorage("PubSubStore");
        // orleans.AddDashboard(); // disabled: headless backtest, dashboard is pure overhead
        orleans.Configure<GrainCollectionOptions>(options =>
        {
          options.CollectionAge = TimeSpan.FromDays(100);
          options.CollectionQuantum = TimeSpan.FromDays(1);
          options.DeactivationTimeout = TimeSpan.FromDays(1);
        });

        orleans.ConfigureServices(services =>
        {
          var converter = new ConversionService();

          services.AddSingleton(converter.MessageOptions);
          services.AddSerializer(serBuilder => serBuilder.AddMessagePackSerializer(
            o => true,
            o => true,
            o => o.Configure(options => options.SerializerOptions = converter.MessageOptions)));
        });
      });

      builder.Services.AddSingleton<StrategyContext>();
      builder.Services.AddSingleton<IStrategy, CarverFastMrStrategy>();
      builder.Services.AddSingleton<Strategy>();
      builder.Services.AddSingleton<StateService>();
      builder.Services.AddSingleton<SchedulerService>();
      //builder.Services.AddSingleton(o => new LogService("D:/Code/NET/Terminal/Logs"));

      var app = builder.Build();
      var strategy = app.Services.GetRequiredService<Strategy>();

      await app.StartAsync();
      strategy.Setup();
      await strategy.Adapter.Connect();
      await app.WaitForShutdownAsync();
    }
  }

  public class Strategy(IClusterClient connector, IConfiguration configuration, IHostApplicationLifetime lifetime, IStrategy strategy, StrategyContext context)
  {
    protected virtual IClusterClient Connector { get; set; } = connector;
    protected virtual IConfiguration Configuration { get; set; } = configuration;
    protected virtual IHostApplicationLifetime Lifetime { get; set; } = lifetime;

    /// <summary>
    /// Primary gateway
    /// </summary>
    public virtual IGateway Adapter { get; set; }

    TimeGroup SpanDay { get; set; }
    TimeGroup SpanMin { get; set; }
    TimeGroup SpanHour { get; set; }
    int PosDirection { get; set; }
    int Direction { get; set; }
    long endTime;
    int finalized;

    // Bar timeframe (minutes) for bar-building strategies in this run.
    const int BarMinutes = 60;

    // Non-RTH test mode: when true, entries are allowed only outside regular
    // trading hours (18:00-09:30 ET); any open position is force-flattened at
    // the RTH open. Topstep rules still apply on top.
    const bool NonRthOnly = false;

    // Screening runs: Topstep guard disabled entirely when false
    // (no halt window, no daily stop, entries never blocked).
    const bool TopstepEnabled = false;

    // --- Topstep guard + run window (host-level, applies to every strategy) ---
    long runEndNs;                 // window end (unix ns): first tick >= this ends the run
    long lastTopstepMinute = -1;   // last ET minute the Topstep check ran
    DateTime topstepDay;           // current ET calendar day being tracked
    bool dayStopped;               // $1000 daily loss stop hit for topstepDay
    bool windowClosed;             // window-end flatten already issued
    readonly Dictionary<string, double> lastMid = new();

    const double TickSize = 0.25;

    Dictionary<string, Instrument> Instruments = new()
    {
      ["ES"] = new() { Name = "ES", Leverage = 50, Commission = 1.0, StepSize = 0.25 },
      //["NQ"] = new() { Name = "NQ", Leverage = 20, Commission = 1.0, StepSize = 0.25 },
    };

    public void Setup()
    {
      var adapter = Adapter = new SimGateway
      {
        Connector = Connector,
        Source = "/home/hatch/workspace/terminal-backtester/Data/FUTS",
        Account = new()
        {
          Descriptor = "Demo",
          Balance = 25000,
          Instruments = Instruments
        }
      };

      SpanDay = new() { TimeFrame = TimeSpan.FromDays(1) };
      SpanMin = new() { TimeFrame = TimeSpan.FromMinutes(1) };
      SpanHour = new() { TimeFrame = TimeSpan.FromHours(1) };

      // End of available tick history (timestamps are unix-ns): finalize when reached.
      // Read up-front from the last 56-byte record of the .ticks file
      // (Time is the first field); the sim opens the file afterwards.
      var ticksPath = Path.Combine(((SimGateway)adapter).Source, "ES.ticks");
      using (var ticksFs = new FileStream(ticksPath, FileMode.Open, FileAccess.Read, FileShare.Read))
      {
        var buffer = new byte[8];
        ticksFs.Seek(-56, SeekOrigin.End);
        ticksFs.ReadExactly(buffer, 0, 8);
        endTime = BitConverter.ToInt64(buffer, 0);
      }

      // Run window: FULL data file. Cutoff Jul 18 2026 00:00 ET is past the
      // last tick, so the clamp below sets runEndNs = endTime (end of stream
      // flattens + finalizes).
      var runEndLocal = new DateTime(2026, 7, 18, 0, 0, 0, DateTimeKind.Unspecified);
      var runEndUtc = TimeZoneInfo.ConvertTimeToUtc(runEndLocal, StrategyContext.EtZone);
      runEndNs = (runEndUtc.Ticks - DateTime.UnixEpoch.Ticks) * 100L;
      if (endTime > 0 && endTime < runEndNs) runEndNs = endTime;

      adapter.OnInstrument = async instrument =>
      {
        await OnTick(instrument);
      };

      // End of history: the final summaries carry no price updates, so the last
      // record arrives here (its order time equals endTime). Shut down from a
      // separate thread: StopApplication must not run on the grain callback thread.
      adapter.OnDomOrder = order =>
      {
        if (order is not null && endTime > 0 && order.Time >= endTime)
        {
          _ = Task.Run(() => FinalizeAsync());
        }

        return Task.CompletedTask;
      };

      // Wire the strategy context now that the gateway exists.
      context.BarMinutes = BarMinutes;
      context.DataEndNs = endTime;
      context.GetAdapter = () => Adapter;
      context.Instruments = Instruments;
      context.RequestShutdownAsync = () =>
      {
        _ = Task.Run(() => FinalizeAsync());
        return Task.CompletedTask;
      };
    }

    async Task FinalizeAsync()
    {
      if (Interlocked.Exchange(ref finalized, 1) != 0) return;

      var openPos = await Adapter.GetPositions(new() { Source = true });
      Console.WriteLine("OPENPOSITIONS " + JsonConvert.SerializeObject(openPos.Data.Select(p => new
      {
        Name = p.Operation.Instrument.Name,
        p.Operation.Amount,
        Side = p.Side?.ToString(),
        p.Operation.AveragePrice
      })));

      var metrics = await GetMetrics(Adapter);

      Console.WriteLine(JsonConvert.SerializeObject(metrics, Formatting.Indented));

      var transactions = await GetTransactionLog(Adapter);

      Console.WriteLine("TRANSACTIONS");
      Console.WriteLine(JsonConvert.SerializeObject(transactions, Formatting.Indented));
      Console.Out.Flush();

      await Adapter.Disconnect();
      Lifetime.StopApplication();
    }

    public async Task OnTick(Instrument instrument)
    {
      var price = instrument.Price;
      var bid = price.Bid ?? double.NaN;
      var ask = price.Ask ?? double.NaN;
      if (double.IsNaN(bid) || double.IsNaN(ask)) return;
      if ((ask - bid) / TickSize > 3) return; // skip insane quotes (see tick study)
      var mid = (bid + ask) / 2;
      var t = price.Time.Value;

      lastMid[instrument.Name] = mid;

      // Run window end: flatten once and finalize; later ticks are ignored.
      if (runEndNs > 0 && t >= runEndNs)
      {
        if (!windowClosed)
        {
          windowClosed = true;
          await context.ClosePosition();
          (strategy as IForcedFlatAware)?.OnForcedFlat();
          _ = Task.Run(() => FinalizeAsync());
        }
        return;
      }

      // Topstep evaluation once per minute of data (cheap; not per tick).
      // Skipped entirely on screening runs (TopstepEnabled = false).
      var minute = t / 60000000000L;
      if (TopstepEnabled && minute != lastTopstepMinute)
      {
        lastTopstepMinute = minute;
        await EvaluateTopstepAsync(t);
      }

      await strategy.OnTickAsync(new StrategyTick(instrument.Name, bid, ask, mid, t));
    }

    /// <summary>
    /// Topstep rules, evaluated once per data minute:
    /// - no trading 16:00-18:00 ET: entries blocked, open position force-flattened;
    /// - max $1000 loss per ET calendar day (realized + unrealized at last mid):
    ///   on breach, flatten and block entries until the next ET day.
    /// </summary>
    async Task EvaluateTopstepAsync(long t)
    {
      var et = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(t / 1000000L), StrategyContext.EtZone);
      var day = et.Date;

      if (day != topstepDay)
      {
        topstepDay = day;
        dayStopped = false;
      }

      var secs = et.TimeOfDay.TotalSeconds;
      var inHalt = secs >= 16 * 3600 && secs < 18 * 3600;
      var inRth = secs >= 9.5 * 3600 && secs < 16 * 3600;

      var positions = (await Adapter.GetPositions(new() { Source = true })).Data;
      var hasPosition = positions is not null && positions.Count > 0;

      var needFlat = false;

      if (inHalt && hasPosition) needFlat = true;
      if (NonRthOnly && inRth && hasPosition) needFlat = true;

      if (!dayStopped)
      {
        var dayPnl = await DayPnlAsync(day, positions);
        if (dayPnl <= -1000.0) { dayStopped = true; needFlat = true; }
      }

      context.EntriesBlocked = inHalt || dayStopped || (NonRthOnly && inRth);

      if (needFlat && hasPosition)
      {
        await context.ClosePosition();
        (strategy as IForcedFlatAware)?.OnForcedFlat();
      }
    }

    /// <summary>Realized PnL for the ET day + unrealized on open positions.</summary>
    async Task<double> DayPnlAsync(DateTime day, IList<Order> positions)
    {
      double pnl = 0;

      var txns = (await Adapter.GetTransactions(default)).Data;
      foreach (var txn in txns)
      {
        if (txn?.Operation?.Time is null || txn.Balance?.Current is null) continue;
        var et = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(txn.Operation.Time.Value / 1000000L), StrategyContext.EtZone);
        if (et.Date == day) pnl += txn.Balance.Current.Value;
      }

      if (positions is not null)
      {
        foreach (var position in positions)
        {
          var op = position.Operation;
          if (op?.Instrument is null || op.AveragePrice is null || op.Amount is null) continue;
          if (!lastMid.TryGetValue(op.Instrument.Name, out var mid)) continue;
          var dir = position.Side is OrderSideEnum.Long ? 1.0 : -1.0;
          var pointValue = Instruments.TryGetValue(op.Instrument.Name, out var instr) ? instr.Leverage ?? 0 : 0;
          pnl += dir * (mid - op.AveragePrice.Value) * op.Amount.Value * pointValue;
        }
      }

      return pnl;
    }

    /// <summary>
    /// Completed transactions ordered by close time
    /// </summary>
    /// <param name="adapters"></param>
    public virtual async Task<IList<object>> GetTransactionLog(params IGateway[] adapters)
    {
      var actionResponses = await Task.WhenAll(adapters.Select(o => o.GetTransactions(default)));
      return actionResponses
        .SelectMany(o => o.Data)
        .Where(o => o?.Operation?.Time is not null)
        .OrderBy(o => o.Operation.Time)
        .Select(o => (object)new
        {
          Time = new DateTime(o.Operation.Time.Value),
          Name = o.Operation.Instrument?.Name,
          Side = o.Side?.ToString(),
          Amount = o.Operation.Amount,
          EntryPrice = o.Operation.AveragePrice,
          ExitPrice = o.Operation.Price,
          Profit = o.Balance.Current,
          Commission = o.Operation.Instrument?.Commission
        })
        .ToList();
    }

    /// <summary>
    /// Trading metrics
    /// </summary>
    /// <param name="adapters"></param>
    public virtual async Task<IDictionary<string, IList<ScoreData>>> GetMetrics(params IGateway[] adapters)
    {
      var values = new List<InputData>();
      var actionResponses = await Task.WhenAll(adapters.Select(o => o.GetTransactions(default)));
      var balance = adapters.Sum(o => o.Account.Balance ?? 0);
      var actions = actionResponses
        .SelectMany(o => o.Data)
        .Where(o => o?.Operation?.Time is not null)
        .OrderBy(o => o.Operation.Time)
        .ToList();

      if (actions.Count > 0)
      {
        values.Add(new InputData
        {
          Time = new DateTime(actions.First().Operation.Time.Value),
          Value = 0,
          Min = 0,
          Max = 0
        });
      }

      foreach (var o in actions)
      {
        values.Add(new InputData
        {
          Min = o.Balance.Min ?? 0,
          Max = o.Balance.Max ?? 0,
          Value = o.Balance.Current ?? 0,
          Time = new DateTime(o.Operation.Time.Value),
          Direction = o.Side is OrderSideEnum.Long ? 1 : -1,
          Commission = o.Operation.Instrument.Commission.Value
        });
      }

      return new Score { Items = values, Balance = balance }.Calculate();
    }
  }
}
