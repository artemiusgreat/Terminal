# Connors %B

Mean-reversion buying panic breakdowns through the lower Bollinger Band.

## Rules

1. Compute Bollinger Bands (20, 2.0) with **shifted** windows: bands at bar i use closes[i-20..i-1]
2. Compute %B = (close[i] - lower) / (upper - lower)
3. **Long** when %B crosses BELOW 0 (price broke through lower band)
4. **Exit long** when %B rises above 0.5 (recovered to mid-band) OR after 10 bars max

Long-only. 1 lot. Bar close = last tick's mid (not averaged).

## Files

- `ConnorsPctBStrategy.cs` — strategy logic
- `BollingerBands.cs` — indicator (SMA + population stddev + %B)

## Parameters

| Param | Value |
|---|---|
| Period | 20 |
| Stddev multiplier | 2.0 |
| Exit level | 0.5 |
| Max hold | 10 bars |

## Metrics

**8-day Python backtest** (ES 1-min, 2026-06-30 → 2026-07-08, $1/side):

| Metric | Value |
|---|---|
| Net | +$1,419 |
| Trades | 203 |
| Win % | 53.2% |
| Profit Factor | 1.09 |
| Sharpe | 2.25 |
| Max Drawdown | $2,569 |
| $/trade | $6.99 |
| Avg Win / Avg Loss | $158 / -$164 |
| Max Consec Win / Loss | 6 / 6 |
| Recovery | 0.55 |

**600-bar C# validation:** 20/20 trades match Python (systematic +1 bar execution offset).

**Topstep $1000/day stop:** Net improves to $2,252 (+$833). Only 1 stop-out in 8 days. Avg hold 8 min — best for event management. **Top pick for Topstep.**

**C# Terminal backtest — 3 days, Topstep rules** (ES trade ticks via MemoryMapReader, Jul 1–3 2026 ET; pipeline-enforced $1000/day stop and no trading 16:00–18:00 ET; commission $1 per closed trade in C#, $1/side in Python):

| Metric | C# | Python (same window) |
|---|---|---|
| Net | $-1,008 | $-1,252 |
| Trades | 70 | 70 |
| Win % | 51.4% | 54.3% |
| Profit Factor | 0.78 | 0.74 |
| Sharpe | -8.03 | -9.30 |
| Max Drawdown | $1,630 | $1,928 |
| $/trade | $-14.39 | $-17.89 |
| Avg Win / Avg Loss | $99 / $-134 | $96 / $-153 |
| Max Consec Win / Loss | 5 / 5 | 7 / 4 |
| Recovery | -0.62 | -0.65 |

C# and Python agree closely on this window (trade counts within 1, PF within 0.1); residual differences are fill timing and the commission convention.

Note: this 3-day window (Jul 1–3, downtrend + holiday-shortened Jul 3) is much harder than the 8-day window above — both engines lose here. The 8-day Topstep pick stands on the longer sample.
