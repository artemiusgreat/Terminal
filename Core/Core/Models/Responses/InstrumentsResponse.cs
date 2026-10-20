using Orleans;
using System.Collections.Generic;

namespace Core.Models
{
  [Immutable]
  public record InstrumentsResponse : Response
  {
    /// <summary>
    /// Data
    /// </summary>
    public List<Instrument> Data { get; init; } = [];
  }
}
