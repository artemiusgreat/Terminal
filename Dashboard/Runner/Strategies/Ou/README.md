# OU Mean Reversion

Ornstein-Uhlenbeck mean-reversion on ES. The committed baseline strategy.

## Rules

1. Maintain 60-minute rolling window of tick mids
2. Compute mean and standard deviation
3. **Long** when mid < mean - 1.5σ (night session only: 18:00–09:30 ET)
4. **Short** when mid > mean + 1.5σ
5. **Exit long:** stop-loss at -20 ticks or take-profit at +10 ticks
6. **Exit short:** stop-loss at +20 ticks or take-profit at -10 ticks
7. Force flat during 16:00–18:00 ET clearing halt

1 lot fixed. Commission $1/side.

## Files

- `OuStrategy.cs` — strategy logic
- `OrnsteinUhlenbeck.cs` — OU process toolkit (fit, hitting time, Kelly — reusable)

## Parameters

| Param | Value |
|---|---|
| Window | 60 min |
| Entry | 1.5σ |
| Stop-loss | 20 ticks |
| Take-profit | 10 ticks |

## Notes

Entry was 2.0σ until 2026-10-18, when a Python shootout showed 1.5σ outperformed (+$1,160 vs +$768). Changed per user direction, pushed as c8f6a6f.
