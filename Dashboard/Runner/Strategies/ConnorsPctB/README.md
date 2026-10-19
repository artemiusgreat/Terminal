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
