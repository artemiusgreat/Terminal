using MessagePack;
using MessagePack.Resolvers;
using Simulation.Models;
using System;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace Simulation.Readers
{
  /// <summary>
  /// Random-access reader for variable-length MessagePack blobs.
  ///
  /// File: {name}.var — raw blobs concatenated back-to-back, no header.
  /// Offsets and lengths come from the fixed record's VarOffset/VarLength
  /// fields (see FixedSummaryReader).
  ///
  /// This is not an ISummaryReader — variable data is sparse (only ticks
  /// with Dom/Order/Options have blobs) so sequential iteration makes no
  /// sense. The fixed reader calls ReadSummary() on demand for the rare
  /// ticks that carry variable data.
  /// </summary>
  public class VariableSummaryReader : IDisposable
  {
    static readonly MessagePackSerializerOptions MsgPackOptions = ContractlessStandardResolver.Options;

    readonly MemoryMappedFile mmf;
    readonly MemoryMappedViewAccessor accessor;

    public VariableSummaryReader(string varPath)
    {
      mmf = MemoryMappedFile.CreateFromFile(varPath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
      accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
    }

    /// <summary>
    /// Deserialize the MessagePack blob at the given byte offset/length.
    /// </summary>
    public Summary ReadSummary(int offset, int length)
    {
      var bytes = new byte[length];
      accessor.ReadArray(offset, bytes, 0, length);
      return MessagePackSerializer.Deserialize<Summary>(bytes, MsgPackOptions);
    }

    public void Dispose()
    {
      accessor?.Dispose();
      mmf?.Dispose();
    }
  }
}
