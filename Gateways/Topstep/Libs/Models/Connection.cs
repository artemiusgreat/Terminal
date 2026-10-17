using Core.Models;
using Orleans;
using System;

namespace Topstep.Models
{
  [GenerateSerializer]
  public record Connection
  {
    /// <summary>
    /// Username
    /// </summary>
    [Id(0)] public string Username { get; init; }

    /// <summary>
    /// Access token
    /// </summary>
    [Id(1)] public string Token { get; init; }

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
