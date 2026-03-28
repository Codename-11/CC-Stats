namespace CCStats.Core.Models;

public sealed record CreditLimits(int FiveHourCredits, int SevenDayCredits, double? MonthlyPrice = null)
{
    public double NormalizationFactor => FiveHourCredits <= 0 ? 0 : (double)SevenDayCredits / FiveHourCredits;
}
