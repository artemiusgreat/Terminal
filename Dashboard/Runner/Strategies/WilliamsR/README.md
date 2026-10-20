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

**C# Terminal backtest — 3 days, Topstep rules** (ES trade ticks via MemoryMapReader, Jul 1–3 2026 ET; pipeline-enforced $1000/day stop and no trading 16:00–18:00 ET; commission $1 per closed trade in C#, $1/side in Python):

| Metric | C# | Python (same window) |
|---|---|---|
| Net | $-1,048 | $-1,134 |
| Trades | 111 | 111 |
| Win % | 60.4% | 61.3% |
| Profit Factor | 0.90 | 0.89 |
| Sharpe | -3.10 | -3.27 |
| Max Drawdown | $2,980 | $3,011 |
| $/trade | $-9.45 | $-10.22 |
| Avg Win / Avg Loss | $135 / $-229 | $135 / $-240 |
| Max Consec Win / Loss | 9 / 4 | 9 / 3 |
| Recovery | -0.35 | -0.38 |

C# and Python agree closely on this window (trade counts within 1, PF within 0.1); residual differences are fill timing and the commission convention.
