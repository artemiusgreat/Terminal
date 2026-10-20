using Core.Enums;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Runner.Strategies.Ou
{
  /// <summary>
  /// OU mean-reversion (committed baseline): 60-min rolling tick window with
  /// fixed 1.5-sigma entry, night-session filter (entries 18:00-09:30 ET, flat
  /// through the 16:00-18:00 clearing halt), 20-tick SL / 10-tick TP, 1 lot.
  /// Refits the OU process on 1-min bars every 15 min (used for diagnostics;
  /// entry distance is the fixed 1.5-sigma band).
  /// </summary>
  public sealed class OuStrategy : IStrategy, IForcedFlatAware
  {
    readonly StrategyContext _ctx;

    // Mean-reversion: 1.5-sigma Bollinger fade. Grid-optimal region W=60m, SL=20t, TP=10t.
    const long MrWindowNs = 60L * 60 * 1000000000L;
    const double MrSlTicks = 20, MrTpTicks = 10, MrTickSize = 0.25;
    readonly Queue<(long t, double m)> mrWindow = new();
    double mrSum, mrSumSq;
    double mrPrevMid = double.NaN, mrPrevLo = double.NaN, mrPrevHi = double.NaN;
    bool mrHaveBand;
    int mrPos;
    double mrEntry;
    // OU-derived entry/sizing: 1-min bars -> OU fit every 15 min -> OptimalEntryDistance + Kelly.
    readonly List<(double tSeconds, double x)> ouBars = new();
    long ouLastBarMin = -1;
    double ouBarSum;
    int ouBarN;
    long ouLastRefitNs;
    double ouMu = double.NaN;
    double ouEntryDist;
    double ouContracts = 1;
    const double OuBankroll = 100_000;
    const int OuMaxContracts = 10;
    const long OuRefitNs = 15L * 60 * 1000000000L;

    const string symName = "ES";

    public OuStrategy(StrategyContext ctx)
    {
      _ctx = ctx;
    }

    public void OnForcedFlat() { mrPos = 0; }

    public async Task OnTickAsync(StrategyTick tick)
    {
      var t = tick.TimeNs;
      var mid = tick.Mid;
      var bid = tick.Bid;
      var ask = tick.Ask;

      var etSod = StrategyContext.EtSecondsOfDay(t);
      var mrNight = etSod >= 18 * 3600 || etSod < 9.5 * 3600; // entries 18:00 -> 09:30 ET
      var mrHalt = etSod >= 16 * 3600 && etSod < 18 * 3600;   // 16:00-18:00 ET clearing halt

      mrWindow.Enqueue((t, mid)); mrSum += mid; mrSumSq += mid * mid;
      while (mrWindow.Count > 0 && mrWindow.Peek().t < t - MrWindowNs)
      {
        var o = mrWindow.Dequeue(); mrSum -= o.m; mrSumSq -= o.m * o.m;
      }

      // OU 1-min bars
      var barMin = t / 60000000000L;
      if (barMin != ouLastBarMin)
      {
        if (ouLastBarMin >= 0 && ouBarN > 0)
        {
          ouBars.Add((ouLastBarMin * 60.0, ouBarSum / ouBarN));
          if (ouBars.Count > 60) ouBars.RemoveAt(0);
        }
        ouLastBarMin = barMin; ouBarSum = mid; ouBarN = 1;
      }
      else { ouBarSum += mid; ouBarN++; }

      // OU refit every 15 min
      if (t - ouLastRefitNs >= OuRefitNs && ouBars.Count >= 60)
      {
        ouLastRefitNs = t;
        try
        {
          var op = OrnsteinUhlenbeck.Fit(ouBars);
          var tpPts = MrTpTicks * MrTickSize;
          var slPts = MrSlTicks * MrTickSize;
          var costPts = MrTickSize + 2 * (1.0 / 12.50 * MrTickSize);
          var ed = OrnsteinUhlenbeck.OptimalEntryDistance(op, tpPts, slPts, costPts);
          if (ed > 0)
          {
            var prevMu = ouMu;
            ouMu = op.Mu;
            ouEntryDist = ed;
            var entryPx = op.Mu - ed;
            var winP = OrnsteinUhlenbeck.HitProbability(op, entryPx, entryPx + tpPts, entryPx - slPts);
            var avgWin = MrTpTicks * 12.50 - 2 * 1.0;
            var avgLoss = MrSlTicks * 12.50 + 2 * 1.0;
            ouContracts = 1; // fixed 1 lot, Kelly removed
            if (double.IsNaN(prevMu)) { mrHaveBand = false; }
          }
        }
        catch { }
      }

      if (mrWindow.Count >= 120)
      {
        var n = (double)mrWindow.Count;
        var mean = mrSum / n;
        var variance = mrSumSq / n - mean * mean;
        if (variance < 0) variance = 0;
        var sd = Math.Sqrt(variance);
        // Fixed 1.5-sigma entry (Python shootout: 1.5σ outperformed 2.0σ on same history)
        const double EntrySigmaMult = 1.5;
        var lo = mean - EntrySigmaMult * sd;
        var hi = mean + EntrySigmaMult * sd;

        if (mrPos == 0 && mrHaveBand && mrNight)
        {
          if (mid < lo)
          {
            if (await _ctx.OpenPosition(symName, OrderSideEnum.Long, ouContracts))
            { mrPos = 1; mrEntry = ask; }
          }
          else if (mid > hi)
          {
            if (await _ctx.OpenPosition(symName, OrderSideEnum.Short, ouContracts))
            { mrPos = -1; mrEntry = bid; }
          }
        }
        else if (mrPos == 1)
        {
          if (mrHalt)
          {
            mrPos = 0;
            await _ctx.ClosePosition();
          }
          else if (bid <= mrEntry - MrSlTicks * MrTickSize || bid >= mrEntry + MrTpTicks * MrTickSize)
          {
            mrPos = 0;
            await _ctx.ClosePosition();
          }
        }
        else if (mrPos == -1)
        {
          if (mrHalt)
          {
            mrPos = 0;
            await _ctx.ClosePosition();
          }
          else if (ask >= mrEntry + MrSlTicks * MrTickSize || ask <= mrEntry - MrTpTicks * MrTickSize)
          {
            mrPos = 0;
            await _ctx.ClosePosition();
          }
        }

        mrPrevMid = mid; mrPrevLo = lo; mrPrevHi = hi; mrHaveBand = true;
      }
    }
  }
}
