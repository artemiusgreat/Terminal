# Carver 28 - Cross Instrument Spread (ES/NQ)

EWMAC16 trend rule on a synthetic ES/NQ spread price.
Source: Robert Carver, *Advanced Futures Trading Strategies*, strategy 28.
The book tests this method on US bond futures spreads (plus a carry rule
on the spread, which needs futures-curve data and is not run here); this
is the same method applied to the ES/NQ spread - a method test, not the
book's instruments.

## Rules

1. Hedge ratio R* = std(daily NQ price changes) / std(daily ES price
   changes), rounded to the nearest 0.5 and FIXED: R* = 8.5 (computed in
   the Python reference from the window's daily closes).
2. Spread price pD = 8.5 x ES_close - NQ_close on synchronised hourly
   bars (evaluated on ES bar closes using the latest NQ close).
3. vol_sp = EWMA (span 35) standard deviation of hourly changes in pD.
4. Forecast = clip((EWMA16 - EWMA64 of pD) / vol_sp x 4.10, -20, +20).
5. Units = round(forecast / 10) in -2..+2, NO buffer. One unit (pack) =
   long 17 ES + short 5 NQ, scaled linearly; negative units invert both
   legs. PnL per pack = $100 per point of pD. Both legs flat at the last
   completable bar.

This is a trend rule ON the spread, not spread fading: it rides
persistent relative moves between ES and NQ rather than betting on
convergence.

## Files

- `CarverSpreadStrategy.cs` - strategy logic
- `../CarverBatch2/CarverCommon.cs` - shared helpers (SymbolFeed,
  EwmacEngine, CarverCommon.RebalanceAsync)

Host config to reproduce: enable NQ in Program.cs Instruments and set
the DI registration to `CarverSpreadStrategy`; `BarMinutes = 60`,
Topstep guard off.

## Parameters

| Param | Value |
|---|---|
| Hedge ratio R* | 8.5 (fixed) |
| Spread price | 8.5 x ES - NQ (hourly closes) |
| Rule | EWMAC (16, 64) on the spread price |
| Forecast scalar | 4.10 |
| Forecast cap | +/-20 |
| Sizing | round(forecast / 10) packs; 1 pack = +17 ES / -5 NQ |
| Buffering | None |
| Commission | $1 per contract per order |

## Metrics

ES + NQ, full window 2026-06-30 20:00 -> 2026-07-17 16:59 ET (293
synchronised hourly bars), Topstep OFF. Two engines, identical logic:

C# Terminal (fills on ticks after the bar close, bid/ask):

| Metric | Value |
|---|---|
| Net | +$97,123 |
| Closed | 8 packs (176 leg-lots) |
| Profit Factor | 1.27 (leg-lot basis) |
| Sharpe | +3.99 (daily PnL, realized basis) |
| Commissions | $352 |

Python reference (fills at the signal bar close, mid):

| Metric | Value |
|---|---|
| Net | +$99,348 |
| Closed packs | 8 |
| Win % | 50.0 |
| Profit Factor | 3.86 (pack basis) |
| Avg win / loss | +$33,634 / -$8,709 |
| Max drawdown | $75,160 (hourly mark-to-market) |
| Sharpe | +3.43 (daily PnL) |

Scale warning: one pack is ~$9M gross notional (17 ES + 5 NQ), far
larger than the other Carver ports - compare the profit factor, not the
dollar net. The window's relative move was large (NQ -5.8% vs ES -0.7%),
which is exactly what a trend rule on the spread monetises; 8 packs is
a small sample. For contrast: fading this spread lost money in our
earlier ES/NQ divergence study, and the book's own spread results on
bond futures are ~0.
