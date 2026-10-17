namespace Core.Models
{
  [Immutable]
  [GenerateSerializer]
  public record DomResponse : Response
  {
    /// <summary>
    /// Data
    /// </summary>
    [Id(1)] public Dom Data { get; init; }
  }
}
