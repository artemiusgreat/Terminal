using System;
using Runner.Indicators;

namespace Runner.Strategies.Supertrend
{
  /// <summary>
  /// Supertrend. Exact port of Python supertrend_nb in round2.py:
  /// Wilder ATR, basic bands from (h+l)/2 +/- mult*ATR, final bands with
  /// ratchet, direction flips on close vs prior final band.
  /// Direction: +1 uptrend, -1 downtrend, 0 = not enough data.
  /// </summary>
  public static class SupertrendIndicator
  {
    public static (double[] line, int[] direction) Compute(
      double[] h, double[] l, double[] c, int period, double mult)
    {
      int n = c.Length;
      var line = new double[n];
      var direction = new int[n];
      for (int i = 0; i < n; i++) { line[i] = double.NaN; direction[i] = 0; }

      var atrv = Atr.Compute(h, l, c, period);

      var fup = new double[n];
      var flo = new double[n];
      for (int i = 0; i < n; i++) { fup[i] = double.NaN; flo[i] = double.NaN; }

      for (int i = period - 1; i < n; i++)
      {
        if (double.IsNaN(atrv[i])) continue;
        double mid = (h[i] + l[i]) / 2.0;
        double bup = mid + mult * atrv[i];
        double blo = mid - mult * atrv[i];
        if (i == period - 1 || double.IsNaN(fup[i - 1]))
        {
          fup[i] = bup; flo[i] = blo;
        }
        else
        {
          fup[i] = (bup < fup[i - 1] || c[i - 1] > fup[i - 1]) ? bup : fup[i - 1];
          flo[i] = (blo > flo[i - 1] || c[i - 1] < flo[i - 1]) ? blo : flo[i - 1];
        }
      }

      int d = 1;
      for (int i = period - 1; i < n; i++)
      {
        if (double.IsNaN(fup[i])) continue;
        if (!double.IsNaN(fup[i - 1]) && c[i] > fup[i - 1])
          d = 1;
        else if (!double.IsNaN(flo[i - 1]) && c[i] < flo[i - 1])
          d = -1;
        direction[i] = d;
        line[i] = d == 1 ? flo[i] : fup[i];
      }
      return (line, direction);
    }
  }
}
