using Core.Enums;
using Runner.Indicators;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Runner.Strategies.WilliamsR
{
  /// <summary>
  /// Williams %R(14) (Python shootout strat_williamsr):
  /// long entry on %R cross below -80, exit long when %R > -20;
  /// short entry on %R cross above -20, exit short when %R &lt; -80.
  /// Both directions, 1 lot. Bars built from tick mids (HLC from mid).
  /// </summary>
  public sealed class WilliamsRStrategy : IStrategy
  {
    readonly StrategyContext _ctx;

    const int WrPeriod = 14;
    const double Oversold = -80.0;
    const double Overbought = -20.0;

    readonly List<double> _h = new();
    readonly List<double> _l = new();
    readonly List<double> _c = new();
    long _lastBarMin = -1;
    double _barHigh, _barLow, _barSum, _barClose;
    int _barN;

    int _pos; // +1 long, -1 short, 0 flat

    const string symName = "ES";

    public WilliamsRStrategy(StrategyContext ctx) { _ctx = ctx; }

    public async Task OnTickAsync(StrategyTick tick)
    {
      if (tick.Symbol != "ES") return;
      var t = tick.TimeNs;
      var mid = tick.Mid;

      long barMin = t / 60000000000L;
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
      if (n < WrPeriod + 1) return;

      var wr = WilliamsRIndicator.Compute(_h.ToArray(), _l.ToArray(), _c.ToArray(), WrPeriod);
      int i = n - 1;
      if (double.IsNaN(wr[i]) || double.IsNaN(wr[i - 1])) return;

      // Python engine semantics: on a given bar, EITHER exit OR enter, not both.
      // If pos==1, only lx (wr > -20) exits; se is ignored (no same-bar reversal).
      // If pos==0, le/se entries are checked.
      bool longEntry = wr[i] < Oversold && wr[i - 1] >= Oversold;
      bool shortEntry = wr[i] > Overbought && wr[i - 1] <= Overbought;
      bool exitLong = wr[i] > Overbought;
      bool exitShort = wr[i] < Oversold;

      if (_pos == 1)
      {
        if (exitLong)
        {
          _pos = 0;
          await _ctx.ClosePosition();
        }
      }
      else if (_pos == -1)
      {
        if (exitShort)
        {
          _pos = 0;
          await _ctx.ClosePosition();
        }
      }
      else // _pos == 0: check entries (Python: le/se only when flat)
      {
        if (longEntry)
        {
          _pos = 1;
          await _ctx.OpenPosition(symName, OrderSideEnum.Long, 1);
        }
        else if (shortEntry)
        {
          _pos = -1;
          await _ctx.OpenPosition(symName, OrderSideEnum.Short, 1);
        }
      }
    }
  }
}
