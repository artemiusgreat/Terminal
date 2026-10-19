using System.Collections.Generic;

namespace Core.Models
{
  public partial record Dom
  {
    /// <summary>
    /// Asks
    /// </summary>
    public SortedDictionary<long, LinkedList<DomOrder>> Asks { get; init; } = new();

    /// <summary>
    /// Bids
    /// </summary>
    public SortedDictionary<long, LinkedList<DomOrder>> Bids { get; init; } = new(Comparer<long>.Create((x, y) => y.CompareTo(x)));
  }
}
