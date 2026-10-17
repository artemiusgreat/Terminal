namespace Core.Models
{
  [Immutable]
  [GenerateSerializer]
  public record DescriptorResponse : Response
  {
    /// <summary>
    /// Data
    /// </summary>
    [Id(1)] public string Data { get; init; }
  }
}
