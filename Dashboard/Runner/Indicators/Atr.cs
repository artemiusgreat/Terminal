using System;

namespace Runner.Indicators
{
  /// <summary>
  /// Wilder's ATR. Matches Python atr_wilder() in round2.py:
  /// TR[0] = h[0]-l[0], TR[i] = max(h-l, |h-c[i-1]|, |l-c[i-1]|).
  /// First value = SMA of first `period` TRs at index period-1,
  /// then atr[i] = (atr[i-1]*(period-1) + tr[i])/period.
  /// Indices before period-1 are NaN. Returns NaN array if n &lt;= period.
  /// </summary>
  public static class Atr
  {
    public static double[] Compute(double[] h, double[] l, double[] c, int period)
    {
      int n = c.Length;
      var out_ = new double[n];
      for (int i = 0; i < n; i++) out_[i] = double.NaN;
      if (n <= period) return out_;

      var tr = new double[n];
      tr[0] = h[0] - l[0];
      for (int i = 1; i < n; i++)
        tr[i] = Math.Max(h[i] - l[i],
                Math.Max(Math.Abs(h[i] - c[i - 1]), Math.Abs(l[i] - c[i - 1])));

      double s = 0;
      for (int i = 0; i < period; i++) s += tr[i];
      out_[period - 1] = s / period;
      for (int i = period; i < n; i++)
        out_[i] = (out_[i - 1] * (period - 1) + tr[i]) / period;
      return out_;
    }
  }
}
