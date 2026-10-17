using Core.Enums;

namespace Core.Models
{
  [Immutable]
  [GenerateSerializer]
  public record OrderCriteria : Criteria
  {
    /// <summary>
    /// Order ID
    /// </summary>
    [Id(8)] public string Id { get; init; }

    /// <summary>
    /// Status
    /// </summary>
    [Id(9)] public OrderStatusEnum Status { get; init; }
  }
}
