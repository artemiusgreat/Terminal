namespace Core.Models
{
  [Immutable]
  [GenerateSerializer]
  public record AccountResponse : Response
  {
    /// <summary>
    /// Data
    /// </summary>
    [Id(1)] public Account Data { get; init; }
  }
}
