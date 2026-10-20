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

  /// <summary>
  /// Optional hook: the pipeline force-flattened all positions (Topstep
  /// halt window, daily stop, or run end). Implement to reset internal
  /// position state so it stays in sync with the account.
  /// </summary>
  public interface IForcedFlatAware
  {
    void OnForcedFlat();
  }

  /// <summary>A single spread-filtered tick handed to the strategy.</summary>
  public sealed record StrategyTick(string Symbol, double Bid, double Ask, double Mid, long TimeNs);
}
