using Core.Enums;
using System;

namespace Core.Models
{
  [GenerateSerializer]
  public record Message
  {
    /// <summary>
    /// Message or error code
    /// </summary>
    [Id(0)] public int? Code { get; init; }

    /// <summary>
    /// Description
    /// </summary>
    [Id(1)] public string Source { get; init; }

    /// <summary>
    /// Description
    /// </summary>
    [Id(2)] public string Description { get; init; }

    /// <summary>
    /// Action
    /// </summary>
    [Id(3)] public ActionEnum Action { get; init; }

    /// <summary>
    /// Exception
    /// </summary>
    [Id(4)] public Exception Error { get; init; }
  }
}
