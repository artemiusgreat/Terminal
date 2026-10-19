using Orleans;

namespace Core.Models
{
  [Immutable]
  public record DomResponse : Response
  {
    /// <summary>
    /// Data
    /// </summary>
    public Dom Data { get; init; }
  }
}
