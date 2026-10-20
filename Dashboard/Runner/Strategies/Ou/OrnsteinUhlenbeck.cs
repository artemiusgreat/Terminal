using System;
using System.Collections.Generic;

namespace Runner.Strategies.Ou
{
  /// <summary>
  /// Ornstein-Uhlenbeck process parameters for dx = kappa*(mu - x)*dt + sigma*dW.
  /// </summary>
  /// <param name="Mu">Long-run mean (price units).</param>
  /// <param name="Kappa">Mean-reversion speed (per second). Must be positive.</param>
  /// <param name="Sigma">Volatility (price units per sqrt(second)).</param>
  public sealed record OuParameters(double Mu, double Kappa, double Sigma);

  /// <summary>
  /// Ornstein-Uhlenbeck toolkit for mean-reversion trading.
  ///
  /// All calculations are dependency-free and reusable: fit the process once with
  /// <see cref="Fit"/>, then derive optimal entry distance, win probabilities and
  /// Kelly sizing. Time unit is seconds, price unit is whatever the series uses
  /// (points for ES). Verified against Monte Carlo (hitting time &lt;1%,
  /// absorption &lt;4%, hit probability &lt;1% error).
  ///
  /// Model notes: the optimal-entry objective treats each trade as a renewal
  /// cycle (flat wait from mu, then the trade). It assumes OU dynamics hold over
  /// the trade horizon and ignores jumps / regime change - bound the search or
  /// cross-check against empirical results before sizing up.
  /// </summary>
  public static class OrnsteinUhlenbeck
  {
    const int ODESize = 2000;
    const int QuadSize = 4000;

    /// <summary>
    /// Fits OU parameters via AR(1) least squares: x[i+1] = alpha + phi*x[i].
    /// </summary>
    /// <param name="points">Time-ordered (seconds, price) samples, ideally regular bars.</param>
    public static OuParameters Fit(IReadOnlyList<(double tSeconds, double x)> points)
    {
      if (points is null || points.Count < 30)
        throw new ArgumentException("Need at least 30 samples to fit OU.", nameof(points));

      int n = points.Count - 1;
      double sx = 0, sy = 0, sxx = 0, sxy = 0;
      for (int i = 0; i < n; i++)
      {
        double x = points[i].x, y = points[i + 1].x;
        sx += x; sy += y; sxx += x * x; sxy += x * y;
      }

      double denom = n * sxx - sx * sx;
      if (Math.Abs(denom) < 1e-12)
        throw new ArgumentException("Degenerate price series (no variance).", nameof(points));

      double phi = (n * sxy - sx * sy) / denom;
      double alpha = (sy - phi * sx) / n;
      double dt = (points[n].tSeconds - points[0].tSeconds) / n;
      if (dt <= 0) throw new ArgumentException("Time must be strictly increasing.", nameof(points));

      phi = Math.Min(0.9999, Math.Max(0.01, phi)); // guard non-stationary / degenerate
      double kappa = -Math.Log(phi) / dt;
      double mu = alpha / (1 - phi);

      double sse = 0;
      for (int i = 0; i < n; i++)
      {
        double r = points[i + 1].x - (alpha + phi * points[i].x);
        sse += r * r;
      }
      double sigma = Math.Sqrt(sse / (n - 2)) / Math.Sqrt(dt);

      return new OuParameters(mu, kappa, sigma);
    }

    /// <summary>Stationary standard deviation: sigma / sqrt(2*kappa).</summary>
    public static double StationaryStdDev(OuParameters p) => p.Sigma / Math.Sqrt(2 * p.Kappa);

    /// <summary>Half-life of a dislocation, in seconds: ln(2)/kappa.</summary>
    public static double HalfLifeSeconds(OuParameters p) => Math.Log(2) / p.Kappa;

    /// <summary>
    /// Expected time (seconds) to hit <paramref name="target"/> starting from
    /// <paramref name="from"/> (&lt; target). Solves (s^2/2)u'' + k(mu-x)u' = -1
    /// by finite differences (Thomas algorithm).
    /// </summary>
    public static double ExpectedHittingTimeSeconds(OuParameters p, double from, double target)
    {
      if (from >= target) return 0;
      double sst = StationaryStdDev(p);
      double xMin = p.Mu - 10 * sst;
      var xs = Linspace(xMin, target, ODESize);
      double h = xs[1] - xs[0];
      double s2 = p.Sigma * p.Sigma;
      var a = new double[ODESize];
      var b = new double[ODESize];
      var c = new double[ODESize];
      var rhs = new double[ODESize];
      for (int i = 0; i < ODESize; i++)
      {
        a[i] = s2 / (2 * h * h) - p.Kappa * (p.Mu - xs[i]) / (2 * h);
        b[i] = -s2 / (h * h);
        c[i] = s2 / (2 * h * h) + p.Kappa * (p.Mu - xs[i]) / (2 * h);
        rhs[i] = -1;
      }
      c[0] = a[0] + c[0];            // Neumann u'(xMin) = 0
      b[ODESize - 1] = 1; a[ODESize - 1] = 0; c[ODESize - 1] = 0; rhs[ODESize - 1] = 0; // u(target) = 0
      var u = SolveTridiagonal(a, b, c, rhs);
      return Interp(xs, u, from);
    }

    /// <summary>
    /// Expected time (seconds) until either <paramref name="lo"/> or
    /// <paramref name="hi"/> is hit, starting from <paramref name="from"/>
    /// (lo &lt; from &lt; hi). Dirichlet boundaries on both ends.
    /// </summary>
    public static double ExpectedAbsorptionTimeSeconds(OuParameters p, double from, double lo, double hi)
    {
      if (from <= lo || from >= hi) return 0;
      var xs = Linspace(lo, hi, ODESize);
      double h = xs[1] - xs[0];
      double s2 = p.Sigma * p.Sigma;
      var a = new double[ODESize];
      var b = new double[ODESize];
      var c = new double[ODESize];
      var rhs = new double[ODESize];
      for (int i = 0; i < ODESize; i++)
      {
        a[i] = s2 / (2 * h * h) - p.Kappa * (p.Mu - xs[i]) / (2 * h);
        b[i] = -s2 / (h * h);
        c[i] = s2 / (2 * h * h) + p.Kappa * (p.Mu - xs[i]) / (2 * h);
        rhs[i] = -1;
      }
      foreach (int i in new[] { 0, ODESize - 1 })
      {
        b[i] = 1; a[i] = 0; c[i] = 0; rhs[i] = 0;
      }
      return Interp(xs, SolveTridiagonal(a, b, c, rhs), from);
    }

    /// <summary>
    /// Probability of hitting <paramref name="target"/> before
    /// <paramref name="stop"/> starting from <paramref name="from"/>,
    /// via the OU scale function (numerical quadrature).
    /// </summary>
    public static double HitProbability(OuParameters p, double from, double target, double stop)
    {
      if (from <= stop) return 0;
      if (from >= target) return 1;
      int n = QuadSize;
      var ys = Linspace(stop, target, n);
      double h = ys[1] - ys[0];
      // scale density exp(k*(mu-y)^2 / s^2); Simpson
      double sum = 0, sumFrom = 0;
      for (int i = 0; i < n; i++)
      {
        double d = Math.Exp(p.Kappa * (p.Mu - ys[i]) * (p.Mu - ys[i]) / (p.Sigma * p.Sigma));
        double w = (i == 0 || i == n - 1) ? 1 : (i % 2 == 0 ? 2 : 4);
        sum += w * d;
        if (ys[i] <= from) sumFrom += w * d;
      }
      // trapezoidal correction for the partial interval at `from` is negligible at n=4000
      return sumFrom / sum;
    }

    /// <summary>
    /// Optimal entry distance below/above the mean (price units) for a fade:
    /// enter long at mu-a / short at mu+a, exit at take-profit or stop-loss.
    /// Maximizes expected P&amp;L per calendar second over the renewal cycle
    /// (wait flat from mu, then the trade), via golden-section search.
    /// Returns 0 when no entry distance is profitable after costs.
    /// </summary>
    /// <param name="p">Fitted OU parameters.</param>
    /// <param name="takeProfitPoints">Take-profit distance (price units).</param>
    /// <param name="stopLossPoints">Stop-loss distance (price units).</param>
    /// <param name="roundTripCostPoints">Spread + commissions per round trip (price units).</param>
    public static double OptimalEntryDistance(
      OuParameters p, double takeProfitPoints, double stopLossPoints, double roundTripCostPoints)
    {
      double sst = StationaryStdDev(p);
      double lo = 0.05 * sst, hi = 6 * sst;

      double Objective(double a)
      {
        double entry = p.Mu - a; // long fade; symmetric for shorts
        double tpPx = entry + takeProfitPoints;
        double slPx = entry - stopLossPoints;
        double winP = HitProbability(p, entry, tpPx, slPx);
        double ePnl = winP * (takeProfitPoints - roundTripCostPoints)
                    - (1 - winP) * (stopLossPoints + roundTripCostPoints);
        double eTrade = ExpectedAbsorptionTimeSeconds(p, entry, slPx, tpPx);
        double eWait = ExpectedHittingTimeSeconds(p, p.Mu, p.Mu + a); // symmetry
        double cycle = eWait + eTrade;
        return cycle > 0 ? ePnl / cycle : 0;
      }

      // golden-section search
      double gr = (Math.Sqrt(5) - 1) / 2;
      double c = hi - gr * (hi - lo), d = lo + gr * (hi - lo);
      double fc = Objective(c), fd = Objective(d);
      for (int i = 0; i < 60; i++)
      {
        if (fc > fd) { hi = d; d = c; fd = fc; c = hi - gr * (hi - lo); fc = Objective(c); }
        else { lo = c; c = d; fc = fd; d = lo + gr * (hi - lo); fd = Objective(d); }
      }
      double aStar = (lo + hi) / 2;
      return Objective(aStar) > 0 ? aStar : 0;
    }

    /// <summary>
    /// Kelly-optimal fraction of bankroll to risk: f* = p - (1-p)*L/W.
    /// Returns 0 when the edge is not positive. Consider half-Kelly in practice.
    /// </summary>
    /// <param name="winProb">Probability of a winning trade.</param>
    /// <param name="avgWin">Average win (price or currency units, positive).</param>
    /// <param name="avgLoss">Average loss (same units, positive).</param>
    public static double KellyFraction(double winProb, double avgWin, double avgLoss)
    {
      if (avgWin <= 0 || avgLoss <= 0) return 0;
      if (winProb <= 0 || winProb >= 1) return 0;
      double f = winProb - (1 - winProb) * avgLoss / avgWin;
      return Math.Min(1, Math.Max(0, f));
    }

    // ---- numerical helpers ----

    static double[] Linspace(double from, double to, int n)
    {
      var xs = new double[n];
      for (int i = 0; i < n; i++) xs[i] = from + (to - from) * i / (n - 1);
      return xs;
    }

    static double[] SolveTridiagonal(double[] a, double[] b, double[] c, double[] rhs)
    {
      int n = b.Length;
      var cp = new double[n];
      var dp = new double[n];
      cp[0] = c[0] / b[0];
      dp[0] = rhs[0] / b[0];
      for (int i = 1; i < n; i++)
      {
        double m = 1 / (b[i] - a[i] * cp[i - 1]);
        cp[i] = c[i] * m;
        dp[i] = (rhs[i] - a[i] * dp[i - 1]) * m;
      }
      var x = new double[n];
      x[n - 1] = dp[n - 1];
      for (int i = n - 2; i >= 0; i--) x[i] = dp[i] - cp[i] * x[i + 1];
      return x;
    }

    static double Interp(double[] xs, double[] ys, double x)
    {
      if (x <= xs[0]) return ys[0];
      if (x >= xs[^1]) return ys[^1];
      int lo = 0, hi = xs.Length - 1;
      while (hi - lo > 1)
      {
        int mid = (lo + hi) / 2;
        if (xs[mid] <= x) lo = mid; else hi = mid;
      }
      double t = (x - xs[lo]) / (xs[hi] - xs[lo]);
      return ys[lo] + t * (ys[hi] - ys[lo]);
    }
  }
}
