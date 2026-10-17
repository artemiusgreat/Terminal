using Core.Enums;

namespace Core.Models
{
  [GenerateSerializer]
  public record Subscription
  {
    /// <summary>
    /// Next state
    /// </summary>
    [Id(0)] public SubscriptionEnum Next { get; init; }

    /// <summary>
    /// Previous state
    /// </summary>
    [Id(1)] public SubscriptionEnum Previous { get; init; }
  }
}
