using Canvas.Core.Services;
using Core.Conventions;
using Core.Enums;
using Core.Groups;
using Core.Models;
using Core.Services;
using Estimator.Models;
using Estimator.Services;
using LiteDB;
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
      // <-- SWITCH STRATEGY HERE: one line selects the active strategy
      builder.Services.AddSingleton<IStrategy, ConnorsPctBStrategy>();
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

    const double TickSize = 0.25;

    Dictionary<string, Instrument> Instruments = new()
    {
      ["ES"] = new() { Name = "ES", Leverage = 50, Commission = 1.0, StepSize = 0.25 },
      ["NQ"] = new() { Name = "NQ", Leverage = 20, Commission = 1.0, StepSize = 0.25 },
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
      // Queried up-front; the sim opens the file afterwards.
      using (var storage = new LiteDatabase(Path.Combine(((SimGateway)adapter).Source, "ES.db")))
      {
        endTime = storage.GetCollection<Summary>("prices").Query()
          .OrderByDescending(o => o.Id).FirstOrDefault()?.Time ?? 0;
      }

      adapter.OnInstrument = async instrument =>
      {
        await OnTick(instrument);
      };

      // End of history: the final summaries carry no price updates, so the last
      // record arrives here (its order time equals endTime). Shut down from a
      // separate thread: StopApplication must not run on the grain callback thread.
      adapter.OnDomOrder = order =>
      {
        if (endTime > 0 && order.Time >= endTime)
        {
          _ = Task.Run(() => FinalizeAsync());
        }

        return Task.CompletedTask;
      };

      // Wire the strategy context now that the gateway exists.
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
      await strategy.OnTickAsync(new StrategyTick(instrument.Name, bid, ask, mid, price.Time.Value));
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
