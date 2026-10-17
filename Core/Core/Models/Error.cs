namespace Core.Models
{
  [GenerateSerializer]
  public record Error
  {
    [Id(0)] public string Message { get; init; }
  }
}
