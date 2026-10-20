using Core.Enums;
using Orleans;

namespace Core.Models
{
  [Immutable]
  public record Subscription
  {
    /// <summary>
    /// Next state
    /// </summary>
    public SubscriptionEnum Next { get; init; }

    /// <summary>
    /// Previous state
    /// </summary>
    public SubscriptionEnum Previous { get; init; }
  }
}
