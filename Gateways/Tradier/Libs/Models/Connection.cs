using Core.Models;
using Orleans;
using System;

namespace Tradier.Models
{
  [GenerateSerializer]
  public record Connection
  {
    /// <summary>
    /// Access token
    /// </summary>
    [Id(0)] public string AccessToken { get; init; }

    /// <summary>
    /// Streaming session token
    /// </summary>
    [Id(1)] public string SessionToken { get; init; }

    /// <summary>
    /// Timeout
    /// </summary>
    [Id(2)] public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Account
    /// </summary>
    [Id(3)] public Account Account { get; init; }
  }
}
