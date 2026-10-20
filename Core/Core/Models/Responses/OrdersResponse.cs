using Orleans;
using System.Collections.Generic;

namespace Core.Models
{
  [Immutable]
  public record OrdersResponse : Response
  {
    /// <summary>
    /// Data
    /// </summary>
    public List<Order> Data { get; init; } = [];
  }
}
