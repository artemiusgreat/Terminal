using MessagePack;
using MessagePack.Resolvers;
using Simulation.Models;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace Simulation.Readers
{
  /// <summary>
  /// Memory-mapped ISummaryReader for variable-length MessagePack blobs.
  ///
  /// File: {name}.vars — length-prefixed blobs back-to-back, no header:
  ///   [length: int32][blob][length: int32][blob]...
  ///
  /// Each blob deserializes directly into a Summary (no field mapping).
  /// Full-fidelity view: includes Dom/Order/Options where present.
  /// For price-only history use FixedSummaryReader ({name}.ticks) instead.
  /// The two readers are independent — neither calls the other.
  ///
  /// Works like LiteSummaryReader (sequential MoveNext/Current) but backed
  /// by a memory-mapped file instead of LiteDB.
  ///
  /// Use TickFileConverter to build .vars files from LiteDB (one-time).
  /// </summary>
  public class VariableMapReader : ISummaryReader
  {
    static readonly MessagePackSerializerOptions MsgPackOptions = ContractlessStandardResolver.Options;

    public string Name { get; }

    readonly MemoryMappedFile mmf;
    readonly MemoryMappedViewAccessor accessor;
    readonly long fileLength;

    long position = 0; // byte offset of next blob's length prefix
    Summary current;

    public Summary Current => current;

    public VariableMapReader(string source, string name)
    {
      Name = name;

      var varsPath = Path.Combine(source, $"{name}.vars");
      mmf = MemoryMappedFile.CreateFromFile(varsPath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
      accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
      fileLength = new FileInfo(varsPath).Length;
    }

    public bool MoveNext()
    {
      if (position + 4 > fileLength) return false;

      var length = accessor.ReadInt32(position);
      position += 4;

      if (position + length > fileLength) return false;

      var bytes = new byte[length];
      accessor.ReadArray(position, bytes, 0, length);
      position += length;

      current = MessagePackSerializer.Deserialize<Summary>(bytes, MsgPackOptions);
      return true;
    }

    public void Dispose()
    {
      accessor?.Dispose();
      mmf?.Dispose();
    }
  }
}
