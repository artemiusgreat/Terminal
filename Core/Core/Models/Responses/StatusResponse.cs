using Core.Enums;
using Orleans;

namespace Core.Models
{
  [Immutable]
  public record StatusResponse : Response
  {
    /// <summary>
    /// Data
    /// </summary>
    public StatusEnum? Data { get; init; }
  }
}
