using Orleans;

namespace Core.Models
{
  [Immutable]
  public record DomOrderResponse : Response
  {
    /// <summary>
    /// Data
    /// </summary>
    public DomOrder Data { get; init; }
  }
}
