using Orleans;
using System.Collections.Generic;

namespace Core.Models
{
  [Immutable]
  public record Instruments
  {
    /// <summary>
    /// Instruments
    /// </summary>
    public List<Instrument> Items { get; init; } = [];
  }
}
