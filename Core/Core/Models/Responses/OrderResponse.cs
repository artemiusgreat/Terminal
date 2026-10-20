using Orleans;

namespace Core.Models
{
  [Immutable]
  public record OrderResponse : Response
  {
    /// <summary>
    /// Data
    /// </summary>
    public Order Data { get; init; }

    /// <summary>
    /// Transaction
    /// </summary>
    public Order Transaction { get; init; }
  }
}
