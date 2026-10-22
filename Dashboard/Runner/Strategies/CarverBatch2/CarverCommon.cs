using Core.Enums;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Runner.Strategies.CarverBatch2
{
  /// <summary>
  /// Per-symbol feed: hourly bars from ticks (close = last tick mid in the
  /// ET clock-hour bucket, identical bucketing to batch 1) plus the daily
  /// machinery of batch 1 (daily closes = close of the last hourly bar of
  /// each ET date; daily state uses closes through the PREVIOUS day).
  /// Blended daily vol = 0.7 * sqrt(EWMA32 of squared daily returns)
  /// + 0.3 * expanding RMS of daily returns (NaN for day index &lt; 2),
  /// exactly as in ~/workspace/backtest-runs/carver_py2.py.
  /// </summary>
  public sealed class SymbolFeed
  {
    readonly StrategyContext _ctx;
    public readonly string Symbol;
    public readonly double Mult;

    long _lastBucket = -1;
    double _barClose;
    int _barN;

    /// <summary>Index of the last completed bar (-1 before the first).</summary>
    public int BarIndex { get; private set; } = -1;
    /// <summary>Bucket (TimeNs / BarNs) of the last completed bar.</summary>
    public long LastCompletedBucket { get; private set; } = -1;
    /// <summary>Close of the last completed bar.</summary>
    public double LastCompletedClose { get; private set; }
    /// <summary>Bucket currently accumulating.</summary>
    public long CurrentBucket => _lastBucket;
    /// <summary>Last mid in the bucket currently accumulating.</summary>
    public double CurrentBarClose => _barClose;
    /// <summary>True once at least one tick has been seen.</summary>
    public bool HasData { get; private set; }

    readonly Dictionary<long, double> _byBucket = new();

    // Daily state through the last completed day
    bool _hasOpenDay;
    DateTime _openDay;
    double _openDayClose;
    /// <summary>Number of completed days; latest day index = DayCount - 1.</summary>
    public int DayCount { get; private set; }
    double _prevDayClose;
    double _ewmaSq;   // EWMA32 state over squared daily returns
    double _sumSq;     // sum of squared returns for days 1..latest
    /// <summary>Blended daily vol (fraction) at the latest completed day; NaN while that day index &lt; 2.</summary>
    public double DailyStd { get; private set; } = double.NaN;

    public SymbolFeed(StrategyContext ctx, string symbol, double mult)
    {
      _ctx = ctx; Symbol = symbol; Mult = mult;
    }

    /// <summary>Feed one tick. Returns true when the tick completed the previous bar.</summary>
    public bool Advance(StrategyTick tick, out bool lastCompletable)
    {
      HasData = true;
      lastCompletable = false;
      var bucket = tick.TimeNs / _ctx.BarNs;
      if (bucket != _lastBucket)
      {
        // The bar being completed is the last one that can ever complete if
        // the tick completing it sits in the final bucket of the data file.
        lastCompletable = _ctx.DataEndNs > 0 && bucket >= _ctx.DataEndNs / _ctx.BarNs;
        var completed = _lastBucket >= 0 && _barN > 0;
        if (completed) CompleteBar();
        _lastBucket = bucket; _barClose = tick.Mid; _barN = 1;
        return completed;
      }
      _barClose = tick.Mid; _barN++;
      return false;
    }

    void CompleteBar()
    {
      BarIndex++;
      LastCompletedBucket = _lastBucket;
      LastCompletedClose = _barClose;
      _byBucket[_lastBucket] = _barClose;

      // ET date of the completed bar (bucket start, ET clock hours)
      var barStartUtc = DateTimeOffset.FromUnixTimeMilliseconds(_lastBucket * _ctx.BarNs / 1000000L);
      var barDate = TimeZoneInfo.ConvertTime(barStartUtc, StrategyContext.EtZone).Date;

      if (!_hasOpenDay) { _hasOpenDay = true; _openDay = barDate; _openDayClose = _barClose; }
      else if (barDate > _openDay)
      {
        FinalizeDay(_openDayClose);
        _openDay = barDate; _openDayClose = _barClose;
      }
      else _openDayClose = _barClose;
    }

    void FinalizeDay(double close)
    {
      var i = DayCount;
      var sq = 0.0;

      if (i > 0)
      {
        var r = close / _prevDayClose - 1.0;
        sq = r * r;
        _sumSq += sq;
      }

      const double a32 = 2.0 / 33.0;
      _ewmaSq = i == 0 ? sq : a32 * sq + (1 - a32) * _ewmaSq;
      DailyStd = i >= 2
        ? 0.7 * Math.Sqrt(_ewmaSq) + 0.3 * Math.Sqrt(_sumSq / i)
        : double.NaN;

      _prevDayClose = close;
      DayCount++;
    }

    /// <summary>
    /// Close of the given bucket if it is complete (or still accumulating but
    /// already final because global time has passed it); otherwise the latest
    /// close at or before that bucket.
    /// </summary>
    public double CloseAtOrBefore(long bucket)
    {
      if (_lastBucket == bucket) return _barClose;
      if (_byBucket.TryGetValue(bucket, out var c)) return c;
      var best = _barClose;
      var bestB = long.MinValue;
      foreach (var kv in _byBucket)
        if (kv.Key < bucket && kv.Key > bestB) { bestB = kv.Key; best = kv.Value; }
      return best;
    }
  }

  /// <summary>
  /// The six EWMAC rules of Carver strategy 9 on a streaming price series.
  /// Rule k: (EWMA(fast) - EWMA(slow)) / vol * scalar, capped at +/-20, where
  /// vol = sqrt(EWMA35 of squared price changes) in price points (the first
  /// change is 0 and the EWMA is seeded with it, so forecasts are NaN on the
  /// first bar, exactly as the Python reference). EWMAs are seeded with the
  /// first price; alpha = 2 / (span + 1) throughout.
  /// </summary>
  public sealed class EwmacEngine
  {
    public static readonly int[] Keys = { 2, 4, 8, 16, 32, 64 };
    static readonly int[] FastSpans = { 2, 4, 8, 16, 32, 64 };
    static readonly int[] SlowSpans = { 8, 16, 32, 64, 128, 256 };
    static readonly double[] Scalars = { 12.1, 8.53, 5.95, 4.10, 2.79, 1.91 };

    public static int IndexOf(int key) => Array.IndexOf(Keys, key);

    readonly double[] _f = new double[6];
    readonly double[] _s = new double[6];
    double _volSq;
    double _prev;
    bool _warm;

    /// <summary>Per-rule forecast history, indexed by bar (NaN at bar 0).</summary>
    public readonly List<double>[] History = new List<double>[6];

    public EwmacEngine()
    {
      for (var k = 0; k < 6; k++) History[k] = new List<double>();
    }

    /// <summary>Feed one price; returns the six capped forecasts (all NaN on the first call).</summary>
    public double[] OnPrice(double p)
    {
      var change = _warm ? p - _prev : 0.0;
      const double a35 = 2.0 / 36.0;
      _volSq = _warm ? a35 * change * change + (1 - a35) * _volSq : 0.0;

      for (var k = 0; k < 6; k++)
      {
        var af = 2.0 / (FastSpans[k] + 1.0);
        var asn = 2.0 / (SlowSpans[k] + 1.0);
        _f[k] = _warm ? af * p + (1 - af) * _f[k] : p;
        _s[k] = _warm ? asn * p + (1 - asn) * _s[k] : p;
      }
      _prev = p;

      var F = new double[6];
      if (!_warm)
      {
        for (var k = 0; k < 6; k++) F[k] = double.NaN;
      }
      else
      {
        var vol = Math.Sqrt(_volSq);
        for (var k = 0; k < 6; k++)
          F[k] = vol > 0
            ? Math.Max(-20.0, Math.Min(20.0, (_f[k] - _s[k]) / vol * Scalars[k]))
            : double.NaN;
      }
      _warm = true;
      for (var k = 0; k < 6; k++) History[k].Add(F[k]);
      return F;
    }
  }

  /// <summary>
  /// Carver's buffered position targets (strategies 8/9/12/17/23):
  /// optimal = A * forecast / 10, buffer B = 0.10 * A around the current
  /// target; trade only to the nearest buffer boundary when outside it.
  /// NaN forecast or anchor holds the current target. Mirrors
  /// buffered_targets() in carver_py2.py, including half-to-even rounding.
  /// </summary>
  public sealed class ForecastBuffer
  {
    int _c;
    public int Current => _c;

    public int Next(double fc, double A)
    {
      if (double.IsNaN(fc) || double.IsNaN(A)) return _c;
      var opt = A * fc / 10.0;
      var B = 0.10 * A;
      if (_c < opt - B) _c = (int)Math.Round(opt - B);
      else if (_c > opt + B) _c = (int)Math.Round(opt + B);
      return _c;
    }

    public void Reset() => _c = 0;
  }

  public static class CarverCommon
  {
    /// <summary>Move the position in one symbol to the signed target; returns the new position.</summary>
    public static async Task<int> RebalanceAsync(StrategyContext ctx, string sym, int current, int target)
    {
      if (target == current) return current;
      var delta = target - current;
      var ok = delta > 0
        ? await ctx.OpenPosition(sym, OrderSideEnum.Long, delta)
        : await ctx.OpenPosition(sym, OrderSideEnum.Short, -delta);
      return ok ? target : current;
    }

    public static double Clip(double x, double lo, double hi) => Math.Max(lo, Math.Min(hi, x));

    /// <summary>Half-to-even rounding to int, NaN maps to 0 (the Python nan_to_num convention).</summary>
    public static int Round0(double x) => double.IsNaN(x) ? 0 : (int)Math.Round(x);
  }

  /// <summary>
  /// Base for the ES-only hourly bar strategies of batch 2: builds ES bars
  /// via a SymbolFeed, ignores every other symbol, and flattens at the last
  /// completable bar (batch-1 end-of-stream fix).
  /// </summary>
  public abstract class CarverEsBase : IStrategy, IForcedFlatAware
  {
    protected readonly StrategyContext _ctx;
    protected readonly SymbolFeed Es;
    int _pos;

    protected int Pos => _pos;

    protected CarverEsBase(StrategyContext ctx)
    {
      _ctx = ctx;
      Es = new SymbolFeed(ctx, "ES", 50.0);
    }

    public virtual void OnForcedFlat() { _pos = 0; }

    public async Task OnTickAsync(StrategyTick tick)
    {
      if (tick.Symbol != "ES") return;
      if (Es.Advance(tick, out var lastCompletable)) await OnBarAsync(lastCompletable);
    }

    /// <summary>Called on each completed ES hourly bar.</summary>
    protected abstract Task OnBarAsync(bool lastCompletable);

    /// <summary>Trade to the signed target (forced to 0 at the last completable bar).</summary>
    protected async Task TradeAsync(int target, bool forceFlat)
    {
      if (forceFlat) target = 0;
      _pos = await CarverCommon.RebalanceAsync(_ctx, "ES", _pos, target);
    }

    /// <summary>
    /// Strategy-3 sizing anchor (unrounded): A3 = 0.20 * 500000 /
    /// (sig_ann * price * 50), sig_ann = previous day's blended daily vol
    /// * 16. NaN until the vol is available.
    /// </summary>
    protected double A3(double price) => double.IsNaN(Es.DailyStd)
      ? double.NaN
      : 0.20 * 500000.0 / (Es.DailyStd * 16.0 * price * 50.0);
  }
}
