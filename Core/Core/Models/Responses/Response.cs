using Orleans;
using System.Collections.Generic;

namespace Core.Models
{
  [Immutable]
  public record Response
  {
    /// <summary>
    /// Errors
    /// </summary>
    public List<string> Errors { get; init; } = [];
  }
}
