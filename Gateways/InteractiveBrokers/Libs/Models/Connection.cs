using Core.Models;
using Orleans;
using System;

namespace InteractiveBrokers.Models
{
  [GenerateSerializer]
  public record Connection
  {
    /// <summary>
    /// Port
    /// </summary>
    [Id(0)] public int Port { get; init; }

    /// <summary>
    /// Host
    /// </summary>
    [Id(1)] public string Host { get; init; }

    /// <summary>
    /// Throttle
    /// </summary>
    [Id(2)] public TimeSpan Span { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// Timeout
    /// </summary>
    [Id(3)] public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Account
    /// </summary>
    [Id(4)] public Account Account { get; init; }
  }
}
