using Orleans;

namespace Core.Models
{
  [Immutable]
  public record AccountResponse : Response
  {
    /// <summary>
    /// Data
    /// </summary>
    public Account Data { get; init; }
  }
}
