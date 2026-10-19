# Williams %R

Mean-reversion on the Williams %R oscillator. Fades overbought/oversold extremes.

## Rules

1. Compute %R(14): `(HH - close) / (HH - LL) × -100` where HH/LL are 14-bar rolling max/min
2. **Long** when %R crosses BELOW -80 (oversold)
3. **Exit long** when %R rises above -20
4. **Short** when %R crosses ABOVE -20 (overbought)
5. **Exit short** when %R drops below -80

Both directions. 1 lot. No same-bar reversals (if exiting, the bar is done).

## Files

- `WilliamsRStrategy.cs` — strategy logic
- `WilliamsRIndicator.cs` — indicator (matches Python `roll_maxmin`)

## Parameters

| Param | Value |
|---|---|
| Period | 14 |
| Oversold | -80 |
| Overbought | -20 |

## Metrics

**8-day Python backtest** (ES 1-min, 2026-06-30 → 2026-07-08, $1/side):

| Metric | Value |
|---|---|
| Net | +$5,396 |
| Trades | 296 |
| Win % | 64.5% |
| Profit Factor | 1.21 |
| Sharpe | 5.27 |
| Max Drawdown | $4,195 |
| $/trade | $18.23 |
| Avg Win / Avg Loss | $165 / -$250 |
| Max Consec Win / Loss | 7 / 4 |
| Recovery | 1.29 |

**600-bar C# validation:** 22/22 trades match Python.

**Topstep $1000/day stop:** Net $4,698 (3 stop-outs). Avg hold 18 min — good for event management. Recommended as Topstep workhorse.
