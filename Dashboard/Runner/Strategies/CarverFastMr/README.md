# Carver 26 - Fast Mean Reversion

Contrarian hourly mean reversion to a 5-day EWMA equilibrium.
Source: Robert Carver, *Advanced Futures Trading Strategies*, strategy 26.

## Rules

1. Daily closes are built from hourly bars (ET dates). Equilibrium = EWMA
   (span 5 days) of daily closes, using closes through the previous day.
2. Volatility: strategy-3 blended daily vol = 0.7 x sqrt(EWMA32 of squared
   daily returns) + 0.3 x expanding RMS of daily returns (undefined for
   day index < 2). Sigma is computed daily and held through the next day.
3. Every hourly bar close: raw = equilibrium - price;
   risk-adjusted = raw / (daily vol fraction x price).
4. Forecast = clip(9.3 x risk-adjusted, -20, +20).
5. Target position = round(forecast / 10) contracts (-2..+2). Rebalance to
   the target every hour; no buffering (the book is explicit: buffering
   decimates mean reversion). Flat at the last completable bar.

Lab deviations from the book: orders fill at market on ticks after the bar
close, NOT via the book's resting one-contract limit orders that earn the
spread - the book's edge is engineered around that execution, so this run
measures how much of the signal survives market fills. Position anchor is
round(forecast / 10) lots instead of the book's capital-based vol-target
sizing. ES only.

## Files

- `CarverMrBase.cs` - shared base: daily series + blended vol built from
  hourly bars, hourly target evaluation, end-of-stream flatten
- `CarverFastMrStrategy.cs` - strategy 26 (scalar 9.3, no trend gate)

Host config to reproduce: `BarMinutes = 60`, ES only in Instruments,
Topstep guard off.

## Parameters

| Param | Value |
|---|---|
| Equilibrium | EWMA of daily closes, span 5 days |
| Evaluation | Hourly bar close |
| Forecast scalar | 9.3 |
| Forecast cap | +/-20 |
| Sizing | round(forecast / 10) contracts |
| Buffering | None |
| Commission | $1 per contract per order |

## Metrics

ES, full window 2026-06-30 20:00 -> 2026-07-17 16:59 ET (293 hourly
bars), Topstep OFF. Two engines, identical logic:

C# Terminal (fills on ticks after the bar close, bid/ask):

| Metric | Value |
|---|---|
| Net | +$3,715 |
| Closed lots | 30 (28 close events) |
| Win % | 67.9 (close-event basis) |
| Profit Factor | 1.55 |
| Avg win / loss | +$550 / -$748 |
| Max drawdown | $4,910 (realized reconstruction) |
| Commissions | $60 |

Python reference (fills at the signal bar close, mid):

| Metric | Value |
|---|---|
| Net | +$3,840 |
| Closed lots | 30 |
| Win % | 60.0 |
| Profit Factor | 1.48 |
| Avg win / loss | +$664 / -$671 |
| Max drawdown | $5,653 (hourly mark-to-market) |

Book reference (Jumbo portfolio, hourly since 2013, limit-order
execution): Sharpe 0.80 aggregate, median single instrument 0.41.
