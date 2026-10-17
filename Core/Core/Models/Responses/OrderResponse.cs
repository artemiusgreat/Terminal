namespace Core.Models
{
  [Immutable]
  [GenerateSerializer]
  public record OrderResponse : Response
  {
    /// <summary>
    /// Data
    /// </summary>
    [Id(1)] public Order Data { get; init; }

    /// <summary>
    /// Transaction
    /// </summary>
    [Id(2)] public Order Transaction { get; init; }
  }
}
