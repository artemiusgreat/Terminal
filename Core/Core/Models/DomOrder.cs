using Core.Enums;

namespace Core.Models
{
  [Immutable]
  [GenerateSerializer]
  public record DomOrder
  {
    /// <summary>
    /// Id
    /// </summary>
    [Id(0)] public string Id { get; init; }

    /// <summary>
    /// Name
    /// </summary>
    [Id(1)] public string Name { get; init; }

    /// <summary>
    /// Source of order
    /// </summary>
    [Id(2)] public string Source { get; init; }

    /// <summary>
    /// Flags
    /// </summary>
    [Id(3)] public int? Mask { get; init; }

    /// <summary>
    /// Sequence number
    /// </summary>
    [Id(4)] public int? Index { get; init; }

    /// <summary>
    /// Size
    /// </summary>
    [Id(5)] public double? Size { get; init; }

    /// <summary>
    /// Price
    /// </summary>
    [Id(6)] public double? Price { get; init; }

    /// <summary>
    /// Time stamp
    /// </summary>
    [Id(7)] public long? Time { get; init; }

    /// <summary>
    /// Side
    /// </summary>
    [Id(8)] public DomSide? Side { get; init; }

    /// <summary>
    /// Action
    /// </summary>
    [Id(9)] public DomAction? Action { get; init; }
  }
}
