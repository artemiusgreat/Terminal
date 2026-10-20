using Core.Enums;
using Orleans;

namespace Core.Models
{
  [Immutable]
  public record OrderCriteria : Criteria
  {
    /// <summary>
    /// Order ID
    /// </summary>
    public string Id { get; init; }

    /// <summary>
    /// Status
    /// </summary>
    public OrderStatusEnum Status { get; init; }
  }
}
