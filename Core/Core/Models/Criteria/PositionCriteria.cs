using Orleans;

namespace Core.Models
{
  [Immutable]
  public record PositionCriteria : Criteria
  {
    /// <summary>
    /// Instrument name
    /// </summary>
    public string Name { get; init; }
  }
}
