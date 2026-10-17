namespace Core.Models
{
  [Immutable]
  [GenerateSerializer]
  public record PriceResponse : Response
  {
    /// <summary>
    /// Data
    /// </summary>
    [Id(1)] public Price Data { get; init; }
  }
}
