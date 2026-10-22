using System;
using System.Threading.Tasks;
using Runner.Strategies.CarverBatch2;

namespace Runner.Strategies.CarverSpread
{
  /// <summary>
  /// Carver strategy 28: Cross instrument spread (ES/NQ), EWMAC16 trend on
  /// the synthetic spread price. R* = 8.5 FIXED (computed in the Python
  /// reference from daily closes: sigma ratio of daily diffs rounded to
  /// the nearest 0.5; hardcoded here, not recomputed). Spread price
  /// pD = 8.5 * ES_close - NQ_close on synchronised hourly bars; forecast
  /// f = clip(((EWMA16 - EWMA64) of pD) / vol_sp * 4.10, +/-20) with
  /// vol_sp = EWMA35 std of hourly pD changes, via the shared engine.
  /// Units = round(f / 10) in -2..2, NO buffer. One unit (pack) = long
  /// 17 ES + short 5 NQ, scaled linearly; negative units invert both legs.
  /// Evaluated on ES bar closes, using the latest NQ close at or before
  /// that bar (the framework delivers a single time-ordered tick stream,
  /// so when an ES bar completes, the NQ close for that bucket is final).
  /// Both legs flattened at the last completable ES bar.
  /// </summary>
  public sealed class CarverSpreadStrategy : IStrategy, IForcedFlatAware
  {
    const double R = 8.5;

    readonly StrategyContext _ctx;
    readonly SymbolFeed _es;
    readonly SymbolFeed _nq;
    readonly EwmacEngine _eng = new();
    int _posEs, _posNq;

    public CarverSpreadStrategy(StrategyContext ctx)
    {
      _ctx = ctx;
      _es = new SymbolFeed(ctx, "ES", 50.0);
      _nq = new SymbolFeed(ctx, "NQ", 20.0);
    }

    public void OnForcedFlat() { _posEs = 0; _posNq = 0; }

    public async Task OnTickAsync(StrategyTick tick)
    {
      if (tick.Symbol == "NQ")
      {
        _nq.Advance(tick, out _);
        return;
      }
      if (tick.Symbol != "ES") return;
      if (!_es.Advance(tick, out var lastCompletable)) return;
      if (!_nq.HasData) return;

      var pD = R * _es.LastCompletedClose - _nq.CloseAtOrBefore(_es.LastCompletedBucket);
      var F = _eng.OnPrice(pD);
      var f = F[EwmacEngine.IndexOf(16)];
      var units = double.IsNaN(f) ? 0 : (int)Math.Round(f / 10.0);
      if (lastCompletable) units = 0;

      _posEs = await CarverCommon.RebalanceAsync(_ctx, "ES", _posEs, 17 * units);
      _posNq = await CarverCommon.RebalanceAsync(_ctx, "NQ", _posNq, -5 * units);
    }
  }
}
