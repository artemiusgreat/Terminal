# TS Momentum

Time-series momentum from Kakushadze "151 Trading Strategies" (§10.4).

## Rules

1. Compute 60-bar return: `ret = (close[i] - close[i-60]) / close[i-60]`
2. Every 60 bars starting at bar 120: regime = +1 if ret > 0, else -1
3. Forward-fill regime between re-evaluations
4. **Long** when regime flips to +1 (entry delayed 1 bar)
5. **Short** when regime flips to -1 (entry delayed 1 bar)
6. Exit long when regime flips to -1; exit short when flips to +1
7. Also enters on initial regime establishment (bar 120 → entry at 121)

Both directions. 1 lot. No stop-loss/take-profit — pure regime following.

## Parameters

| Param | Value |
|---|---|
| Lookback | 60 bars |
| Re-eval interval | 60 bars |
| First eval | Bar 120 |

## Metrics

**8-day Python backtest** (ES 1-min, 2026-06-30 → 2026-07-08, $1/side):

| Metric | Value |
|---|---|
| Net | +$10,178 |
| Trades | 55 |
| Win % | 43.6% |
| Profit Factor | 2.11 |
| Sharpe | 9.83 |
| Max Drawdown | $1,546 |
| $/trade | $185.05 |
| Avg Win / Avg Loss | $807 / -$296 |
| Max Consec Win / Loss | 3 / 4 |
| Recovery | 6.59 |

**600-bar C# validation:** 4/4 trades match Python (systematic +1 bar execution offset).

**Topstep $1000/day stop:** Net unchanged ($10,178), 0 stop-outs. Avg hold 130 min — too long for event management.

**C# Terminal backtest — 3 days, Topstep rules** (ES trade ticks via MemoryMapReader, Jul 1–3 2026 ET; pipeline-enforced $1000/day stop and no trading 16:00–18:00 ET; commission $1 per closed trade in C#, $1/side in Python):

| Metric | C# | Python (same window) |
|---|---|---|
| Net | $-2,693 | $-2,736 |
| Trades | 18 | 18 |
| Win % | 33.3% | 38.9% |
| Profit Factor | 0.36 | 0.36 |
| Sharpe | -11.46 | -11.71 |
| Max Drawdown | $3,509 | $3,518 |
| $/trade | $-149.61 | $-152.00 |
| Avg Win / Avg Loss | $255 / $-352 | $216 / $-386 |
| Max Consec Win / Loss | 2 / 6 | 2 / 6 |
| Recovery | -0.77 | -0.78 |

C# and Python agree closely on this window (trade counts within 1, PF within 0.1); residual differences are fill timing and the commission convention.
