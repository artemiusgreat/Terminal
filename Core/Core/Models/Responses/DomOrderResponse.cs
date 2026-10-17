namespace Core.Models
{
  [Immutable]
  [GenerateSerializer]
  public record DomOrderResponse : Response
  {
    /// <summary>
    /// Data
    /// </summary>
    [Id(1)] public DomOrder Data { get; init; }
  }
}
