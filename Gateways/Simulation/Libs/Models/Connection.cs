using Core.Models;
using Orleans;

namespace Simulation.Models
{
  [GenerateSerializer]
  public record Connection
  {
    /// <summary>
    /// Data source
    /// </summary>
    [Id(0)] public string Source { get; init; }

    /// <summary>
    /// Account
    /// </summary>
    [Id(1)] public Account Account { get; init; }
  }
}
