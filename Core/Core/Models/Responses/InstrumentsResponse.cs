using System.Collections.Generic;

namespace Core.Models
{
  [Immutable]
  [GenerateSerializer]
  public record InstrumentsResponse : Response
  {
    /// <summary>
    /// Data
    /// </summary>
    [Id(1)] public List<Instrument> Data { get; init; } = [];
  }
}
