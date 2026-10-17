namespace Core.Models
{
  [Immutable]
  [GenerateSerializer]
  public record PositionCriteria : Criteria
  {
    /// <summary>
    /// Instrument name
    /// </summary>
    [Id(8)] public string Name { get; init; }
  }
}
