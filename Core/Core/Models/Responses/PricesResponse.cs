using System.Collections.Generic;

namespace Core.Models
{
  [Immutable]
  [GenerateSerializer]
  public record PricesResponse : Response
  {
    /// <summary>
    /// Data
    /// </summary>
    [Id(1)] public List<Price> Data { get; init; } = [];
  }
}
