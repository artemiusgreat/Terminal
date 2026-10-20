# Lead-Lag Follower

NQ leads ES by ~0.5-1.0s (Hayashi-Yoshida). Trade ES in NQ's direction before ES reacts.

## Rules

1. Monitor NQ impulse over 500ms window: must move ≥ 6 ticks
2. ES must move < 2 ticks over same window (hasn't reacted yet)
3. **Long ES** if NQ impulse positive, **Short ES** if negative
4. **Exit:** +3 ticks TP, -3 ticks SL, or 2s timeout

## Status

**CONCLUDED — failed.** 3-hour test: net -$6,988, 409 trades, 3.2% win rate. The 6-tick NQ threshold captures noise, not signal. Do not extend.

Kept for reference only.

**C# Terminal backtest — 3 days, Topstep rules** (ES trade ticks via MemoryMapReader, Jul 1–3 2026 ET; pipeline-enforced $1000/day stop and no trading 16:00–18:00 ET; commission $1 per closed trade in C#, $1/side in Python):

| Metric | C# |
|---|---|
| Net | $-4,057 |
| Trades | 257 |
| Win % | 5.4% |
| Profit Factor | 0.05 |
| Sharpe | -177.30 |
| Max Drawdown | $4,057 |
| $/trade | $-15.79 |
| Avg Win / Avg Loss | $15 / $-18 |
| Max Consec Win / Loss | 1 / 42 |
| Recovery | -1.00 |

Confirms the concluded-failed status under Topstep rules: the $1000 day-stop capped each day, final net -$4,057. No Python version exists (C#-only strategy).
