using LiteDB;
using Simulation.Models;
using System.Collections.Generic;

namespace Simulation.Readers
{
  /// <summary>
  /// LiteDB-backed ISummaryReader. Reads Summary BSON documents sequentially.
  /// The original SimStream implementation, preserved as the default.
  /// </summary>
  public class LiteReader : ISummaryReader
  {
    protected LiteDatabase storage;
    protected IEnumerator<Summary> enumerator;

    /// <summary>
    /// Name
    /// </summary>
    public virtual string Name { get; }

    /// <summary>
    /// Current position
    /// </summary>
    public virtual Summary Current => enumerator.Current;

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="source"></param>
    /// <param name="name"></param>
    public LiteReader(string source, string name)
    {
      Name = name;
      storage = new LiteDatabase(source);
      enumerator = storage
        .GetCollection<Summary>("prices")
        .FindAll()
        .GetEnumerator();
    }

    /// <summary>
    /// Iterate
    /// </summary>
    /// <returns></returns>
    public virtual bool MoveNext() => enumerator.MoveNext();

    /// <summary>
    /// Dispose
    /// </summary>
    public virtual void Dispose()
    {
      enumerator.Dispose();
      storage.Dispose();
    }
  }
}
