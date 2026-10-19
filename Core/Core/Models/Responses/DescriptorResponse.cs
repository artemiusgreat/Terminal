using Orleans;

namespace Core.Models
{
  [Immutable]
  public record DescriptorResponse : Response
  {
    /// <summary>
    /// Data
    /// </summary>
    public string Data { get; init; }
  }
}
