using System;

namespace Runner.Strategies.ConnorsPctB
{
  /// <summary>
  /// Bollinger Bands: SMA middle, population stddev, %B = (close-lower)/(upper-lower).
  /// Bands at index i use closes[i-period+1..i] (window ending at i, inclusive).
  /// Indices before period-1 are NaN. %B is 0.5 when band width is zero.
  /// </summary>
  public static class BollingerBands
  {
    public static (double[] upper, double[] middle, double[] lower, double[] pctB)
      Compute(double[] closes, int period, double mult)
    {
      int n = closes.Length;
      var upper = new double[n];
      var middle = new double[n];
      var lower = new double[n];
      var pctB = new double[n];
      for (int i = 0; i < n; i++)
      {
        upper[i] = double.NaN; middle[i] = double.NaN;
        lower[i] = double.NaN; pctB[i] = double.NaN;
      }

      for (int i = period - 1; i < n; i++)
      {
        double sum = 0;
        for (int j = i - period + 1; j <= i; j++) sum += closes[j];
        double mean = sum / period;
        double var = 0;
        for (int j = i - period + 1; j <= i; j++) var += (closes[j] - mean) * (closes[j] - mean);
        double sd = Math.Sqrt(var / period);
        double up = mean + mult * sd;
        double lo = mean - mult * sd;
        middle[i] = mean; upper[i] = up; lower[i] = lo;
        double range = up - lo;
        pctB[i] = range > 0 ? (closes[i] - lo) / range : 0.5;
      }
      return (upper, middle, lower, pctB);
    }
  }
}
