using Core.Enums;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Runner.Strategies.ConnorsPctB
{
  /// <summary>
  /// Connors %B (Python round2 s_pctb): %B(20,2) on 1-min closes.
  /// Long-only: enter when %B crosses below 0 (price broke through the lower
  /// band), exit when %B recovers above 0.5 or after 10 bars max.
  /// 1-day test cutoff: flattens and requests shutdown after 24h of data.
  /// </summary>
  public sealed class ConnorsPctBStrategy : IStrategy, IForcedFlatAware
  {
    readonly StrategyContext _ctx;

    const int CpbPeriod = 20;
    const double CpbMult = 2.0;
    const double CpbExitLevel = 0.5;
    const int CpbMaxBars = 10;
    readonly List<double> cpbCloses = new();
    long cpbLastBarMin = -1;
    double cpbBarSum, cpbBarClose;
    int cpbBarN;
    double cpbPrevPctB = double.NaN;
    int cpbPos;       // 0 flat, 1 long
    int cpbBarsHeld;

    const string symName = "ES";

    public ConnorsPctBStrategy(StrategyContext ctx)
    {
      _ctx = ctx;
    }

    public void OnForcedFlat() { cpbPos = 0; }

    public async Task OnTickAsync(StrategyTick tick)
    {
      if (tick.Symbol != "ES") return;

      var t = tick.TimeNs;
      var mid = tick.Mid;

      // Build 1-min bars from tick mids
      var cpbBarMin = t / 60000000000L;
      if (cpbBarMin != cpbLastBarMin)
      {
        if (cpbLastBarMin >= 0 && cpbBarN > 0)
        {
          cpbCloses.Add(cpbBarClose); // bar close = last tick's mid (not average)
          if (cpbCloses.Count > 40) cpbCloses.RemoveAt(0);

          if (cpbCloses.Count >= CpbPeriod + 1)
          {
            // Shifted bands (Python-compatible): bands from PRIOR 20 closes,
            // %B = (current close - lower) / (upper - lower).
            var pctB = ShiftedPctB(cpbCloses, CpbPeriod, CpbMult);

            if (cpbPos == 0 && !double.IsNaN(cpbPrevPctB) && cpbPrevPctB >= 0 && pctB < 0)
            {
              // %B crossed below 0: price broke through lower band -> long
              if (await _ctx.OpenPosition(symName, OrderSideEnum.Long, 1))
              { cpbPos = 1; cpbBarsHeld = 0; }
            }
            else if (cpbPos == 1)
            {
              cpbBarsHeld++;
              if (pctB > CpbExitLevel || cpbBarsHeld >= CpbMaxBars)
              {
                cpbPos = 0;
                await _ctx.ClosePosition();
              }
            }
            cpbPrevPctB = pctB;
          }
        }
        cpbLastBarMin = cpbBarMin; cpbBarSum = cpbBarClose = mid; cpbBarN = 1;
      }
      else { cpbBarSum += mid; cpbBarN++; cpbBarClose = mid; }
    }

    /// <summary>
    /// %B with shifted bands: bands computed from the PRIOR <paramref name="period"/>
    /// closes (excluding the current bar), %B evaluated at the current close.
    /// Matches Python bollinger() which assigns bands from closes[i-w..i-1] at bar i.
    /// </summary>
    static double ShiftedPctB(List<double> closes, int period, double mult)
    {
      int n = closes.Count;
      // prior `period` closes: indices [n-1-period .. n-2]; current close at [n-1]
      double sum = 0;
      for (int i = n - 1 - period; i <= n - 2; i++) sum += closes[i];
      double mean = sum / period;
      double var = 0;
      for (int i = n - 1 - period; i <= n - 2; i++) var += (closes[i] - mean) * (closes[i] - mean);
      double sd = Math.Sqrt(Math.Max(var / period, 0.0));
      double upper = mean + mult * sd;
      double lower = mean - mult * sd;
      double range = upper - lower;
      if (range <= 0) return 0.5;
      return (closes[n - 1] - lower) / range;
    }
  }
}
