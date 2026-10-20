using Orleans;

namespace Core.Models
{
  [Immutable]
  public record PriceResponse : Response
  {
    /// <summary>
    /// Data
    /// </summary>
    public Price Data { get; init; }
  }
}
