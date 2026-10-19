using Core.Enums;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Runner.Strategies.TsMomentum
{
  /// <summary>
  /// Time-series momentum (Kakushadze §10.4, Python round3 s_tsmom):
  /// sign of past 60-bar return, re-evaluated every 60 bars starting at bar 120.
  /// Regime forward-filled between re-evals. Long on flip to +1, short on flip
  /// to -1; entries delayed 1 bar after the flip (engine can't flip same-bar),
  /// exits on the flip bar. Both directions, 1 lot.
  /// </summary>
  public sealed class TsMomentumStrategy : IStrategy
  {
    readonly StrategyContext _ctx;

    const int RetPeriod = 60;
    const int FirstEval = 120;
    const int ReEvalEvery = 60;

    readonly List<double> _closes = new();
    // OHLC bar building from tick mids
    long _lastBarMin = -1;
    double _barSum, _barClose; int _barN;

    int _regime;          // +1, -1, 0 = no regime yet
    int _pos;             // +1 long, -1 short, 0 flat
    int _pendingEntry;    // +1 long, -1 short, 0 none (1-bar delayed entry)
    int _barsSinceEval;

    const string symName = "ES";

    public TsMomentumStrategy(StrategyContext ctx) { _ctx = ctx; }

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
          _closes.Add(_barClose);
          await OnBarCloseAsync();
        }
        _lastBarMin = barMin; _barSum = _barClose = mid; _barN = 1;
      }
      else { _barSum += mid; _barN++; _barClose = mid; }
    }

    async Task OnBarCloseAsync()
    {
      int n = _closes.Count; // number of completed bars
      int i = n - 1;         // index of just-closed bar

      // 1) Execute pending entry from last bar's regime flip
      if (_pendingEntry != 0 && _pos == 0)
      {
        _pos = _pendingEntry;
        var side = _pos > 0 ? OrderSideEnum.Long : OrderSideEnum.Short;
        await _ctx.OpenPosition(symName, side, 1);
        _pendingEntry = 0;
      }

      // 2) Re-evaluate regime every 60 bars starting at bar 120 (0-indexed: n>=121)
      _barsSinceEval++;
      bool isEvalBar = n >= FirstEval + 1 && ((n - 1 - FirstEval) % ReEvalEvery == 0);
      if (isEvalBar)
      {
        double ret = (_closes[i] - _closes[i - RetPeriod]) / _closes[i - RetPeriod];
        int newRegime = ret > 0 ? 1 : -1;
        if (_regime == 0)
        {
          // First regime establishment: enter delayed 1 bar (Python: chg on 0->regime)
          _pendingEntry = newRegime;
        }
        else if (newRegime != _regime)
        {
          // Flip: exit now (lx/sx on flip bar), enter delayed 1 bar
          if (_pos != 0) { _pos = 0; await _ctx.ClosePosition(); }
          _pendingEntry = newRegime;
        }
        _regime = newRegime;
        _barsSinceEval = 0;
      }
    }
  }
}
