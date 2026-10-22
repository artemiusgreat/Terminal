namespace Runner.Strategies.CarverFastMr
{
  /// <summary>
  /// Carver strategy 26: Fast mean reversion. Forecast = 9.3 * risk-adjusted
  /// deviation of the hourly price from the EWMA5 daily equilibrium.
  /// </summary>
  public sealed class CarverFastMrStrategy : CarverMrBase
  {
    public CarverFastMrStrategy(StrategyContext ctx) : base(ctx) { }

    protected override double ForecastScalar => 9.3;
    protected override bool UseTrendGate => false;
  }
}
