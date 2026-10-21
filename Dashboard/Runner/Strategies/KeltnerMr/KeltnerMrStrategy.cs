using Core.Enums;
using Runner.Indicators;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Runner.Strategies.KeltnerMr
{
  /// <summary>
  /// Keltner Channel mean reversion (Python round2 s_keltner):
  /// 30-bar EMA, 1.3x Wilder ATR. Long-only: enter when close crosses
  /// below the lower band, exit when close crosses above the EMA middle.
  /// 1 lot. Bars built from tick mids (HLC from mid).
  /// </summary>
  public sealed class KeltnerMrStrategy : IStrategy, IForcedFlatAware
  {
    readonly StrategyContext _ctx;

    const int KcPeriod = 30;
    const double KcMult = 1.3;

    readonly List<double> _h = new();
    readonly List<double> _l = new();
    readonly List<double> _c = new();
    long _lastBarMin = -1;
    double _barHigh, _barLow, _barSum, _barClose;
    int _barN;

    int _pos; // 0 flat, 1 long

    const string symName = "ES";

    public KeltnerMrStrategy(StrategyContext ctx) { _ctx = ctx; }

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
        _barHigh = _barLow = _barSum = _barClose = mid; _barN = 1;
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
      if (n < KcPeriod + 1) return; // need current + prior for cross detection

      var ha = _h.ToArray(); var la = _l.ToArray(); var ca = _c.ToArray();
      var (upper, middle, lower) = KeltnerChannel.Compute(ha, la, ca, KcPeriod, KcMult);

      int i = n - 1;
      // Python: le = _cross_below(c, lo); lx = _cross_above(c, me)
      bool loOk = !double.IsNaN(lower[i]) && !double.IsNaN(lower[i - 1]);
      bool meOk = !double.IsNaN(middle[i]) && !double.IsNaN(middle[i - 1]);

      if (_pos == 0 && loOk && ca[i] < lower[i] && ca[i - 1] >= lower[i - 1])
      {
        if (await _ctx.OpenPosition(symName, OrderSideEnum.Long, 1)) _pos = 1;
      }
      else if (_pos == 1 && meOk && ca[i] > middle[i] && ca[i - 1] <= middle[i - 1])
      {
        _pos = 0;
        await _ctx.ClosePosition();
      }
    }
  }
}
