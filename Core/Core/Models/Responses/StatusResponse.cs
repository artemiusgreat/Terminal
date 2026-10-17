using Core.Enums;

namespace Core.Models
{
  [Immutable]
  [GenerateSerializer]
  public record StatusResponse : Response
  {
    /// <summary>
    /// Data
    /// </summary>
    [Id(1)] public StatusEnum? Data { get; init; }
  }
}
