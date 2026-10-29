namespace SimpleBank.Core
{
    public static class BankLimits
    {
        public const int MaximumTermDepositsPerAccount = 100;
        public const int MaximumPersonalAccounts = 64;
        public const decimal MaximumDepositDailyRatePercent = 1m;
        public const decimal MaximumLoanDailyRatePercent = 10m;
        public const decimal MaximumOverdueRateMultiplier = 10m;
        public const decimal MaximumCollectionPercent = 100m;
        public const decimal MaximumOverdueInterestCapPercent = 1000m;
        public const int MaximumCollectionDays = 1120;
    }
}
