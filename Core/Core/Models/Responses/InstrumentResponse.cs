namespace Core.Models
{
  [Immutable]
  [GenerateSerializer]
  public record InstrumentResponse : Response
  {
    /// <summary>
    /// Data
    /// </summary>
    [Id(1)] public Instrument Data { get; init; }
  }
}
