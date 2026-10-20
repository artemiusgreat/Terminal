using Core.Enums;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Models
{
  /// <summary>
  /// Order book aggregator (extracted from DomGrain for reuse).
  /// Maintains MBO book state and builds aggregated Instrument quotes.
  ///
  /// Usage:
  ///   var book = new DomBook(stepSize: 0.25);
  ///   foreach (var order in mboStream)
  ///   {
  ///     if (order.Action == DomAction.Trade)
  ///       quote = book.Trade(order);  // aggregated Bid/Ask + Last/Volume
  ///     else
  ///       book.SendOrder(order);      // update book state
  ///   }
  /// </summary>
  public class DomBook
  {
    /// <summary>
    /// Book state
    /// </summary>
    public Dom State { get; } = new();

    /// <summary>
    /// Number of live orders currently in the book
    /// </summary>
    public int OrderCount => orderIndex.Count;

    /// <summary>
    /// Order index for O(1) lookup by ID
    /// </summary>
    protected Dictionary<string, LinkedListNode<DomOrder>> orderIndex = new();

    /// <summary>
    /// Tick size for price-to-key conversion
    /// </summary>
    protected readonly double stepSize;

    public DomBook(double stepSize = 0.01)
    {
      this.stepSize = stepSize;
    }

    /// <summary>
    /// Update DOM with order (dispatch by action)
    /// </summary>
    public virtual void SendOrder(DomOrder order)
    {
      switch (order.Action)
      {
        case DomAction.Store:
        case DomAction.Update: StoreOrder(order); break;
        case DomAction.Remove: RemoveOrder(order); break;
        case DomAction.Clear: Clear(); break;
      }
    }

    /// <summary>
    /// Store order in the book
    /// </summary>
    public virtual void StoreOrder(DomOrder order)
    {
      if (order?.Id is null)
      {
        return;
      }

      orderIndex.TryGetValue(order.Id, out var node);

      var domOrder = node?.Value;
      var orderSide = order.Side ?? domOrder?.Side;
      var orderSize = order.Size ?? domOrder?.Size;
      var orderPrice = order.Price ?? domOrder?.Price;

      if (orderSize <= 0)
      {
        RemoveOrder(order);
        return;
      }

      var side = Side(orderSide);
      var price = Price(orderPrice);

      if (price is null || side is null)
      {
        return;
      }

      var mergeOrder = (domOrder ?? order) with
      {
        Size = orderSize,
        Side = orderSide,
        Price = orderPrice,
        Action = order.Action,
        Name = order.Name ?? domOrder?.Name,
        Mask = order.Mask ?? domOrder?.Mask,
        Index = order.Index ?? domOrder?.Index,
        Source = order.Source ?? domOrder?.Source
      };

      if (domOrder is not null && domOrder.Side == orderSide && domOrder.Price == orderPrice && domOrder.Size >= orderSize)
      {
        node.Value = mergeOrder;
        return;
      }

      if (domOrder is not null)
      {
        RemoveOrder(domOrder);
      }

      var chain = side.GetValueOrDefault(price.Value);

      if (chain is null)
      {
        side[price.Value] = chain = new LinkedList<DomOrder>();
      }

      orderIndex[order.Id] = chain.AddLast(mergeOrder);
    }

    /// <summary>
    /// Remove order from the book
    /// </summary>
    public virtual void RemoveOrder(DomOrder order)
    {
      if (order?.Id is null || orderIndex.TryGetValue(order.Id, out var node) is false)
      {
        return;
      }

      var domOrder = node.Value;
      var size = domOrder.Size - (order.Size ?? domOrder.Size);

      // 1. Reduce
      if (order.Size is not null && size > 0)
      {
        node.Value = domOrder with { Size = size };
        return;
      }

      // 2. Remove
      if (node.List is not null)
      {
        var chain = node.List;
        var side = Side(domOrder.Side);
        var price = Price(domOrder.Price);

        chain.Remove(node);

        if (chain.Count is 0 && price.HasValue && side is not null)
        {
          side.Remove(price.Value);
        }
      }

      orderIndex.Remove(domOrder.Id);
    }

    /// <summary>
    /// Build aggregated Instrument quote from book state + trade order.
    /// Bid/Ask = best prices, BidSize/AskSize = aggregated sizes at best.
    /// Last/Volume = trade price/size from the order.
    /// </summary>
    public virtual Instrument Trade(DomOrder order)
    {
      var bids = State.Bids.FirstOrDefault().Value;
      var asks = State.Asks.FirstOrDefault().Value;

      return new Instrument
      {
        Name = order.Name,
        Price = new Price
        {
          Time = order.Time,
          Last = order.Price,
          Volume = order.Size,
          Bid = bids?.First?.Value.Price,
          Ask = asks?.First?.Value.Price,
          BidSize = bids?.Sum(o => o.Size) ?? 0,
          AskSize = asks?.Sum(o => o.Size) ?? 0
        }
      };
    }

    /// <summary>
    /// Clear book state
    /// </summary>
    public virtual void Clear()
    {
      State.Bids.Clear();
      State.Asks.Clear();
      orderIndex.Clear();
    }

    /// <summary>
    /// Get side dictionary
    /// </summary>
    protected virtual SortedDictionary<long, LinkedList<DomOrder>> Side(DomSide? side)
    {
      switch (side)
      {
        case DomSide.Bid: return State.Bids;
        case DomSide.Ask: return State.Asks;
      }

      return null;
    }

    /// <summary>
    /// Convert price to dictionary key (price / stepSize, rounded)
    /// </summary>
    protected virtual long? Price(double? price)
    {
      if (price is not null)
      {
        return (long)Math.Round(price.Value / stepSize);
      }

      return null;
    }
  }
}
