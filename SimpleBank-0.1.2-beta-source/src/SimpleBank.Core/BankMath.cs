using System;

namespace SimpleBank.Core
{
    public static class BankMath
    {
        public static decimal PercentToRate(decimal percent) { return percent / 100m; }
        public static long RoundCoins(decimal value)
        {
            if (value < 0) throw new ArgumentOutOfRangeException("value");
            if (value > long.MaxValue) throw new OverflowException("Coin amount overflow.");
            return checked((long)decimal.Round(value, 0, MidpointRounding.AwayFromZero));
        }
        public static long DailyInterest(long baseAmount, decimal dailyPercent)
        {
            if (baseAmount < 0 || dailyPercent < 0) throw new ArgumentOutOfRangeException();
            return RoundCoins(checked(baseAmount * PercentToRate(dailyPercent)));
        }
        public static long AccrueDailyInterest(long baseAmount, decimal dailyPercent, decimal previousRemainder, out decimal nextRemainder)
        {
            if (baseAmount < 0 || dailyPercent < 0) throw new ArgumentOutOfRangeException();
            if (!IsValidInterestRemainder(previousRemainder)) throw new ArgumentOutOfRangeException("previousRemainder");
            var exactInterest = checked(baseAmount * PercentToRate(dailyPercent) + previousRemainder);
            return ExtractWholeCoins(exactInterest, out nextRemainder);
        }
        public static long ExtractWholeCoins(decimal amount, out decimal remainder)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException("amount");
            var wholeCoins = decimal.Floor(amount);
            if (wholeCoins > long.MaxValue) throw new OverflowException("Coin amount overflow.");
            remainder = amount - wholeCoins;
            return checked((long)wholeCoins);
        }
        public static long RoundInterestRemainder(decimal remainder)
        {
            if (!IsValidInterestRemainder(remainder)) throw new ArgumentOutOfRangeException("remainder");
            return RoundCoins(remainder);
        }
        public static bool IsValidInterestRemainder(decimal remainder)
        {
            return remainder >= 0m && remainder < 1m;
        }
        public static long Compound(long principal, decimal dailyPercent, int days)
        {
            if (principal < 0 || days < 0 || dailyPercent < 0) throw new ArgumentOutOfRangeException();
            long amount = principal;
            decimal remainder = 0m;
            for (var i = 0; i < days; i++)
            {
                decimal nextRemainder;
                var interest = AccrueDailyInterest(amount, dailyPercent, remainder, out nextRemainder);
                amount = checked(amount + interest);
                remainder = nextRemainder;
            }
            return checked(amount + RoundInterestRemainder(remainder));
        }
        public static long RoundedWithdrawalFraction(long balance, int denominator)
        {
            if (balance < 0) throw new ArgumentOutOfRangeException("balance");
            if (denominator <= 0) throw new ArgumentOutOfRangeException("denominator");
            if (balance == 0) return 0;
            var rounded = RoundCoins((decimal)balance / denominator);
            return Math.Max(1, Math.Min(balance, rounded));
        }
        public static long CreditLimit(BankConfig config, long historicalEarnings)
        {
            if (config == null) throw new ArgumentNullException("config");
            if (historicalEarnings < 0) historicalEarnings = 0;
            var raw = checked(config.BaseCredit + historicalEarnings * config.EarningsRatio);
            var floored = checked(Math.Floor(Math.Max(0m, raw) / 1000m) * 1000m);
            var cap = checked(Math.Floor(config.AbsoluteCreditCap / 1000m) * 1000m);
            return checked((long)Math.Min(cap, floored));
        }
    }
}
