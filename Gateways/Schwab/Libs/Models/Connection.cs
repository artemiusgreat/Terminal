using Core.Models;
using Orleans;
using System;

namespace Schwab.Models
{
  [GenerateSerializer]
  public record Connection
  {
    /// <summary>
    /// Client ID
    /// </summary>
    [Id(0)] public string Id { get; init; }

    /// <summary>
    /// Client secret
    /// </summary>
    [Id(1)] public string Secret { get; init; }

    /// <summary>
    /// Access token
    /// </summary>
    [Id(2)] public string AccessToken { get; init; }

    /// <summary>
    /// Refresh token
    /// </summary>
    [Id(3)] public string RefreshToken { get; init; }

    /// <summary>
    /// Timeout
    /// </summary>
    [Id(4)] public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Account
    /// </summary>
    [Id(5)] public Account Account { get; init; }
  }
}
