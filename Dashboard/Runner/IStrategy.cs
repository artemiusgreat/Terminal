using System.Threading.Tasks;

namespace Runner
{
  /// <summary>
  /// Trading strategy. The pipeline calls <see cref="OnTickAsync"/> for every
  /// tick that passes the spread filter. Switch strategies with one line in
  /// Program.Main: <c>builder.Services.AddSingleton&lt;IStrategy, MyStrategy&gt;();</c>
  /// </summary>
  public interface IStrategy
  {
    Task OnTickAsync(StrategyTick tick);
  }

  /// <summary>A single spread-filtered tick handed to the strategy.</summary>
  public sealed record StrategyTick(string Symbol, double Bid, double Ask, double Mid, long TimeNs);
}
