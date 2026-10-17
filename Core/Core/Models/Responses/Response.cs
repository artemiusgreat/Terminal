using System.Collections.Generic;

namespace Core.Models
{
  [Immutable]
  [GenerateSerializer]
  public record Response
  {
    /// <summary>
    /// Errors
    /// </summary>
    [Id(0)] public List<string> Errors { get; init; } = [];
  }
}
