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
