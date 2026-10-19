using Orleans;
using System.Collections.Generic;

namespace Core.Models
{
  [Immutable]
  public record PricesResponse : Response
  {
    /// <summary>
    /// Data
    /// </summary>
    public List<Price> Data { get; init; } = [];
  }
}
