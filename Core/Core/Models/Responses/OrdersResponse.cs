using System.Collections.Generic;

namespace Core.Models
{
  [Immutable]
  [GenerateSerializer]
  public record OrdersResponse : Response
  {
    /// <summary>
    /// Data
    /// </summary>
    [Id(1)] public List<Order> Data { get; init; } = [];
  }
}
