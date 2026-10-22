using Core.Enums;
using System;
using System.Threading.Tasks;

namespace Runner.Strategies.CarverFastMr
{
  /// <summary>
  /// Carver fast mean reversion family (strategies 26 and 27), hourly bars.
  /// Mirrors the Python reference (~/workspace/backtest-runs/carver_py.py)
  /// exactly: bar close = last tick mid in the ET clock-hour bucket; daily
  /// closes = close of the last hourly bar of each ET date. Daily state
  /// (EWMA5 equilibrium, blended daily vol, EWMAC16/64 sign) uses daily
  /// closes through the PREVIOUS day: a bar on day index d reads state at
  /// d-1, and the target is 0 while d &lt; 5.
  /// Blended daily vol = 0.7 * sqrt(EWMA32 of squared daily returns)
  /// + 0.3 * expanding RMS of daily returns (undefined for day index &lt; 2).
  /// Target contracts = round(forecast / 10), forecast capped at +/-20.
  /// Strategy 27 adds the EWMAC sign gate and uses scalar 20; it omits the
  /// book's volatility multiplier (needs 10y of vol history - documented
  /// deviation, same as the Python reference).
  /// </summary>
  public abstract class CarverMrBase : IStrategy, IForcedFlatAware
  {
    protected readonly StrategyContext _ctx;

    protected CarverMrBase(StrategyContext ctx) { _ctx = ctx; }

    /// <summary>Forecast scalar (26: 9.3, 27: 20).</summary>
    protected abstract double ForecastScalar { get; }

    /// <summary>27 only: trade only when sign(risk_adj) == sign(EWMAC16 forecast).</summary>
    protected abstract bool UseTrendGate { get; }

    const string Sym = "ES";

    // Hourly bar under construction (close = last tick mid)
    long _lastBucket = -1;
    double _barClose;
    int _barN;

    // ET day currently accumulating (a day completes when a bar from a
    // later ET date closes)
    bool _hasOpenDay;
    DateTime _openDay;
    double _openDayClose;

    // Daily state through the last completed day
    int _dayCount;                    // completed days; latest index = _dayCount - 1
    double _prevDayClose;
    double _ewmaSq;                   // EWMA32 state over squared daily returns
    double _sumSq;                    // sum of squared returns for days 1..latest
    double _dailyStd = double.NaN;    // blended daily vol at latest day index
    double _eq;                       // EWMA5 of daily closes (equilibrium)
    double _ewmacF, _ewmacS;          // EWMA16 / EWMA64 of daily closes

    int _pos; // signed contracts currently held (strategy's view)

    public void OnForcedFlat() { _pos = 0; }

    public async Task OnTickAsync(StrategyTick tick)
    {
      if (tick.Symbol != Sym) return;

      var bucket = tick.TimeNs / _ctx.BarNs;
      if (bucket != _lastBucket)
      {
        // The bar being completed is the last one that can ever complete if
        // the tick completing it sits in the final bucket of the data file.
        var lastCompletable = _ctx.DataEndNs > 0 && bucket >= _ctx.DataEndNs / _ctx.BarNs;
        if (_lastBucket >= 0 && _barN > 0) await OnBarCloseAsync(lastCompletable);
        _lastBucket = bucket; _barClose = tick.Mid; _barN = 1;
      }
      else { _barClose = tick.Mid; _barN++; }
    }

    async Task OnBarCloseAsync(bool lastCompletable)
    {
      // ET date of the bar that just completed (bucket start, ET clock hours)
      var barStartUtc = DateTimeOffset.FromUnixTimeMilliseconds(_lastBucket * _ctx.BarNs / 1000000L);
      var barDate = TimeZoneInfo.ConvertTime(barStartUtc, StrategyContext.EtZone).Date;

      if (!_hasOpenDay) { _hasOpenDay = true; _openDay = barDate; _openDayClose = _barClose; }
      else if (barDate > _openDay)
      {
        FinalizeDay(_openDayClose);
        _openDay = barDate; _openDayClose = _barClose;
      }
      else _openDayClose = _barClose;

      int d = _dayCount; // current bar's day index (days 0..d-1 complete)
      var target = 0;

      if (d >= 5 && !double.IsNaN(_dailyStd) && _dailyStd > 0)
      {
        var riskAdj = (_eq - _barClose) / (_dailyStd * _barClose);
        var ok = true;

        if (UseTrendGate)
        {
          var sign = Math.Sign(_ewmacF - _ewmacS);
          ok = sign != 0 && Math.Sign(riskAdj) == sign;
        }

        if (ok)
        {
          var f = Math.Max(-20.0, Math.Min(20.0, ForecastScalar * riskAdj));
          target = (int)Math.Round(f / 10.0); // Math.Round default = half-to-even, as numpy
        }
      }

      // End of data: flatten here so the close fills on real ticks during
      // the final hour (the host's end-of-stream flatten cannot fill).
      if (lastCompletable) target = 0;

      await RebalanceAsync(target);
    }

    void FinalizeDay(double close)
    {
      var i = _dayCount;
      var sq = 0.0;

      if (i > 0)
      {
        var r = close / _prevDayClose - 1.0;
        sq = r * r;
        _sumSq += sq;
      }

      const double a32 = 2.0 / 33.0;
      _ewmaSq = i == 0 ? sq : a32 * sq + (1 - a32) * _ewmaSq;
      _dailyStd = i >= 2
        ? 0.7 * Math.Sqrt(_ewmaSq) + 0.3 * Math.Sqrt(_sumSq / i)
        : double.NaN;

      const double a5 = 2.0 / 6.0;
      _eq = i == 0 ? close : a5 * close + (1 - a5) * _eq;

      const double a16 = 2.0 / 17.0;
      const double a64 = 2.0 / 65.0;
      _ewmacF = i == 0 ? close : a16 * close + (1 - a16) * _ewmacF;
      _ewmacS = i == 0 ? close : a64 * close + (1 - a64) * _ewmacS;

      _prevDayClose = close;
      _dayCount++;
    }

    async Task RebalanceAsync(int target)
    {
      if (target == _pos) return;
      var delta = target - _pos;
      var ok = delta > 0
        ? await _ctx.OpenPosition(Sym, OrderSideEnum.Long, delta)
        : await _ctx.OpenPosition(Sym, OrderSideEnum.Short, -delta);
      if (ok) _pos = target;
    }
  }
}
