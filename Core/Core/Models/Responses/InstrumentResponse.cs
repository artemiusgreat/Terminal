using Orleans;

namespace Core.Models
{
  [Immutable]
  public record InstrumentResponse : Response
  {
    /// <summary>
    /// Data
    /// </summary>
    public Instrument Data { get; init; }
  }
}
