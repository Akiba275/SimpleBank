using System;
using System.Collections.Generic;
using System.Linq;

namespace SimpleBank.Core
{
    public sealed class BankConfig
    {
        public bool EnableAtm { get; set; } = true;
        public bool DebugLogging { get; set; }
        public decimal ActivityDailyRatePercent { get; set; } = 0.005m;
        public decimal BaseCredit { get; set; } = 5000m;
        public decimal EarningsRatio { get; set; } = 0.10m;
        public decimal AbsoluteCreditCap { get; set; } = 1000000m;
        public decimal OverdueRateMultiplier { get; set; } = 1.5m;
        public bool EnableOverdueCollection { get; set; } = true;
        public int OverdueGraceDays { get; set; } = 7;
        public bool EnableActiveBalanceSetoff { get; set; } = true;
        public long MinimumActiveBalanceReserve { get; set; }
        public long MinimumWalletReserve { get; set; } = 1000;
        public decimal IncomeGarnishmentPercent { get; set; } = 25m;
        public decimal MaximumOverdueInterestPercent { get; set; } = 100m;
        public int DefaultAfterDays { get; set; } = 28;
        public List<TermProductConfig> TermProducts { get; set; } = Defaults.TermProducts();
        public List<LoanProductConfig> LoanProducts { get; set; } = Defaults.LoanProducts();

        public BankConfig Clone()
        {
            return new BankConfig
            {
                EnableAtm = EnableAtm,
                DebugLogging = DebugLogging,
                ActivityDailyRatePercent = ActivityDailyRatePercent,
                BaseCredit = BaseCredit,
                EarningsRatio = EarningsRatio,
                AbsoluteCreditCap = AbsoluteCreditCap,
                OverdueRateMultiplier = OverdueRateMultiplier,
                EnableOverdueCollection = EnableOverdueCollection,
                OverdueGraceDays = OverdueGraceDays,
                EnableActiveBalanceSetoff = EnableActiveBalanceSetoff,
                MinimumActiveBalanceReserve = MinimumActiveBalanceReserve,
                MinimumWalletReserve = MinimumWalletReserve,
                IncomeGarnishmentPercent = IncomeGarnishmentPercent,
                MaximumOverdueInterestPercent = MaximumOverdueInterestPercent,
                DefaultAfterDays = DefaultAfterDays,
                TermProducts = TermProducts == null ? null : TermProducts.Select(x => x == null ? null : x.Clone()).ToList(),
                LoanProducts = LoanProducts == null ? null : LoanProducts.Select(x => x == null ? null : x.Clone()).ToList()
            };
        }

        public static decimal FloorRateToFourDecimalPlaces(decimal value)
        {
            return Math.Floor(value * 10000m) / 10000m;
        }

        public ConfigValidationResult ValidateAndNormalize()
        {
            var errors = new List<string>();
            if (BaseCredit < 0) errors.Add("Base credit cannot be negative.");
            if (BaseCredit > long.MaxValue) errors.Add("Base credit is too large.");
            if (AbsoluteCreditCap < 0) errors.Add("Absolute credit cap cannot be negative.");
            if (AbsoluteCreditCap > long.MaxValue) errors.Add("Absolute credit cap is too large.");
            if (OverdueRateMultiplier < 1 || OverdueRateMultiplier > BankLimits.MaximumOverdueRateMultiplier) errors.Add("Overdue multiplier must be 1..10.");
            if (OverdueGraceDays < 0 || OverdueGraceDays > BankLimits.MaximumCollectionDays) errors.Add("Overdue grace days must be 0..1120.");
            if (DefaultAfterDays < 1 || DefaultAfterDays > BankLimits.MaximumCollectionDays) errors.Add("Default-after days must be 1..1120.");
            if (MinimumActiveBalanceReserve < 0 || MinimumActiveBalanceReserve > int.MaxValue) errors.Add("Active-balance reserve must fit the game wallet range.");
            if (MinimumWalletReserve < 0 || MinimumWalletReserve > int.MaxValue) errors.Add("Wallet reserve must fit the game wallet range.");
            if (IncomeGarnishmentPercent < 0 || IncomeGarnishmentPercent > BankLimits.MaximumCollectionPercent) errors.Add("Income garnishment percent must be 0..100.");
            if (MaximumOverdueInterestPercent < 0 || MaximumOverdueInterestPercent > BankLimits.MaximumOverdueInterestCapPercent) errors.Add("Maximum overdue interest percent must be 0..1000.");
            if (ActivityDailyRatePercent < 0 || ActivityDailyRatePercent > BankLimits.MaximumDepositDailyRatePercent) errors.Add("Activity rate must be 0..1 percent per day.");
            if (EarningsRatio < 0 || EarningsRatio > 1) errors.Add("Earnings ratio must be 0..1 (0%..100%).");
            ValidateProducts(errors, TermProducts, "term", BankLimits.MaximumDepositDailyRatePercent);
            ValidateProducts(errors, LoanProducts, "loan", BankLimits.MaximumLoanDailyRatePercent);
            if (AbsoluteCreditCap < Math.Floor(BaseCredit / 1000m) * 1000m)
                errors.Add("Absolute credit cap must fit the base credit after 1,000-coin flooring.");
            return new ConfigValidationResult(errors);
        }

        private static void ValidateProducts<T>(List<string> errors, List<T> products, string kind, decimal maximumDailyRate) where T : ProductConfig
        {
            if (products == null || products.Count < 10) errors.Add(kind + " product list must contain 10 slots.");
            if (products == null) return;
            var slots = products.Take(10).ToList();
            if (slots.Any(x => x == null)) errors.Add(kind + " product slots cannot be null.");
            if (slots.Any(x => x == null || x.Days < 1 || x.Days > 1120 || x.DailyRatePercent < 0 || x.DailyRatePercent > maximumDailyRate))
                errors.Add(kind + " products have invalid duration or rate.");
            if (slots.Where(x => x != null && x.Enabled).GroupBy(x => x.Days).Any(g => g.Count() > 1))
                errors.Add(kind + " product durations cannot repeat among enabled slots.");
        }
    }

    public sealed class ConfigValidationResult
    {
        public IReadOnlyList<string> Errors { get; }
        public bool IsValid { get { return Errors.Count == 0; } }
        public ConfigValidationResult(IReadOnlyList<string> errors) { Errors = errors; }
    }

    public abstract class ProductConfig
    {
        public bool Enabled { get; set; }
        public int Days { get; set; }
        public decimal DailyRatePercent { get; set; }
    }

    public sealed class TermProductConfig : ProductConfig
    {
        public TermProductConfig Clone() { return new TermProductConfig { Enabled = Enabled, Days = Days, DailyRatePercent = DailyRatePercent }; }
    }

    public sealed class LoanProductConfig : ProductConfig
    {
        public LoanProductConfig Clone() { return new LoanProductConfig { Enabled = Enabled, Days = Days, DailyRatePercent = DailyRatePercent }; }
    }

    public static class Defaults
    {
        public static List<TermProductConfig> TermProducts()
        {
            return new List<TermProductConfig>
            {
                new TermProductConfig { Enabled = true, Days = 7, DailyRatePercent = 0.01m },
                new TermProductConfig { Enabled = true, Days = 14, DailyRatePercent = 0.015m },
                new TermProductConfig { Enabled = true, Days = 28, DailyRatePercent = 0.02m },
                new TermProductConfig { Enabled = true, Days = 56, DailyRatePercent = 0.025m },
                new TermProductConfig { Enabled = true, Days = 112, DailyRatePercent = 0.03m },
                new TermProductConfig { Days = 1 }, new TermProductConfig { Days = 1 }, new TermProductConfig { Days = 1 }, new TermProductConfig { Days = 1 }, new TermProductConfig { Days = 1 }
            };
        }

        public static List<LoanProductConfig> LoanProducts()
        {
            return new List<LoanProductConfig>
            {
                new LoanProductConfig { Enabled = true, Days = 7, DailyRatePercent = 0.15m },
                new LoanProductConfig { Enabled = true, Days = 28, DailyRatePercent = 0.10m },
                new LoanProductConfig { Enabled = true, Days = 112, DailyRatePercent = 0.06m },
                new LoanProductConfig { Days = 1 }, new LoanProductConfig { Days = 1 }, new LoanProductConfig { Days = 1 }, new LoanProductConfig { Days = 1 }, new LoanProductConfig { Days = 1 }, new LoanProductConfig { Days = 1 }, new LoanProductConfig { Days = 1 }
            };
        }
    }
}
