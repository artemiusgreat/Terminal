using System.Collections.Generic;

namespace Core.Models
{
  [GenerateSerializer]
  public record Account
  {
    /// <summary>
    /// Name
    /// </summary>
    [Id(0)] public string Descriptor { get; init; }

    /// <summary>
    /// Balance
    /// </summary>
    [Id(1)] public double? Balance { get; init; } = 0;

    /// <summary>
    /// State of the account in the beginning
    /// </summary>
    [Id(2)] public double? Performance { get; init; } = 0;

    /// <summary>
    /// Instruments
    /// </summary>
    [Id(3)] public Dictionary<string, Instrument> Instruments { get; init; } = new();
  }
}
