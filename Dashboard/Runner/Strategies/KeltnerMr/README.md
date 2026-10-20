# Keltner Mean Reversion

Keltner Channel fade: buy when price breaks below the lower channel, exit at the middle.

## Rules

1. Compute EMA(30) of closes (middle line)
2. Compute Wilder's ATR(30)
3. Bands: `upper = EMA + 1.3 × ATR`, `lower = EMA - 1.3 × ATR`
4. **Long** when close crosses BELOW lower band
5. **Exit long** when close crosses ABOVE the EMA middle line

Long-only. 1 lot.

## Files

- `KeltnerMrStrategy.cs` — strategy logic
- `KeltnerChannel.cs` — indicator (uses shared `Ema` and `Atr` from `Runner.Indicators`)

## Parameters

| Param | Value |
|---|---|
| EMA period | 30 |
| ATR period | 30 (Wilder) |
| ATR multiplier | 1.3 |

## Metrics

**8-day Python backtest** (ES 1-min, 2026-06-30 → 2026-07-08, $1/side):

| Metric | Value |
|---|---|
| Net | +$3,047 |
| Trades | 139 |
| Win % | 61.9% |
| Profit Factor | 1.23 |
| Sharpe | 3.46 |
| Max Drawdown | $2,453 |
| $/trade | $21.92 |
| Avg Win / Avg Loss | $191 / -$252 |
| Max Consec Win / Loss | 6 / 6 |
| Recovery | 1.24 |

**600-bar C# validation:** 11/11 trades match Python.

**Topstep $1000/day stop:** Net drops to $2,346, stopped 4 of 8 days (50%). Too many stop-outs for Topstep.
