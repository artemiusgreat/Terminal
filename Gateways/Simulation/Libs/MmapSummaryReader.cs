using MemoryPack;
using Simulation.Models;
using System;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace Simulation
{
  /// <summary>
  /// Memory-mapped ISummaryReader. The file contains length-prefixed
  /// MemoryPack-serialized Summary records:
  ///
  ///   [header: magic(4) | version(4) | count(8)]
  ///   [record: length(4) | MemoryPack(Summary)] *
  ///
  /// The reader only deserializes — it does NOT reconstruct order books.
  /// Dom/Order processing remains the responsibility of DomGrain, exactly
  /// as with the LiteDB reader. This is a storage swap, not a logic change.
  ///
  /// Use TickFileConverter to build .ticks files from LiteDB sources (one-time).
  /// </summary>
  public class MmapSummaryReader : ISummaryReader
  {
    public const int Magic = 0x5449434B; // "TICK"
    public const int CurrentVersion = 1;
    const int HeaderSize = 16;

    public string Name { get; }

    readonly MemoryMappedFile mmf;
    readonly MemoryMappedViewAccessor accessor;
    readonly long count;
    readonly long dataStart;

    long position = -1;
    long offset;
    Summary current;
    bool disposed;

    public Summary Current => current;

    public MmapSummaryReader(string source, string instrumentName)
    {
      Name = instrumentName;
      var path = Path.Combine(source, $"{instrumentName}.ticks");
      if (!File.Exists(path))
        throw new FileNotFoundException($"Tick file not found: {path}. Run TickFileConverter first.", path);

      mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
      accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

      var magic = accessor.ReadInt32(0);
      var version = accessor.ReadInt32(4);
      if (magic != Magic)
        throw new InvalidDataException($"Bad magic in {path}: 0x{magic:X8}");
      if (version != CurrentVersion)
        throw new InvalidDataException($"Unsupported version {version} in {path}");

      count = accessor.ReadInt64(8);
      dataStart = HeaderSize;
      offset = dataStart;
    }

    public bool MoveNext()
    {
      if (position + 1 >= count) return false;
      position++;

      var length = accessor.ReadInt32(offset);
      offset += 4;

      // Deserialize directly from the mapped view — no intermediate copy
      // for the common case (MemoryPack reads from Span<byte>).
      var buffer = new byte[length];
      accessor.ReadArray(offset, buffer, 0, length);
      offset += length;

      current = MemoryPackSerializer.Deserialize<Summary>(buffer);
      return true;
    }

    public void Dispose()
    {
      if (disposed) return;
      disposed = true;
      accessor?.Dispose();
      mmf?.Dispose();
    }
  }
}
