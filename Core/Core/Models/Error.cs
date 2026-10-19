using Orleans;

namespace Core.Models
{
  [Immutable]
  public record Error
  {
    public string Message { get; init; }
  }
}
