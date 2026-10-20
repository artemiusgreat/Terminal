using System;
using Runner.Indicators;

namespace Runner.Strategies.KeltnerMr
{
  /// <summary>
  /// Keltner Channel: middle = EMA(closes, emaPeriod),
  /// upper/lower = EMA +/- atrMult * WilderATR(h,l,c,emaPeriod).
  /// Matches Python s_keltner in round2.py (30-bar EMA, 1.3x ATR).
  /// Indices before emaPeriod-1 are NaN.
  /// </summary>
  public static class KeltnerChannel
  {
    public static (double[] upper, double[] middle, double[] lower)
      Compute(double[] h, double[] l, double[] c, int emaPeriod, double atrMult)
    {
      int n = c.Length;
      var upper = new double[n];
      var middle = new double[n];
      var lower = new double[n];
      for (int i = 0; i < n; i++)
      {
        upper[i] = double.NaN; middle[i] = double.NaN; lower[i] = double.NaN;
      }

      var me = Ema.Compute(c, emaPeriod);
      var av = Atr.Compute(h, l, c, emaPeriod);

      for (int i = 0; i < n; i++)
      {
        if (double.IsNaN(me[i]) || double.IsNaN(av[i])) continue;
        middle[i] = me[i];
        upper[i] = me[i] + atrMult * av[i];
        lower[i] = me[i] - atrMult * av[i];
      }
      return (upper, middle, lower);
    }
  }
}
