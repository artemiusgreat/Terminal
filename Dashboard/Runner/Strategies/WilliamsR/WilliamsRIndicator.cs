using System;

namespace Runner.Strategies.WilliamsR
{
  /// <summary>
  /// Williams %R: (HH - close)/(HH - LL) * -100, HH/LL = rolling max/min
  /// over `period` bars INCLUDING the current bar.
  /// Matches Python roll_maxmin() in shootout.py. -50 when range is zero.
  /// Indices before period-1 are NaN.
  /// </summary>
  public static class WilliamsRIndicator
  {
    public static double[] Compute(double[] h, double[] l, double[] c, int period)
    {
      int n = c.Length;
      var out_ = new double[n];
      for (int i = 0; i < n; i++) out_[i] = double.NaN;

      for (int i = period - 1; i < n; i++)
      {
        double hi = h[i - period + 1];
        double lo = l[i - period + 1];
        for (int j = i - period + 2; j <= i; j++)
        {
          if (h[j] > hi) hi = h[j];
          if (l[j] < lo) lo = l[j];
        }
        double rng = hi - lo;
        out_[i] = rng > 0 ? (hi - c[i]) / rng * -100.0 : -50.0;
      }
      return out_;
    }
  }
}
