using Canvas.Core.Services;
using Core.Conventions;
using Core.Enums;
using Core.Groups;
using Core.Models;
using Core.Services;
using Estimator.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using LiteDB;
using Simulation.Models;
using System.IO;
using System.Threading;
using Orleans;
using Orleans.Configuration;
using Orleans.Dashboard;
using Orleans.Hosting;
using Orleans.Providers;
using Orleans.Serialization;
using Simulation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
        orleans.AddDashboard();
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

  public class Strategy(IClusterClient connector, IConfiguration configuration, IHostApplicationLifetime lifetime)
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
    // Mean-reversion: 2-sigma Bollinger fade. Grid-optimal region W=60m, SL=20t, TP=10t.
    const long MrWindowNs = 60L * 60 * 1000000000L;
    const double MrSlTicks = 20, MrTpTicks = 10, MrTickSize = 0.25;
    // Night session only (ET): entries 18:00 -> 09:30, flat through 16:00-18:00 halt.
    static readonly TimeZoneInfo EtZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    readonly Queue<(long t, double m)> mrWindow = new();
    double mrSum, mrSumSq;
    double mrPrevMid = double.NaN, mrPrevLo = double.NaN, mrPrevHi = double.NaN;
    bool mrHaveBand;
    int mrPos;
    double mrEntry;

    const string symName = "ES";

    Dictionary<string, Instrument> Instruments = new()
    {
      [symName] = new() { Name = symName, Leverage = 50, Commission = 3.65, StepSize = 0.25 },
    };

    public void Setup()
    {
      var adapter = Adapter = new SimGateway
      {
        Connector = Connector,
        Source = "D:/Code/NET/Terminal/Data/FUTS",
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
      var adapter = Adapter;
      var bid = price.Bid ?? double.NaN;
      var ask = price.Ask ?? double.NaN;
      if (double.IsNaN(bid) || double.IsNaN(ask)) return;
      if ((ask - bid) / MrTickSize > 3) return; // skip insane quotes (see tick study)
      var mid = (bid + ask) / 2;
      var t = price.Time.Value;
      var etSod = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(t / 1000000L), EtZone).TimeOfDay.TotalSeconds;
      var mrNight = etSod >= 18 * 3600 || etSod < 9.5 * 3600; // entries 18:00 -> 09:30 ET
      var mrHalt = etSod >= 16 * 3600 && etSod < 18 * 3600;   // 16:00-18:00 ET clearing halt

      mrWindow.Enqueue((t, mid)); mrSum += mid; mrSumSq += mid * mid;
      while (mrWindow.Count > 0 && mrWindow.Peek().t < t - MrWindowNs)
      {
        var o = mrWindow.Dequeue(); mrSum -= o.m; mrSumSq -= o.m * o.m;
      }

      if (mrWindow.Count >= 120)
      {
        var n = (double)mrWindow.Count;
        var mean = mrSum / n;
        var variance = mrSumSq / n - mean * mean;
        if (variance < 0) variance = 0;
        var sd = Math.Sqrt(variance);
        var lo = mean - 2 * sd;
        var hi = mean + 2 * sd;

        if (mrPos == 0 && mrHaveBand && mrNight)
        {
          if (mrPrevMid >= mrPrevLo && mid < lo)
          {
            mrPos = 1; mrEntry = ask;
            await OpenPosition(adapter, Instruments[symName], OrderSideEnum.Long);
          }
          else if (mrPrevMid <= mrPrevHi && mid > hi)
          {
            mrPos = -1; mrEntry = bid;
            await OpenPosition(adapter, Instruments[symName], OrderSideEnum.Short);
          }
        }
        else if (mrPos == 1)
        {
          if (mrHalt)
          {
            mrPos = 0;
            await ClosePosition(adapter);
          }
          else if (bid <= mrEntry - MrSlTicks * MrTickSize || bid >= mrEntry + MrTpTicks * MrTickSize)
          {
            mrPos = 0;
            await ClosePosition(adapter);
          }
        }
        else if (mrPos == -1)
        {
          if (mrHalt)
          {
            mrPos = 0;
            await ClosePosition(adapter);
          }
          else if (ask >= mrEntry + MrSlTicks * MrTickSize || ask <= mrEntry - MrTpTicks * MrTickSize)
          {
            mrPos = 0;
            await ClosePosition(adapter);
          }
        }

        mrPrevMid = mid; mrPrevLo = lo; mrPrevHi = hi; mrHaveBand = true;
      }
    }

    /// <summary>
    /// SL price
    /// </summary>
    /// <param name="side"></param>
    /// <param name="price"></param>
    /// <param name="distance"></param>
    protected virtual double? SL(OrderSideEnum side, Price price, double distance)
    {
      switch (side)
      {
        case OrderSideEnum.Long: return price.Bid - distance;
        case OrderSideEnum.Short: return price.Ask + distance;
      }

      return null;
    }

    /// <summary>
    /// TP price
    /// </summary>
    /// <param name="side"></param>
    /// <param name="price"></param>
    /// <param name="distance"></param>
    protected virtual double? TP(OrderSideEnum side, Price price, double distance)
    {
      switch (side)
      {
        case OrderSideEnum.Long: return price.Ask + distance;
        case OrderSideEnum.Short: return price.Bid - distance;
      }

      return null;
    }

    /// <summary>
    /// Open position
    /// </summary>
    /// <param name="adapter"></param>
    /// <param name="asset"></param>
    /// <param name="side"></param>
    /// <param name="amount"></param>
    /// <param name="SL"></param>
    /// <param name="TP"></param>
    protected virtual async Task OpenPosition(
      IGateway adapter,
      Instrument asset,
      OrderSideEnum side,
      double amount = 1,
      double? SL = null,
      double? TP = null)
    {
      var order = new Order
      {
        Side = side,
        Amount = amount,
        Type = OrderTypeEnum.Market,
        Operation = new() { Instrument = asset }
      };

      if (SL is not null)
      {
        order = order with
        {
          Orders = [..order.Orders,
            new()
            {
              Price = SL,
              Amount = amount,
              Type = OrderTypeEnum.Stop,
              Instruction = InstructionEnum.Brace,
              Side = side is OrderSideEnum.Long ? OrderSideEnum.Short : OrderSideEnum.Long,
              Operation = new() { Instrument = asset }
            }
          ]
        };
      }

      if (TP is not null)
      {
        order = order with
        {
          Orders = [..order.Orders,
            new()
            {
              Price = TP,
              Amount = amount,
              Type = OrderTypeEnum.Limit,
              Instruction = InstructionEnum.Brace,
              Side = side is OrderSideEnum.Long ? OrderSideEnum.Short : OrderSideEnum.Long,
              Operation = new() { Instrument = asset }
            }
          ]
        };
      }

      await adapter.SendOrder(order);
    }

    /// <summary>
    /// Close positions
    /// </summary>
    /// <param name="adapter"></param>
    /// <param name="criteria"></param>
    /// <param name="condition"></param>
    protected virtual async Task<List<Order>> ClosePosition(IGateway adapter, PositionCriteria criteria = null, Func<Order, bool> condition = null)
    {
      var response = new List<Order>();
      var positions = await adapter.GetPositions(criteria ?? new() { Source = true });

      foreach (var position in positions.Data)
      {
        if (condition is null || condition(position))
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

          await adapter.SendOrder(order);

          response.Add(order);
        }
      }

      return response;
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
