using Core.Enums;
using Runner.Indicators;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Runner.Strategies.Supertrend
{
  /// <summary>
  /// Supertrend(10, 3.0), inventor's settings (Python round2 s_supertrend).
  /// Long-only: enter when direction flips -1 -> +1, exit when flips +1 -> -1.
  /// 1 lot. Bars built from tick mids (HLC from mid).
  /// </summary>
  public sealed class SupertrendStrategy : IStrategy, IForcedFlatAware
  {
    readonly StrategyContext _ctx;

    const int StPeriod = 10;
    const double StMult = 3.0;

    readonly List<double> _h = new();
    readonly List<double> _l = new();
    readonly List<double> _c = new();
    long _lastBarMin = -1;
    double _barOpen, _barHigh, _barLow, _barSum, _barClose;
    int _barN;

    int _pos; // 0 flat, 1 long
    int _prevDir;

    const string symName = "ES";

    public SupertrendStrategy(StrategyContext ctx) { _ctx = ctx; }

    public void OnForcedFlat() { _pos = 0; }

    public async Task OnTickAsync(StrategyTick tick)
    {
      if (tick.Symbol != "ES") return;
      var t = tick.TimeNs;
      var mid = tick.Mid;

      long barMin = t / _ctx.BarNs;
      if (barMin != _lastBarMin)
      {
        if (_lastBarMin >= 0 && _barN > 0)
        {
          _h.Add(_barHigh); _l.Add(_barLow); _c.Add(_barClose);
          if (_h.Count > 300) { _h.RemoveAt(0); _l.RemoveAt(0); _c.RemoveAt(0); }
          await OnBarCloseAsync();
        }
        _lastBarMin = barMin;
        _barOpen = _barHigh = _barLow = _barSum = _barClose = mid; _barN = 1;
      }
      else
      {
        if (mid > _barHigh) _barHigh = mid;
        if (mid < _barLow) _barLow = mid;
        _barSum += mid; _barN++; _barClose = mid;
      }
    }

    async Task OnBarCloseAsync()
    {
      int n = _c.Count;
      if (n < StPeriod) return;

      var ha = _h.ToArray(); var la = _l.ToArray(); var ca = _c.ToArray();
      var (_, direction) = SupertrendIndicator.Compute(ha, la, ca, StPeriod, StMult);

      int i = n - 1;
      int dir = direction[i];
      int prevDir = i > 0 ? direction[i - 1] : 0;

      // Python: le when dc flips -1->+1, lx when +1->-1 (both require ok)
      bool ok = dir != 0 && prevDir != 0;
      if (_pos == 0 && ok && prevDir == -1 && dir == 1)
      {
        if (await _ctx.OpenPosition(symName, OrderSideEnum.Long, 1)) _pos = 1;
      }
      else if (_pos == 1 && ok && prevDir == 1 && dir == -1)
      {
        _pos = 0;
        await _ctx.ClosePosition();
      }
      _prevDir = dir;
    }
  }
}
