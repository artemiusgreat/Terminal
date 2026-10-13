using Core.Conventions;
using Core.Enums;
using Core.Extensions;
using Core.Grains;
using Core.Models;
using Orleans;
using Simulation.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Simulation.Grains
{
  public interface ISimConnectionGrain : IConnectionGrain
  {
    /// <summary>
    /// Connect
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="grainObserver"></param>
    Task<StatusResponse> Setup(Connection connection, ITradeObserver grainObserver);
  }

  /// <summary>
  /// Constructor
  /// </summary>
  public class SimConnectionGrain : ConnectionGrain, ISimConnectionGrain
  {
    protected Connection state;
    protected CancellationTokenSource cts;
    protected string descriptor;
    protected IDomGrain domGrain;
    protected IOptionsGrain optionsGrain;
    protected ISimOrdersGrain ordersGrain;
    protected ISimPositionsGrain positionsGrain;
    protected IInstrumentGrain instrumentGrain;
    protected PriorityQueue<SimStream, long> queue = new();
    protected ConcurrentDictionary<string, SimStream> docs = new();
    protected ConcurrentDictionary<string, SimStream> streams = new();

    /// <summary>
    /// Setup
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="grainObserver"></param>
    public virtual async Task<StatusResponse> Setup(Connection connection, ITradeObserver grainObserver)
    {
      descriptor = this.GetDescriptor();

      state = connection;
      observer = grainObserver;
      cts = new CancellationTokenSource();
      ordersGrain = GrainFactory.GetGrain<ISimOrdersGrain>(descriptor);
      positionsGrain = GrainFactory.GetGrain<ISimPositionsGrain>(descriptor);

      await GrainFactory.GetGrain<ISimTransactionsGrain>(descriptor).Setup(connection, observer);

      foreach (var instrument in state.Account.Instruments.Values)
      {
        if (docs.ContainsKey(instrument.Name) is false)
        {
          var name = instrument.Name;
          var instrumentDescriptor = this.GetDescriptor(name);
          var source = Path.Combine(state.Source, $"{name}.db");
          var stream = docs[name] = new SimStream(source, name);

          domGrain = GrainFactory.GetGrain<IDomGrain>(instrumentDescriptor);
          optionsGrain = GrainFactory.GetGrain<IOptionsGrain>(instrumentDescriptor);
          instrumentGrain = GrainFactory.GetGrain<IInstrumentGrain>(instrumentDescriptor);

          await Subscribe(instrument);
          await domGrain.StoreInstrument(instrument);

          if (stream.MoveNext())
          {
            queue.Enqueue(streams[instrument.Name] = stream, stream.Current.Time);
          }
        }
      }

      connections.Add(this.RegisterGrainTimer(o => Process(), 0, TimeSpan.Zero, TimeSpan.FromMicroseconds(1)));

      return new StatusResponse { Data = StatusEnum.Active };
    }

    /// <summary>
    /// Disconnect
    /// </summary>
    public override Task<StatusResponse> Disconnect()
    {
      cts?.Cancel();
      cts?.Dispose();
      streams?.Clear();
      docs?.Values?.ForEach(o => o.Dispose());
      docs?.Clear();

      return Task.FromResult(new StatusResponse { Data = StatusEnum.Inactive });
    }

    /// <summary>
    /// Subscribe
    /// </summary>
    /// <param name="instrument"></param>
    public override Task<StatusResponse> Subscribe(Instrument instrument)
    {
      streams[instrument.Name] = docs[instrument.Name];
      return Task.FromResult(new StatusResponse { Data = StatusEnum.Active });
    }

    /// <summary>
    /// Unsubscribe
    /// </summary>
    /// <param name="instrument"></param>
    public override Task<StatusResponse> Unsubscribe(Instrument instrument)
    {
      streams.TryRemove(instrument.Name, out var _);
      return Task.FromResult(new StatusResponse { Data = StatusEnum.Pause });
    }

    /// <summary>
    /// Sequential reader loop
    /// </summary>
    protected async Task Process()
    {
      try
      {
        var queueResponse = queue.TryPeek(out var s, out var _);
        var streamResponse = streams.ContainsKey(s?.Name ?? string.Empty);

        if (queueResponse is false || streamResponse is false)
        {
          return;
        }

        var stream = queue.Dequeue();
        var instrument = state.Account.Instruments[stream.Name];
        var summary = stream.Current;
        var summaryOrder = summary.Order;
        var summaryInstrument = summary.Instrument;

        if (summaryOrder is not null)
        {
          var orderResponse = await domGrain.SendOrder(summaryOrder);
          summaryOrder = orderResponse.Data;

          if (summaryOrder.Action is DomAction.Trade)
          {
            var instrumentResponse = await domGrain.Trade(summaryOrder);
            summaryInstrument = instrumentResponse.Data;
          }
        }

        if (summary.Dom is not null)
        {
          await domGrain.Store(summary.Dom);
        }

        if (summary.Options is not null && summary.Options.Count is not 0)
        {
          await optionsGrain.Store(summary.Options);

          var map = new HashSet<string>();
          var orders = await ordersGrain.Orders(default);
          var positions = await positionsGrain.Positions(default);

          foreach (var order in orders.Data) map.Add(order.Operation.Instrument.Name);
          foreach (var position in positions.Data) map.Add(position.Operation.Instrument.Name);
          foreach (var option in summary.Options)
          {
            if (map.Contains(option.Name))
            {
              await ordersGrain.Tap(option);
              await positionsGrain.Tap(option);
            }
          }
        }

        if (summaryInstrument is not null)
        {
          var groupResponse = await instrumentGrain.Send(summaryInstrument with { Name = instrument.Name });
          var group = groupResponse.Data;

          await ordersGrain.Tap(group);
          await positionsGrain.Tap(group);
          await observer.StreamInstrument(group);
        }

        await observer.StreamDomOrder(summaryOrder);

        if (stream.MoveNext())
        {
          queue.Enqueue(stream, stream.Current.Time);
        }
      }
      catch (Exception e)
      {
        Console.WriteLine(e);
      }
    }
  }
}
