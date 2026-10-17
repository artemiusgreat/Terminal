using System.Collections.Generic;

namespace Core.Models
{
  [GenerateSerializer]
  public record Dom
  {
    /// <summary>
    /// Asks
    /// </summary>
    [Id(0)] public SortedDictionary<long, LinkedList<DomOrder>> Asks { get; init; } = new();

    /// <summary>
    /// Bids
    /// </summary>
    [Id(1)] public SortedDictionary<long, LinkedList<DomOrder>> Bids { get; init; } = new(Comparer<long>.Create((x, y) => y.CompareTo(x)));
  }
}
