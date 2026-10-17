using System.Collections.Generic;

namespace Core.Models
{
  [GenerateSerializer]
  public record Instruments
  {
    /// <summary>
    /// Instruments
    /// </summary>
    [Id(0)] public List<Instrument> Items { get; init; } = [];
  }
}
