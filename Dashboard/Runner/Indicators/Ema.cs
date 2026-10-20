using System;

namespace Runner.Indicators
{
  /// <summary>
  /// Exponential moving average. Matches Python ema() in shootout.py:
  /// seed with SMA of first `period` values at index period-1,
  /// k = 2/(period+1). Indices before period-1 are NaN.
  /// </summary>
  public static class Ema
  {
    public static double[] Compute(double[] closes, int period)
    {
      int n = closes.Length;
      var out_ = new double[n];
      for (int i = 0; i < n; i++) out_[i] = double.NaN;
      if (n == 0) return out_;

      double k = 2.0 / (period + 1);
      double s = 0;
      int seedN = Math.Min(period, n);
      for (int i = 0; i < seedN; i++) s += closes[i];
      if (n >= period)
      {
        out_[period - 1] = s / seedN;
        for (int i = period; i < n; i++)
          out_[i] = closes[i] * k + out_[i - 1] * (1 - k);
      }
      return out_;
    }
  }
}
