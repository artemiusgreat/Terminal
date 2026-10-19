using Core.Enums;
using Estimator.Services;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Runner.Strategies.LeadLag
{
  /// <summary>
  /// Lead-lag follower (experimental): NQ leads ES by ~0.5s (Hayashi-Yoshida).
  /// Entry: NQ impulse >= 6 ticks in 500ms while ES moved < 2 ticks (hasn't
  /// reacted) -> trade ES in NQ's direction. Exit: +3t TP, -3t SL, or 2s timeout.
  /// 3-hour test cutoff: requests shutdown after 3h of data.
  /// </summary>
  public sealed class LeadLagStrategy : IStrategy
  {
    readonly StrategyContext _ctx;

    // Lead-lag measurement (Hayashi-Yoshida)
    readonly LeadService leadService = new LeadService(5L * 60 * 1000000000L); // 5-min frame
    double leadPrevEsPx = double.NaN, leadPrevNqPx = double.NaN;
    long leadPrevEsT = 0, leadPrevNqT = 0;
    long leadLastLogT = 0;
    static readonly long[] leadLagsNs = new long[] {
      -10_000_000_000L, -5_000_000_000L, -2_000_000_000L, -1_000_000_000L, -500_000_000L,
      0,
      500_000_000L, 1_000_000_000L, 2_000_000_000L, 5_000_000_000L, 10_000_000_000L
    };

    // Lead-lag follower strategy: NQ leads ES by ~0.5s
    const long LlWindowNs = 500_000_000L; // 500ms impulse window
    const double LlNqImpulseTicks = 6;    // NQ must move >= 6 ticks
    const double LlEsQuietTicks = 2;     // ES must move < 2 ticks (hasn't reacted)
    const double LlTpTicks = 3, LlSlTicks = 3;
    const long LlTimeoutNs = 2_000_000_000L; // 2s
    readonly Queue<(long t, double px)> llNqQ = new(), llEsQ = new();
    int llPos = 0; // +1 long ES, -1 short ES, 0 flat
    double llEntryPx = 0;
    long llEntryT = 0;
    long llFirstT = 0;
    const long LlTestNs = 3L * 60 * 60 * 1000000000L; // 3-hour test window

    public LeadLagStrategy(StrategyContext ctx)
    {
      _ctx = ctx;
    }

    public Task OnTickAsync(StrategyTick tick)
    {
      var t = tick.TimeNs;
      var mid = tick.Mid;
      var sym = tick.Symbol;

      // Lead-lag: feed log returns into Hayashi-Yoshida estimator
      if (sym == "ES" || sym == "NQ")
      {
        if (sym == "ES")
        {
          if (!double.IsNaN(leadPrevEsPx) && t > leadPrevEsT)
          {
            var r = Math.Log(mid / leadPrevEsPx);
            if (Math.Abs(r) < 0.01) leadService.UpdateX(leadPrevEsT, t, r);
          }
          leadPrevEsPx = mid; leadPrevEsT = t;
        }
        else
        {
          if (!double.IsNaN(leadPrevNqPx) && t > leadPrevNqT)
          {
            var r = Math.Log(mid / leadPrevNqPx);
            if (Math.Abs(r) < 0.01) leadService.UpdateY(leadPrevNqT, t, r);
          }
          leadPrevNqPx = mid; leadPrevNqT = t;
        }
        leadService.Trim(t);
        if (t - leadLastLogT >= 5L * 60 * 1000000000L && leadService.IsReady)
        {
          leadLastLogT = t;
          var sb = new StringBuilder();
          sb.Append($"LEADLAG t={t} ");
          foreach (var lag in leadLagsNs)
          {
            var c = leadService.Correlation(lag);
            sb.Append($"lag{lag / 1000000000.0:+0.0;-0.0}s={(c.HasValue ? c.Value.ToString("F3") : "n/a")} ");
          }
          Console.WriteLine(sb.ToString());
        }
      }

      // Lead-lag follower: trade ES on NQ impulse
      var q = sym == "NQ" ? llNqQ : (sym == "ES" ? llEsQ : null);
      if (q != null)
      {
        q.Enqueue((t, mid));
        while (q.Count > 0 && q.Peek().t < t - 1_000_000_000L) q.Dequeue(); // keep 1s

        if (sym == "NQ" && llPos == 0 && llNqQ.Count >= 2 && llEsQ.Count >= 2)
        {
          // NQ impulse: price change over last 500ms
          var nqOld = llNqQ.Peek(); // oldest in 1s window, approx 500ms+ ago
          // Find price ~500ms ago
          double nqRef = nqOld.px;
          foreach (var (qt, qpx) in llNqQ) { if (qt >= t - LlWindowNs) { nqRef = qpx; break; } }
          var nqMoveTicks = (mid - nqRef) / 0.25;

          // ES move over same window
          double esRef = llEsQ.Peek().px;
          // Get last ES price
          var esLast = 0.0;
          foreach (var (qt, qpx) in llEsQ) { esLast = qpx; }
          double esRef2 = esLast;
          foreach (var (qt, qpx) in llEsQ) { if (qt >= t - LlWindowNs) { esRef2 = qpx; break; } }
          var esMoveTicks = (esLast - esRef2) / 0.25;

          if (Math.Abs(nqMoveTicks) >= LlNqImpulseTicks && Math.Abs(esMoveTicks) < LlEsQuietTicks)
          {
            var dir = nqMoveTicks > 0 ? 1 : -1;
            llPos = dir;
            llEntryPx = esLast;
            llEntryT = t;
            var side = dir > 0 ? OrderSideEnum.Long : OrderSideEnum.Short;
            _ = _ctx.OpenPosition("ES", side, 1);
          }
        }

        // Exit logic (on ES ticks)
        if (sym == "ES" && llPos != 0)
        {
          var pnlTicks = (mid - llEntryPx) / 0.25 * llPos;
          var timedOut = t - llEntryT >= LlTimeoutNs;
          if (pnlTicks >= LlTpTicks || pnlTicks <= -LlSlTicks || timedOut)
          {
            llPos = 0;
            _ = _ctx.ClosePosition();
          }
        }
      }

      // 3-hour test cutoff
      if (llFirstT == 0) llFirstT = t;
      if (t - llFirstT >= LlTestNs)
      {
        _ = Task.Run(() => _ctx.RequestShutdownAsync());
      }

      return Task.CompletedTask;
    }
  }
}
