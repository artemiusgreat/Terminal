# Supertrend

Classic Supertrend trend-following (inventor's settings: 10-period, 3.0 multiplier).

## Rules

1. Compute Wilder's ATR(10)
2. Basic bands: `upper = (H+L)/2 + 3.0 × ATR`, `lower = (H+L)/2 - 3.0 × ATR`
3. Final bands with ratchet: upper can only move down, lower can only move up
4. Direction: +1 if close > prev upper band, -1 if close < prev lower band
5. **Long** when direction flips -1 → +1
6. **Exit long** when direction flips +1 → -1

Long-only. 1 lot. No stop-loss/take-profit — the Supertrend line is the trailing exit.

## Files

- `SupertrendStrategy.cs` — strategy logic
- `SupertrendIndicator.cs` — indicator (exact port of Python `supertrend_nb`)

## Parameters

| Param | Value |
|---|---|
| ATR period | 10 (Wilder) |
| Multiplier | 3.0 |

## Metrics

**8-day Python backtest** (ES 1-min, 2026-06-30 → 2026-07-08, $1/side):

| Metric | Value |
|---|---|
| Net | +$2,440 |
| Trades | 99 |
| Win % | 37.4% |
| Profit Factor | 1.20 |
| Sharpe | 3.81 |
| Max Drawdown | $2,456 |
| $/trade | $24.64 |
| Avg Win / Avg Loss | $397 / -$198 |
| Max Consec Win / Loss | 4 / 7 |
| Recovery | 0.99 |

**600-bar C# validation:** 6/6 trades match Python.

**Topstep $1000/day stop:** Net drops to $430 (-$2,010). Trailing exits need room the daily stop doesn't give. Not recommended for Topstep.
