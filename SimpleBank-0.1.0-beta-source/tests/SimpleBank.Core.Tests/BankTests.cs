using System;
using System.Collections.Generic;
using SimpleBank.Core;

public static partial class BankTests
{
    private static int passed;
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        passed++;
    }

    public static int Main()
    {
        TestCreditRounding();
        TestDefaultRates();
        TestWalletProtectionAndIdempotency();
        TestDailySettlementAndCatchup();
        TestFractionalInterestRemainders();
        TestTermEarlyWithdrawal();
        TestActiveTermFunding();
        TestActiveTermFailuresAndIsolation();
        TestActiveTermSettlementAndCollection();
        TestLoanInterestAndRepayment();
        TestCollectionDatesAndGradualInterestCap();
        TestScheduledCollectionOrderReservesAndIdempotency();
        TestNaturalTermMaturityThenActiveSetoff();
        TestIncomeGarnishmentRemainder();
        TestOfflineSetoffAndReconnectSweep();
        TestDebtCancellationAndCreditRestrictionAreIndependent();
        TestCreditRestrictionWalletModeMigration();
        TestSchemaThreeMigrationLocksCollectionTerms();
        TestWalletRollbackFailureIsReported();
        TestWalletRolesAndAccountResolution();
        TestPersonalAccountIsolation();
        TestModeSwitchBlocksOpenProductsAndMergesOnlyActiveBalance();
        TestMigrationAndSettlementBalanceAccounting();
        TestMigrationOverflowSafety();
        TestSameModeMismatchReconciliationAndPersonalPersistence();
        TestSnapshotRevisionAndSharedAccountContract();
        TestConfigValidationAndHotReload();
        TestContractIdsAfterReload();
        TestMalformedStateSafety();
        TestInterestRemainderSchemaMigration();
        TestInvalidAndOverflowInputs();
        TestSecurityAndGrowthLimits();
        TestBusinessAmountGranularity();
        TestWithdrawalShortcutRounding();
        TestLargeTransactionsAndProductEnablement();
        TestDiagnosticsGate();
        Console.WriteLine("PASS: " + passed + " assertions");
        return 0;
    }

    private static BankService NewService(WalletMode mode = WalletMode.Shared)
    {
        var config = new BankConfig();
        Assert(config.EnableAtm && !config.DebugLogging && !new BankConfig { EnableAtm = false, DebugLogging = true }.Clone().EnableAtm && new BankConfig { DebugLogging = true }.Clone().DebugLogging, "ATM and debug settings use safe defaults and survive config cloning");
        var state = new BankState { WalletMode = mode };
        return new BankService(config, state);
    }

    private static void TestCreditRounding()
    {
        var c = new BankConfig { BaseCredit = 5000m, EarningsRatio = 0.10m, AbsoluteCreditCap = 1000000m };
        Assert(BankMath.CreditLimit(c, 0) == 5000, "base credit");
        Assert(BankMath.CreditLimit(c, 4999) == 5000, "below thousand floors");
        Assert(BankMath.CreditLimit(c, 5000) == 5000, "exact threshold still floors");
        Assert(BankMath.CreditLimit(c, 50000) == 10000, "crossing thousand");
        Assert(BankMath.CreditLimit(c, 99999999) == 1000000, "absolute cap");
        Assert(BankMath.CreditLimit(c, long.MaxValue) == 1000000, "large earnings cap");
        c.EarningsRatio = 1m;
        Assert(BankMath.CreditLimit(c, long.MaxValue) == 1000000, "raw limit is capped before conversion overflow");
    }

    private static void TestDefaultRates()
    {
        var config = new BankConfig();
        Assert(config.ActivityDailyRatePercent == 0.005m, "active savings uses the balanced fractional-interest default rate");
        Assert(config.TermProducts[0].DailyRatePercent == 0.01m && config.TermProducts[1].DailyRatePercent == 0.015m && config.TermProducts[2].DailyRatePercent == 0.02m && config.TermProducts[3].DailyRatePercent == 0.025m && config.TermProducts[4].DailyRatePercent == 0.03m, "term defaults rise from 0.01% to 0.03% per day");
        Assert(config.LoanProducts[0].DailyRatePercent == 0.15m && config.LoanProducts[1].DailyRatePercent == 0.10m && config.LoanProducts[2].DailyRatePercent == 0.06m && config.OverdueRateMultiplier == 1.5m, "loan defaults decline by term and retain the 1.5 overdue multiplier");
        Assert(config.EnableOverdueCollection && config.OverdueGraceDays == 7 && config.EnableActiveBalanceSetoff
            && config.MinimumActiveBalanceReserve == 0 && config.MinimumWalletReserve == 1000
            && config.IncomeGarnishmentPercent == 25m && config.MaximumOverdueInterestPercent == 100m && config.DefaultAfterDays == 28,
            "all compulsory-collection defaults match the release config contract");
    }

    private static void TestWalletProtectionAndIdempotency()
    {
        var service = NewService(); var wallet = new TestWallet(10000, 50000);
        var r = service.Deposit(1, wallet, 5000, "d1", 0);
        Assert(r.Status == TransactionStatus.Applied && wallet.Balance == 5000 && service.State.SharedAccount.ActiveBalance == 5000, "deposit changes both sides");
        var duplicate = service.Deposit(1, wallet, 5000, "d1", 1);
        Assert(duplicate.Status == TransactionStatus.Duplicate && wallet.Balance == 5000, "duplicate is harmless");
        var stale = service.Withdraw(1, wallet, 1000, "w-stale", 0);
        Assert(stale.Status == TransactionStatus.StaleRevision && wallet.Balance == 5000, "stale revision is rejected");
        var bad = service.Withdraw(1, wallet, 999999, "w-bad", 1);
        Assert(bad.Status == TransactionStatus.Rejected && wallet.Balance == 5000 && service.State.SharedAccount.ActiveBalance == 5000, "failed transaction rolls back");
        var badDeposit = service.Deposit(1, wallet, 100000, "d-bad", 1);
        Assert(badDeposit.Status == TransactionStatus.Rejected && wallet.Balance == 5000 && service.State.SharedAccount.ActiveBalance == 5000, "failed wallet removal rolls back");
        var ordinary = wallet.Balance; wallet.AddOrdinaryIncome(1000);
        Assert(wallet.Balance == ordinary + 1000 && wallet.HistoricalTotalEarnings == 51000, "ordinary income still works");
    }

    private static void TestDailySettlementAndCatchup()
    {
        var service = NewService(); var wallet = new TestWallet(0, 0);
        Assert(service.Deposit(1, wallet, 10000, "d", 0).Status == TransactionStatus.Rejected, "cannot deposit without wallet funds");
        wallet.AddOrdinaryIncome(10000); Assert(service.Deposit(1, wallet, 10000, "d2", 0).Status == TransactionStatus.Applied, "fund and deposit");
        var first = service.SettleToDay(1, "day-1", 1); Assert(first.Status == TransactionStatus.Applied, "first settlement");
        var amount = service.State.SharedAccount.ActiveBalance;
        var duplicate = service.SettleToDay(1, "day-1-repeat", 2); Assert(duplicate.Status == TransactionStatus.Applied, "same day with new request is no-op");
        Assert(service.State.SharedAccount.ActiveBalance == amount, "same day does not compound twice");
        Assert(service.SettleToDay(3, "day-3", 3).Status == TransactionStatus.Applied, "catch up");
        Assert(service.State.SharedAccount.ActiveBalance > amount, "catch-up compounds missing days");
    }

    private static void TestTermEarlyWithdrawal()
    {
        var service = NewService(); var wallet = new TestWallet(20000, 0);
        Assert(service.SettleToDay(10, "baseline", 0).Status == TransactionStatus.Applied, "term baseline settlement");
        Assert(service.CreateTermDeposit(1, wallet, 0, 5000, 10, "td", 1).Status == TransactionStatus.Applied, "term opens from wallet");
        Assert(wallet.Balance == 15000, "term principal is removed from wallet");
        Assert(service.State.SharedAccount.TermDeposits.Count == 1, "term stored");
        var termId = service.State.SharedAccount.TermDeposits[0].Id;
        Assert(service.SettleToDay(12, "s12", 2).Status == TransactionStatus.Applied, "term days settle");
        Assert(service.State.SharedAccount.TermDeposits[0].DaysSettled == 2, "term catches up each complete day");
        var before = service.State.SharedAccount.ActiveBalance;
        Assert(service.WithdrawTermEarly(1, wallet, termId, 12, "early", 3).Status == TransactionStatus.Applied, "early close");
        Assert(service.State.SharedAccount.ActiveBalance > before && service.State.SharedAccount.TermDeposits.Count == 0, "early returns principal plus activity interest");
    }

    private static void TestFractionalInterestRemainders()
    {
        var activeState = new BankState
        {
            WalletMode = WalletMode.Shared,
            LastSettlementDay = 0,
            SharedAccount = new Account { Id = "shared", ActiveBalance = 1000 }
        };
        var active = new BankService(new BankConfig(), activeState);
        Assert(active.SettleToDay(19, "fractional-active-19", 0).Status == TransactionStatus.Applied, "fractional active interest settles without daily rounding");
        Assert(active.State.SharedAccount.ActiveBalance == 1000 && active.State.SharedAccount.ActiveInterestRemainder == 0.95m, "sub-gold active interest remains in the persisted remainder bucket");
        Assert(active.State.Clone().SharedAccount.ActiveInterestRemainder == 0.95m, "active-interest remainder survives state cloning");
        Assert(active.SettleToDay(20, "fractional-active-20", 1).Status == TransactionStatus.Applied, "fractional active interest crosses one gold");
        Assert(active.State.SharedAccount.ActiveBalance == 1001 && active.State.SharedAccount.ActiveInterestRemainder == 0m, "ten 0.1g accruals post exactly one gold");

        var termState = new BankState { WalletMode = WalletMode.Shared, LastSettlementDay = 0 };
        var term = new BankService(new BankConfig(), termState);
        var termWallet = new TestWallet(1000, 0);
        Assert(term.CreateTermDeposit(1, termWallet, 0, 1000, 0, "fractional-term", 0).Status == TransactionStatus.Applied, "small term deposit opens");
        Assert(term.SettleToDay(7, "fractional-term-maturity", 1).Status == TransactionStatus.Applied, "small term deposit reaches maturity");
        Assert(term.State.SharedAccount.TermDeposits.Count == 0 && term.State.SharedAccount.ActiveBalance == 1001, "term interest carries fractions across days and rounds only the final remainder");

        var loanState = new BankState { WalletMode = WalletMode.Shared, LastSettlementDay = 0 };
        var loan = new BankService(new BankConfig(), loanState);
        var loanWallet = new TestWallet(100, 0);
        Assert(loan.RequestLoan(1, loanWallet, 0, 1000, 0, "fractional-loan", 0).Status == TransactionStatus.Applied, "small loan opens");
        Assert(loan.SettleToDay(1, "fractional-loan-day", 1).Status == TransactionStatus.Applied, "small loan accrues one day");
        Assert(loan.State.SharedAccount.Loan.AccruedInterest == 1 && loan.State.SharedAccount.Loan.InterestRemainder == 0.5m, "loan retains the half-gold remainder instead of rounding it every day");
        Assert(loan.Snapshot(1, loanWallet).LoanInterest == 2 && loan.State.SharedAccount.Loan.TotalOwed == 1002, "loan UI debt includes the final rounded remainder");
        Assert(loan.RepayLoan(1, loanWallet, 1002, "fractional-loan-payoff", 2).Status == TransactionStatus.Applied, "full payoff settles the rounded loan remainder");
        Assert(loan.State.SharedAccount.Loan.Status == LoanStatus.Settled && loan.State.SharedAccount.Loan.InterestRemainder == 0m && loanWallet.Balance == 98, "full payoff clears principal, interest, and remainder exactly once");

        var mergeState = new BankState { WalletMode = WalletMode.Personal, ModeMismatch = true };
        mergeState.PersonalAccounts[11] = new Account { Id = "player-11", ActiveBalance = 100, ActiveInterestRemainder = 0.6m };
        mergeState.PersonalAccounts[22] = new Account { Id = "player-22", ActiveBalance = 200, ActiveInterestRemainder = 0.7m };
        var merge = new BankService(new BankConfig(), mergeState);
        Assert(merge.MigrateWalletMode(WalletMode.Shared, 11, 0).Status == TransactionStatus.Applied, "wallet migration carries fractional active interest");
        Assert(merge.State.SharedAccount.ActiveBalance == 301 && merge.State.SharedAccount.ActiveInterestRemainder == 0.3m, "merged remainders conserve their whole-gold carry and fractional residue");
    }

    private static void TestLoanInterestAndRepayment()
    {
        var service = NewService(); var wallet = new TestWallet(10000, 50000);
        Assert(service.SettleToDay(0, "baseline", 0).Status == TransactionStatus.Applied, "loan baseline settlement");
        Assert(service.RequestLoan(1, wallet, 0, 5000, 0, "loan", 1).Status == TransactionStatus.Applied, "loan opens");
        var balance = wallet.Balance;
        Assert(service.SettleToDay(7, "loan-days", 2).Status == TransactionStatus.Applied, "loan settles");
        var loan = service.State.SharedAccount.Loan;
        Assert(loan.AccruedInterest == 52 && loan.InterestRemainder == 0.5m && loan.PayableInterest == 53 && wallet.Balance == balance, "seven normal-rate days accumulate exact fractional interest without wallet debit");
        var interest = loan.PayableInterest;
        Assert(service.RepayLoan(1, wallet, interest, "repay-interest", 3).Status == TransactionStatus.Applied, "interest-first repayment");
        Assert(service.State.SharedAccount.Loan.Principal == 5000 && service.State.SharedAccount.Loan.AccruedInterest == 0 && service.State.SharedAccount.Loan.InterestRemainder == 0m, "principal remains untouched while visible whole and rounded fractional interest are paid");
        Assert(service.SettleToDay(9, "late", 4).Status == TransactionStatus.Applied, "settle after maturity");
        Assert(service.State.SharedAccount.Loan.Status == LoanStatus.Matured, "overdue status");
    }

    private static BankConfig FastCollectionConfig()
    {
        var config = new BankConfig
        {
            OverdueGraceDays = 0,
            DefaultAfterDays = 3,
            MinimumActiveBalanceReserve = 100,
            MinimumWalletReserve = 5000,
            IncomeGarnishmentPercent = 25m,
            MaximumOverdueInterestPercent = 20m
        };
        config.LoanProducts[0].Days = 1;
        config.LoanProducts[0].DailyRatePercent = 0m;
        return config;
    }

    private static BankService OpenFastLoan(BankConfig config, TestWallet wallet, long amount, long activeBalance = 0)
    {
        var state = new BankState { WalletMode = WalletMode.Shared, LastSettlementDay = 0 };
        state.SharedAccount.ActiveBalance = activeBalance;
        var service = new BankService(config, state);
        Assert(service.RequestLoan(1, wallet, 0, amount, 0, "fast-loan", 0).Status == TransactionStatus.Applied, "fast collection loan opens");
        return service;
    }

    private static void TestCollectionDatesAndGradualInterestCap()
    {
        var config = FastCollectionConfig();
        config.LoanProducts[0].DailyRatePercent = 10m;
        config.OverdueRateMultiplier = 1m;
        var wallet = new TestWallet(0, 100000);
        var service = OpenFastLoan(config, wallet, 1000);
        var loan = service.State.SharedAccount.Loan;
        Assert(loan.MaturityDay == 1 && loan.CollectionStartDay == 2 && loan.DefaultDay == 5, "collection and default dates use the locked formulas");
        var updated = service.Config;
        updated.EnableOverdueCollection = false;
        updated.OverdueGraceDays = 9;
        updated.EnableActiveBalanceSetoff = false;
        updated.MinimumActiveBalanceReserve = 999;
        updated.MinimumWalletReserve = 9999;
        updated.IncomeGarnishmentPercent = 50m;
        updated.MaximumOverdueInterestPercent = 30m;
        updated.DefaultAfterDays = 12;
        service.UpdateConfig(updated);
        Assert(loan.LockedCollectionEnabled && loan.LockedGraceDays == 0 && loan.LockedActiveBalanceSetoff
            && loan.LockedMinimumActiveBalanceReserve == 100 && loan.LockedMinimumWalletReserve == 5000
            && loan.LockedIncomeGarnishmentPercent == 25m && loan.LockedMaximumOverdueInterestPercent == 20m && loan.LockedDefaultAfterDays == 3,
            "GMCM hot updates affect future loans without rewriting all eight locked collection terms on an existing contract");
        Assert(service.SettleToDay(1, "collection-date-1", service.State.Revision).Status == TransactionStatus.Applied && loan.Status == LoanStatus.Current, "maturity day remains current");
        Assert(service.SettleToDay(2, "collection-date-2", service.State.Revision).Status == TransactionStatus.Applied && loan.Status == LoanStatus.Collection, "the day after a zero-day grace enters collection");
        Assert(loan.OverdueInterestCharged == 100m, "overdue interest starts with one daily increment instead of jumping to its cap");
        Assert(service.SettleToDay(4, "collection-date-4", service.State.Revision).Status == TransactionStatus.Applied && loan.OverdueInterestCharged == 200m, "overdue interest grows gradually and stops at the configured cap");
        var cappedInterest = loan.PayableInterest;
        Assert(service.SettleToDay(5, "collection-date-5", service.State.Revision).Status == TransactionStatus.Applied && loan.Status == LoanStatus.Defaulted && loan.PayableInterest == cappedInterest, "defaulted is a nonterminal status and does not reopen the interest cap");
        Assert(service.CheckWalletModeMigration(WalletMode.Personal).Message == "wallet-mode-migration-open-products",
            "a defaulted loan remains unsettled debt and continues blocking wallet-mode changes");

        var continuingConfig = FastCollectionConfig();
        continuingConfig.LoanProducts[0].DailyRatePercent = 1m;
        continuingConfig.OverdueRateMultiplier = 1m;
        continuingConfig.MaximumOverdueInterestPercent = 100m;
        var continuing = OpenFastLoan(continuingConfig, new TestWallet(0, 100000), 1000);
        Assert(continuing.SettleToDay(4, "default-interest-before", continuing.State.Revision).Status == TransactionStatus.Applied
            && continuing.State.SharedAccount.Loan.Status == LoanStatus.Collection, "uncapped loan remains in collection before its default date");
        var interestBeforeDefault = continuing.State.SharedAccount.Loan.PayableInterest;
        Assert(continuing.SettleToDay(5, "default-interest-after", continuing.State.Revision).Status == TransactionStatus.Applied
            && continuing.State.SharedAccount.Loan.Status == LoanStatus.Defaulted
            && continuing.State.SharedAccount.Loan.PayableInterest > interestBeforeDefault,
            "defaulted remains nonterminal and continues accruing daily interest until the configured cap is reached");
    }

    private static void TestNaturalTermMaturityThenActiveSetoff()
    {
        var config = FastCollectionConfig();
        config.MinimumActiveBalanceReserve = 0;
        config.TermProducts[0].Days = 1;
        config.TermProducts[0].DailyRatePercent = 0m;
        var wallet = new TestWallet(2000, 100000);
        var service = OpenFastLoan(config, wallet, 1000);
        Assert(service.CreateTermDeposit(1, wallet, 0, 1000, 0, "collection-term", service.State.Revision).Status == TransactionStatus.Applied,
            "an unexpired term deposit remains a separate contract while the loan is current");
        Assert(service.SettleToDay(2, "collection-term-maturity", service.State.Revision).Status == TransactionStatus.Applied
            && service.State.SharedAccount.TermDeposits.Count == 0
            && service.State.SharedAccount.ActiveBalance == 1000
            && service.State.SharedAccount.Loan.Status == LoanStatus.Collection,
            "natural term maturity transfers its proceeds to active savings before scheduled collection");
        var collected = service.CollectScheduledPayment(1, null, 2, true, false, "collection-term-setoff", service.State.Revision);
        Assert(collected.Status == TransactionStatus.Applied && collected.ActiveSetoffAmount == 1000
            && service.State.SharedAccount.ActiveBalance == 0
            && service.State.SharedAccount.Loan.Status == LoanStatus.Settled,
            "only the naturally matured active balance is set off and it settles the overdue loan");
    }

    private static void TestScheduledCollectionOrderReservesAndIdempotency()
    {
        var config = FastCollectionConfig();
        config.MinimumWalletReserve = 5600;
        var wallet = new TestWallet(5000, 100000);
        var service = OpenFastLoan(config, wallet, 3000, 600);
        Assert(service.SettleToDay(2, "scheduled-settle", service.State.Revision).Status == TransactionStatus.Applied, "scheduled collection loan reaches collection");
        var historyBefore = wallet.HistoricalTotalEarnings;
        var result = service.CollectScheduledPayment(1, wallet, 2, true, true, "scheduled-both", service.State.Revision);
        Assert(result.Status == TransactionStatus.Applied && result.ActiveSetoffAmount == 500 && result.WalletCollectionAmount == 2400, "scheduled collection applies active setoff before wallet sweep and honors both reserves");
        Assert(service.State.SharedAccount.ActiveBalance == 100 && wallet.Balance == 5600 && service.State.SharedAccount.Loan.Principal == 100, "scheduled collection never makes either source negative or crosses a reserve");
        Assert(wallet.HistoricalTotalEarnings == historyBefore, "collection does not change historical total earnings");
        var revision = service.State.Revision;
        var duplicate = service.CollectScheduledPayment(1, wallet, 2, true, true, "scheduled-repeat", revision);
        Assert(duplicate.Status == TransactionStatus.Duplicate && service.State.Revision == revision && service.State.SharedAccount.Loan.Principal == 100, "same-day scheduled collection is idempotent without an empty transaction");
    }

    private static void TestIncomeGarnishmentRemainder()
    {
        var config = FastCollectionConfig();
        config.MinimumWalletReserve = 0;
        var wallet = new TestWallet(0, 0);
        var service = OpenFastLoan(config, wallet, 1000);
        Assert(service.SettleToDay(2, "garnishment-settle", service.State.Revision).Status == TransactionStatus.Applied, "garnishment loan reaches collection");
        for (var i = 0; i < 3; i++)
        {
            wallet.AddOrdinaryIncome(1);
            var partial = service.CollectIncomeGarnishment(1, wallet, 1, 2, "garnishment-part-" + i, service.State.Revision);
            Assert(partial.Status == TransactionStatus.Applied && partial.WalletCollectionAmount == 0, "fractional garnishment is persisted without fabricating a payment record");
        }
        wallet.AddOrdinaryIncome(1);
        var whole = service.CollectIncomeGarnishment(1, wallet, 1, 2, "garnishment-whole", service.State.Revision);
        Assert(whole.Status == TransactionStatus.Applied && whole.WalletCollectionAmount == 1 && service.State.SharedAccount.Loan.GarnishmentRemainder == 0m, "four 25% income samples collect exactly one gold");
        Assert(wallet.Balance == 1003 && wallet.HistoricalTotalEarnings == 4, "garnishment changes wallet balance but never rewrites the game's earnings counter");
    }

    private static void TestOfflineSetoffAndReconnectSweep()
    {
        var config = FastCollectionConfig();
        config.MinimumWalletReserve = 1000;
        var wallet = new TestWallet(0, 0);
        var service = OpenFastLoan(config, wallet, 2000, 600);
        Assert(service.SettleToDay(2, "offline-settle", service.State.Revision).Status == TransactionStatus.Applied, "offline collection loan reaches collection");
        var offline = service.CollectScheduledPayment(1, null, 2, true, false, "offline-active", service.State.Revision);
        Assert(offline.Status == TransactionStatus.Applied && offline.ActiveSetoffAmount == 500 && service.State.SharedAccount.Loan.LastWalletSweepDay == null, "an offline player can receive active setoff without falsely checkpointing an unavailable wallet");
        wallet.AddOrdinaryIncome(2500);
        var reconnect = service.CollectScheduledPayment(1, wallet, 2, false, true, "reconnect-wallet", service.State.Revision);
        Assert(reconnect.Status == TransactionStatus.Applied && reconnect.WalletCollectionAmount == 1500 && service.State.SharedAccount.Loan.Status == LoanStatus.Settled, "reconnect performs the missing same-day wallet sweep and can settle the debt");
    }

    private static void TestDebtCancellationAndCreditRestrictionAreIndependent()
    {
        var config = FastCollectionConfig();
        var wallet = new TestWallet(0, 100000);
        var service = OpenFastLoan(config, wallet, 1000);
        service.State.ModeMismatch = true;
        Assert(service.SetCreditRestriction(1, CreditRestrictionMode.Permanent, null, "ban-during-mismatch", service.State.Revision).Message == "wallet-mode-mismatch",
            "administrative account mutations remain locked while the game and bank wallet modes disagree");
        service.State.ModeMismatch = false;
        Assert(service.SetCreditRestriction(1, CreditRestrictionMode.Permanent, null, "ban", service.State.Revision).Status == TransactionStatus.Applied, "a permanent credit restriction can be set independently");
        Assert(service.CancelDebt(1, 1, "cancel", service.State.Revision).Status == TransactionStatus.Applied, "administrative debt cancellation succeeds independently");
        Assert(service.State.SharedAccount.Loan.Status == LoanStatus.Cancelled && service.State.SharedAccount.CreditRestriction.Mode == CreditRestrictionMode.Permanent, "cancelling debt does not clear or create a credit restriction");
        Assert(service.RequestLoan(1, wallet, 0, 1000, 1, "blocked-by-credit", service.State.Revision).Message == "loan-credit-restricted", "a cancelled debt does not bypass an independently retained credit restriction");
        Assert(service.ClearCreditRestriction(1, "clear-ban", service.State.Revision).Status == TransactionStatus.Applied && service.State.SharedAccount.CreditRestriction.Mode == CreditRestrictionMode.None, "credit restriction clearing does not alter the cancelled contract");
    }

    private static void TestCreditRestrictionWalletModeMigration()
    {
        var state = new BankState { WalletMode = WalletMode.Personal, ModeMismatch = true };
        state.PersonalAccounts[1] = new Account
        {
            Id = "player-1",
            ActiveBalance = 10,
            CreditRestriction = new CreditRestriction { Mode = CreditRestrictionMode.UntilDay, UntilDay = 20 }
        };
        state.PersonalAccounts[2] = new Account
        {
            Id = "player-2",
            ActiveBalance = 20,
            CreditRestriction = new CreditRestriction { Mode = CreditRestrictionMode.Permanent }
        };
        var service = new BankService(new BankConfig(), state);
        var merged = service.MigrateWalletMode(WalletMode.Shared, 1, 0);
        Assert(merged.Status == TransactionStatus.Applied && service.State.SharedAccount.CreditRestriction.Mode == CreditRestrictionMode.Permanent,
            "personal-to-shared migration preserves and merges independent credit restrictions without treating them as open debt");
        service.State.ModeMismatch = true;
        var split = service.MigrateWalletMode(WalletMode.Personal, 1, service.State.Revision);
        Assert(split.Status == TransactionStatus.Applied && service.State.PersonalAccounts[1].CreditRestriction.Mode == CreditRestrictionMode.Permanent,
            "shared-to-personal migration carries the merged restriction to the stable host account");
    }

    private static void TestSchemaThreeMigrationLocksCollectionTerms()
    {
        var config = FastCollectionConfig();
        config.OverdueGraceDays = 9;
        config.MinimumWalletReserve = 4321;
        var legacy = new BankState { SchemaVersion = 2, WalletMode = WalletMode.Shared, LastSettlementDay = 20 };
        legacy.SharedAccount.Loan = new LoanContract
        {
            Id = "schema-2-loan", Principal = 2000, TermDays = 7, LockedRatePercent = 0.1m,
            OverdueMultiplier = 1.5m, OpenDay = 10, MaturityDay = 17, LastSettledDay = 20, Status = LoanStatus.Matured
        };
        BankState migrated;
        Assert(BankStateMigrator.TryMigrateToCurrent(legacy, config, 20, out migrated), "schema two explicitly migrates to schema three");
        var loan = migrated.SharedAccount.Loan;
        Assert(migrated.SchemaVersion == 3 && loan.OriginalPrincipal == 2000 && loan.LockedGraceDays == 9 && loan.LockedMinimumWalletReserve == 4321, "schema three migration locks the current collection configuration into existing internal-development loans");
        Assert(loan.CollectionStartDay == 27 && loan.DefaultDay == 30 && loan.Status == LoanStatus.Overdue && BankStateValidator.Validate(migrated).Count == 0, "migrated dates and status are derived deterministically and validate");
    }

    private static void TestWalletRollbackFailureIsReported()
    {
        var state = new BankState();
        state.SharedAccount.ActiveBalance = long.MaxValue;
        var service = new BankService(new BankConfig(), state);
        var wallet = new FailingRestoreWallet(1);
        var result = service.Deposit(1, wallet, 1, "rollback-failure", 0);
        Assert(result.Status == TransactionStatus.Rejected && result.Message == "wallet-rollback-failed", "a failed wallet rollback is elevated instead of being hidden as an ordinary transaction rejection");
        Assert(service.State.SharedAccount.ActiveBalance == long.MaxValue, "bank state still rolls back when wallet restoration fails");

        var collectionConfig = FastCollectionConfig();
        collectionConfig.MinimumWalletReserve = 0;
        var collectionWallet = new TestWallet(0, 0);
        var collectionService = OpenFastLoan(collectionConfig, collectionWallet, 1000);
        Assert(collectionService.SettleToDay(2, "rollback-collection-settle", collectionService.State.Revision).Status == TransactionStatus.Applied, "rollback collection loan reaches collection");
        collectionService.State.Revision = long.MaxValue;
        var walletBefore = collectionWallet.Balance;
        var principalBefore = collectionService.State.SharedAccount.Loan.Principal;
        var collectionFailure = collectionService.CollectScheduledPayment(1, collectionWallet, 2, false, true, "rollback-collection", long.MaxValue);
        Assert(collectionFailure.Status == TransactionStatus.Rejected && collectionFailure.Message == "amount-overflow", "a post-wallet collection failure is reported after rollback");
        Assert(collectionWallet.Balance == walletBefore && collectionService.State.SharedAccount.Loan.Principal == principalBefore && !collectionService.State.SharedAccount.Loan.LastWalletSweepDay.HasValue,
            "collection rollback restores wallet, debt, checkpoints, revision, and transaction history atomically");
    }

    private static void TestWalletRolesAndAccountResolution()
    {
        var shared = NewService(WalletMode.Shared);
        var hostWallet = new TestWallet(2000, 9000);
        var farmhandWallet = new TestWallet(2000, 7000);

        Assert(object.ReferenceEquals(shared.GetAccount(101), shared.GetAccount(202)), "shared mode resolves the host and farmhand to one account instance");
        Assert(shared.Deposit(101, hostWallet, 250, "shared-host-deposit", 0).Status == TransactionStatus.Applied, "host wallet can deposit into the shared account");
        Assert(hostWallet.Balance == 1750 && farmhandWallet.Balance == 2000, "a shared-account deposit changes only the wallet of the player who submitted it");
        Assert(shared.Snapshot(202, farmhandWallet).ActiveBalance == 250, "the farmhand resolves the shared account balance after the host deposits");
        Assert(shared.Deposit(202, farmhandWallet, 125, "shared-farmhand-deposit", 1).Status == TransactionStatus.Applied, "farmhand wallet can deposit into the shared account");
        Assert(shared.Snapshot(101, hostWallet).ActiveBalance == 375 && shared.Snapshot(202, farmhandWallet).ActiveBalance == 375, "both wallet roles read the same shared balance after either player transacts");
        Assert(hostWallet.HistoricalTotalEarnings == 9000 && farmhandWallet.HistoricalTotalEarnings == 7000, "bank transactions do not alter either wallet role's historical earnings");

        var personal = NewService(WalletMode.Personal);
        var personalHostWallet = new TestWallet(2000, 9000);
        var personalFarmhandWallet = new TestWallet(2000, 7000);
        Assert(personal.Deposit(101, personalHostWallet, 250, "personal-host-deposit", 0).Status == TransactionStatus.Applied, "host wallet can deposit into its personal account");
        Assert(personal.Deposit(202, personalFarmhandWallet, 125, "personal-farmhand-deposit", 1).Status == TransactionStatus.Applied, "farmhand wallet can deposit into its personal account");
        Assert(!object.ReferenceEquals(personal.GetAccount(101), personal.GetAccount(202)), "personal mode resolves host and farmhand to different account instances");
        Assert(personal.Snapshot(101, personalHostWallet).ActiveBalance == 250 && personal.Snapshot(202, personalFarmhandWallet).ActiveBalance == 125, "personal snapshots remain isolated by multiplayer ID");
    }

    private static void TestPersonalAccountIsolation()
    {
        var service = NewService(WalletMode.Personal); var a = new TestWallet(10000, 0); var b = new TestWallet(10000, 0);
        Assert(service.Deposit(11, a, 5000, "a", 0).Status == TransactionStatus.Applied, "personal A deposit");
        Assert(service.Deposit(22, b, 7000, "b", 1).Status == TransactionStatus.Applied, "personal B deposit");
        Assert(service.GetAccount(11).ActiveBalance == 5000 && service.GetAccount(22).ActiveBalance == 7000, "accounts separated by multiplayer ID");
        Assert(service.GetAccount(11).Id != service.GetAccount(22).Id, "display names are not keys");
        Assert(service.Withdraw(11, a, 1000, "a-withdraw", 2).Status == TransactionStatus.Applied, "personal A withdrawal applies");
        Assert(a.Balance == 6000 && service.GetAccount(11).ActiveBalance == 4000, "personal withdrawal updates only the selected wallet and account");
        Assert(b.Balance == 3000 && service.GetAccount(22).ActiveBalance == 7000, "personal B wallet and account remain isolated");
    }

    private static void TestModeSwitchBlocksOpenProductsAndMergesOnlyActiveBalance()
    {
        // A shared account with an open term deposit and unsettled loan cannot
        // be split into personal wallets: only active balances migrate, so an
        // open contract would otherwise be dropped silently.
        var sharedOpen = new BankState { WalletMode = WalletMode.Shared, Revision = 8, ModeMismatch = true };
        sharedOpen.SharedAccount = NewContractAccount("shared-account", 1111, "shared-term", 2222);
        var blockedShared = new BankService(new BankConfig(), sharedOpen);
        var blockedCheck = blockedShared.CheckWalletModeMigration(WalletMode.Personal);
        Assert(blockedCheck.Status == TransactionStatus.Rejected && blockedCheck.Message == "wallet-mode-migration-open-products", "shared-to-personal preflight rejects an open shared contract");
        var blockedMigrate = blockedShared.MigrateWalletMode(WalletMode.Personal, 101, 8);
        Assert(blockedMigrate.Status == TransactionStatus.Rejected && blockedMigrate.Message == "wallet-mode-migration-open-products" && !blockedMigrate.StateChanged, "shared-to-personal migration rejects an open shared contract atomically");
        Assert(blockedShared.State.WalletMode == WalletMode.Shared && blockedShared.State.SharedAccount.ActiveBalance == 1111 && blockedShared.State.SharedAccount.TermDeposits.Count == 1 && blockedShared.State.SharedAccount.Loan != null, "a rejected split preserves the complete shared source");

        // A shared account with only an active balance splits into the host's
        // personal account; term deposits and loans are not transferred.
        var sharedClean = new BankState { WalletMode = WalletMode.Shared, Revision = 8, ModeMismatch = true };
        sharedClean.SharedAccount = new Account { Id = "shared", ActiveBalance = 1111 };
        var personalView = new BankService(new BankConfig(), sharedClean);
        var toPersonal = personalView.MigrateWalletMode(WalletMode.Personal, 101, 8);

        Assert(toPersonal.Status == TransactionStatus.Applied && toPersonal.StateChanged && toPersonal.NewRevision == 9, "shared-to-personal migration applies once and advances revision");
        Assert(personalView.State.WalletMode == WalletMode.Personal && !personalView.State.ModeMismatch, "shared-to-personal migration activates the requested topology");
        Assert(personalView.State.PersonalAccounts[101].ActiveBalance == 1111 && personalView.State.PersonalAccounts[101].TermDeposits.Count == 0 && personalView.State.PersonalAccounts[101].Loan == null, "only the active balance moves into the host personal account");
        Assert(personalView.State.SharedAccount.ActiveBalance == 0 && personalView.State.SharedAccount.TermDeposits.Count == 0 && personalView.State.SharedAccount.Loan == null, "shared source is cleared after the active balance moves");
        Assert(toPersonal.ActiveBalanceMoved == 1111 && toPersonal.TermDepositsMoved == 0 && toPersonal.LoanContractsMoved == 0, "shared-to-personal diagnostics report active-balance-only migration");
        var repeatedPersonal = personalView.MigrateWalletMode(WalletMode.Personal, 101, 9);
        Assert(repeatedPersonal.Status == TransactionStatus.Applied && !repeatedPersonal.StateChanged && personalView.State.Revision == 9, "repeating shared-to-personal migration is idempotent");
        var firstPersonalTransaction = personalView.Deposit(101, new TestWallet(10000, 0), 250, "first-personal-transaction", 9);
        Assert(firstPersonalTransaction.Status == TransactionStatus.Applied && personalView.State.PersonalAccounts[101].ActiveBalance == 1361, "the first host transaction after shared-to-personal migration uses the migrated personal account");

        var termState = new BankState { WalletMode = WalletMode.Personal, Revision = 12, ModeMismatch = true };
        termState.PersonalAccounts[101] = NewContractAccount("player-101", 3333, "personal-host-term", 4444);
        termState.PersonalAccounts[101].Loan = null;
        termState.PersonalAccounts[202] = new Account { Id = "player-202", ActiveBalance = 5555 };
        var termView = new BankService(new BankConfig(), termState);
        var termCheck = termView.CheckWalletModeMigration(WalletMode.Shared);
        Assert(termCheck.Status == TransactionStatus.Rejected && termCheck.Message == "wallet-mode-migration-open-products", "the immediate preflight rejects any personal open term deposit");
        var termReject = termView.MigrateWalletMode(WalletMode.Shared, 101, 12);
        Assert(termReject.Status == TransactionStatus.Rejected && termReject.Message == "wallet-mode-migration-open-products" && !termReject.StateChanged, "personal-to-shared migration rejects open terms before changing state");
        Assert(termView.State.WalletMode == WalletMode.Personal && termView.State.PersonalAccounts[101].TermDeposits.Count == 1 && termView.State.SharedAccount.ActiveBalance == 0, "open-term rejection preserves every source account");

        var loanState = new BankState { WalletMode = WalletMode.Personal, Revision = 13, ModeMismatch = true };
        loanState.PersonalAccounts[101] = NewContractAccount("player-101-loan", 3333, "unused-term", 4444);
        loanState.PersonalAccounts[101].TermDeposits.Clear();
        var loanView = new BankService(new BankConfig(), loanState);
        Assert(loanView.CheckWalletModeMigration(WalletMode.Shared).Message == "wallet-mode-migration-open-products", "the immediate preflight rejects any personal unsettled loan");
        var loanReject = loanView.MigrateWalletMode(WalletMode.Shared, 101, 13);
        Assert(loanReject.Status == TransactionStatus.Rejected && loanReject.Message == "wallet-mode-migration-open-products" && loanView.State.WalletMode == WalletMode.Personal, "unsettled-loan rejection preserves the personal topology");

        var personalState = new BankState { WalletMode = WalletMode.Personal, Revision = 14, ModeMismatch = true };
        personalState.PersonalAccounts[101] = new Account
        {
            Id = "player-101",
            ActiveBalance = 3333,
            Loan = new LoanContract
            {
                Id = "settled-loan", OriginalPrincipal = 0, Principal = 0, AccruedInterest = 0, TermDays = 28,
                LockedRatePercent = 0.2m, OverdueMultiplier = 1.5m, OpenDay = 5,
                LockedCollectionEnabled = true, LockedGraceDays = 7, LockedActiveBalanceSetoff = true,
                LockedMinimumWalletReserve = 1000, LockedIncomeGarnishmentPercent = 25m,
                LockedMaximumOverdueInterestPercent = 100m, LockedDefaultAfterDays = 28,
                MaturityDay = 33, CollectionStartDay = 41, DefaultDay = 69, LastSettledDay = 33,
                Status = LoanStatus.Settled, ResolvedDay = 33, Resolution = LoanResolutionKind.Repaid
            }
        };
        personalState.PersonalAccounts[202] = new Account { Id = "player-202", ActiveBalance = 5555 };
        var sharedView = new BankService(new BankConfig(), personalState);
        var toShared = sharedView.MigrateWalletMode(WalletMode.Shared, 101, 14);

        Assert(toShared.Status == TransactionStatus.Applied && toShared.StateChanged && toShared.NewRevision == 15, "personal-to-shared migration applies after all open products are cleared");
        Assert(sharedView.State.WalletMode == WalletMode.Shared && !sharedView.State.ModeMismatch, "personal-to-shared migration activates the shared topology");
        Assert(sharedView.State.SharedAccount.ActiveBalance == 8888, "personal active balances are merged exactly once");
        Assert(sharedView.State.SharedAccount.TermDeposits.Count == 0 && sharedView.State.SharedAccount.Loan == null, "personal term deposits and loans are never transferred into the shared account");
        Assert(sharedView.State.PersonalAccounts.Count == 0, "personal source accounts are cleared after an eligible merge");
        Assert(toShared.ActiveBalanceMoved == 8888 && toShared.SourceAccountCount == 2 && toShared.TermDepositsMoved == 0 && toShared.LoanContractsMoved == 0, "personal-to-shared diagnostics report active-balance-only migration");
        var repeatedShared = sharedView.MigrateWalletMode(WalletMode.Shared, 101, 15);
        Assert(repeatedShared.Status == TransactionStatus.Applied && !repeatedShared.StateChanged && sharedView.State.Revision == 15, "repeating personal-to-shared migration is idempotent");
    }

    private static void TestMigrationAndSettlementBalanceAccounting()
    {
        var state = new BankState
        {
            WalletMode = WalletMode.Shared,
            LastSettlementDay = 4,
            SharedAccount = new Account { Id = "shared", ActiveBalance = 10000 }
        };
        var service = new BankService(new BankConfig(), state);

        var migration = service.MigrateWalletMode(WalletMode.Personal, 101, 0);
        Assert(migration.Status == TransactionStatus.Applied && migration.ActiveBalanceMoved == 10000, "shared-to-personal migration reports the exact 10,000g source balance");
        Assert(service.State.PersonalAccounts[101].ActiveBalance == 10000, "wallet-mode migration itself neither creates nor removes one gold");

        var settlement = service.SettleToDay(5, "post-migration-day-5", service.State.Revision);
        Assert(settlement.Status == TransactionStatus.Applied, "the first post-migration daily settlement applies normally");
        Assert(service.State.PersonalAccounts[101].ActiveBalance == 10000 && service.State.PersonalAccounts[101].ActiveInterestRemainder == 0.5m, "the configured 0.005% daily active interest accrues exactly after migration without premature rounding");
    }

    private static void TestSnapshotRevisionAndSharedAccountContract()
    {
        var source = new BankState { WalletMode = WalletMode.Shared, Revision = 20 };
        source.SharedAccount = NewContractAccount("shared-snapshot", 7777, "snapshot-term", 8888);
        var sharedSnapshot = BuildSharedSnapshot(source);
        Assert(sharedSnapshot.SharedAccount != null && sharedSnapshot.SharedAccount.ActiveBalance == 7777, "a shared snapshot contains the shared active account balance");
        Assert(sharedSnapshot.SharedAccount.TermDeposits.Count == 1 && sharedSnapshot.SharedAccount.TermDeposits[0].Id == "snapshot-term", "a shared snapshot contains the shared term-deposit collection");
        Assert(sharedSnapshot.SharedAccount.Loan != null && sharedSnapshot.SharedAccount.Loan.Principal == 8888, "a shared snapshot contains the shared loan contract");
        Assert(sharedSnapshot.PersonalAccounts.Count == 0, "a shared snapshot does not invent personal account entries");

        var replica = new SnapshotReplica(source);
        var stale = source.Clone();
        stale.Revision = 19;
        stale.SharedAccount.ActiveBalance = 1;
        stale.SharedAccount.TermDeposits.Clear();
        stale.SharedAccount.Loan = null;
        Assert(!replica.TryApply(stale), "a snapshot with an older revision is rejected");
        Assert(replica.State.Revision == 20 && replica.State.SharedAccount.ActiveBalance == 7777 && replica.State.SharedAccount.TermDeposits.Count == 1 && replica.State.SharedAccount.Loan != null, "a stale snapshot cannot overwrite newer active, term, or loan data");

        var fresh = source.Clone();
        fresh.Revision = 21;
        fresh.SharedAccount.ActiveBalance = 9999;
        Assert(replica.TryApply(fresh), "a snapshot with a newer revision is accepted");
        Assert(replica.State.Revision == 21 && replica.State.SharedAccount.ActiveBalance == 9999, "the accepted snapshot advances the replica revision and state together");
    }

    private static void TestMigrationOverflowSafety()
    {
        // Revision overflow is another all-or-nothing failure boundary.
        var overflowState = new BankState { WalletMode = WalletMode.Shared, ModeMismatch = true, Revision = long.MaxValue };
        overflowState.SharedAccount = new Account { Id = "overflow-shared", ActiveBalance = 111 };
        var overflow = new BankService(new BankConfig(), overflowState);
        var overflowResult = overflow.MigrateWalletMode(WalletMode.Personal, 101, long.MaxValue);
        Assert(overflowResult.Status == TransactionStatus.Rejected && overflowResult.Message == "wallet-mode-migration-overflow", "a migration revision overflow is rejected");
        Assert(overflow.State.WalletMode == WalletMode.Shared && overflow.State.Revision == long.MaxValue && overflow.State.SharedAccount.ActiveBalance == 111, "revision overflow leaves the original topology and products untouched");
    }

    private static void TestSameModeMismatchReconciliationAndPersonalPersistence()
    {
        // A transient ModeMismatch flag on the already-correct topology is not
        // a topology migration.  Clearing it must not move assets or advance
        // the revision; otherwise every host transaction after a rolled-back
        // vanilla wallet request would be pushed to a different revision.
        var personalMismatch = new BankState { WalletMode = WalletMode.Personal, ModeMismatch = true, Revision = 50 };
        var personalReconciled = new BankService(new BankConfig(), personalMismatch);
        var personalResult = personalReconciled.MigrateWalletMode(WalletMode.Personal, 101, 50);
        Assert(personalResult.Status == TransactionStatus.Applied && !personalResult.StateChanged && personalResult.NewRevision == 50, "same-mode personal reconciliation is a revision-neutral no-op");
        Assert(!personalReconciled.State.ModeMismatch && personalReconciled.State.Revision == 50 && personalReconciled.State.PersonalAccounts.Count == 0, "same-mode personal reconciliation only clears the stale mismatch marker");

        var sharedMismatch = new BankState { WalletMode = WalletMode.Shared, ModeMismatch = true, Revision = 50 };
        var sharedReconciled = new BankService(new BankConfig(), sharedMismatch);
        var sharedResult = sharedReconciled.MigrateWalletMode(WalletMode.Shared, 101, 50);
        Assert(sharedResult.Status == TransactionStatus.Applied && !sharedResult.StateChanged && sharedResult.NewRevision == 50 && !sharedReconciled.State.ModeMismatch, "same-mode shared reconciliation is a revision-neutral no-op");

        // After reconciliation, a personal-mode host deposit must land in the
        // stable owner account and remain there; the account must not fall
        // back to a temporary empty snapshot.
        var wallet = new TestWallet(10000, 0);
        var deposited = personalReconciled.Deposit(101, wallet, 5000, "personal-after-reconcile", 50);
        Assert(deposited.Status == TransactionStatus.Applied && personalReconciled.State.PersonalAccounts[101].ActiveBalance == 5000, "personal deposit after same-mode reconciliation persists in the owner account");
        Assert(wallet.Balance == 5000, "personal deposit after same-mode reconciliation changes only the submitting wallet");

        // A personal-mode term deposit must also persist on the owner account.
        var personalTermState = new BankState { WalletMode = WalletMode.Personal, Revision = 0 };
        var personalTermService = new BankService(new BankConfig(), personalTermState);
        var termWallet = new TestWallet(20000, 0);
        var termResult = personalTermService.CreateTermDeposit(101, termWallet, 0, 1000, 5, "personal-term", 0);
        Assert(termResult.Status == TransactionStatus.Applied && termWallet.Balance == 19000, "personal term deposit debits only the submitting wallet");
        Assert(personalTermService.State.PersonalAccounts[101].TermDeposits.Count == 1 && personalTermService.State.PersonalAccounts[101].TermDeposits[0].Id.StartsWith("TD-"), "personal term deposit persists in the owner account");
        Assert(personalTermService.State.PersonalAccounts[101].ActiveBalance == 0, "personal term deposit does not create phantom active savings");
    }

    private static void AssertAccountAssets(Account account, long activeBalance, string termId, long loanPrincipal, string message)
    {
        Assert(account != null && account.ActiveBalance == activeBalance, message + ": active balance");
        Assert(account.TermDeposits != null && account.TermDeposits.Count == 1 && account.TermDeposits[0].Id == termId, message + ": term deposit");
        Assert(account.Loan != null && account.Loan.Principal == loanPrincipal, message + ": loan contract");
    }

    private static Account NewContractAccount(string id, long activeBalance, string termId, long loanPrincipal)
    {
        return new Account
        {
            Id = id,
            ActiveBalance = activeBalance,
            TermDeposits = new List<TermDeposit>
            {
                new TermDeposit
                {
                    Id = termId, Principal = 1000, Balance = 1100, TermDays = 7,
                    LockedTermRatePercent = 0.02m, LockedActivityRatePercent = 0.01m,
                    OpenDay = 5, MaturityDay = 12, DaysSettled = 1
                }
            },
            Loan = new LoanContract
            {
                Id = id + "-loan", OriginalPrincipal = loanPrincipal, Principal = loanPrincipal, AccruedInterest = 25,
                TermDays = 28, LockedRatePercent = 0.2m, OverdueMultiplier = 1.5m,
                LockedCollectionEnabled = true, LockedGraceDays = 7, LockedActiveBalanceSetoff = true,
                LockedMinimumWalletReserve = 1000, LockedIncomeGarnishmentPercent = 25m,
                LockedMaximumOverdueInterestPercent = 100m, LockedDefaultAfterDays = 28,
                OpenDay = 5, MaturityDay = 33, CollectionStartDay = 41, DefaultDay = 69,
                LastSettledDay = 6, Status = LoanStatus.Current
            }
        };
    }

    private static BankState BuildSharedSnapshot(BankState source)
    {
        return new BankState
        {
            SchemaVersion = source.SchemaVersion,
            WalletMode = source.WalletMode,
            ModeMismatch = source.ModeMismatch,
            DataInvalid = source.DataInvalid,
            SharedAccount = source.WalletMode == WalletMode.Shared ? source.SharedAccount.Clone() : new Account { Id = "shared" },
            PersonalAccounts = new Dictionary<long, Account>(),
            LastSettlementDay = source.LastSettlementDay,
            Revision = source.Revision,
            ProcessedTransactionIds = new List<string>()
        };
    }

    private sealed class SnapshotReplica
    {
        public BankState State { get; private set; }
        public SnapshotReplica(BankState initial) { State = initial.Clone(); }
        public bool TryApply(BankState incoming)
        {
            if (incoming == null || incoming.Revision < State.Revision) return false;
            State = incoming.Clone();
            return true;
        }
    }

    private static void TestConfigValidationAndHotReload()
    {
        var badDuration = new BankConfig(); badDuration.TermProducts[7].Days = 0;
        Assert(!badDuration.ValidateAndNormalize().IsValid, "every product duration has a minimum of one day");
        var nullSlot = new BankConfig(); nullSlot.TermProducts[7] = null;
        Assert(!nullSlot.ValidateAndNormalize().IsValid, "null disabled product slot is rejected safely");
        var nullList = new BankConfig { TermProducts = null };
        var gotArgumentError = false;
        try { new BankService(nullList); } catch (ArgumentException) { gotArgumentError = true; }
        Assert(gotArgumentError, "null config list reports validation failure instead of null reference");

        var service = NewService();
        var updated = new BankConfig { BaseCredit = 9000m, EnableAtm = false };
        service.UpdateConfig(updated);
        Assert(service.Snapshot(1, new TestWallet(0, 0)).CreditLimit == 9000, "saved config becomes active without reloading the save");
        Assert(!service.IsAtmEnabled, "ATM enablement becomes active without cloning the full config");
        Assert(service.ConfigRevision == 1, "config revision invalidates UI caches after hot reload");
        service.UpdateConfig(updated.Clone());
        Assert(service.ConfigRevision == 1, "an equivalent synchronized config does not advance the config revision");
        var changedProduct = updated.Clone();
        changedProduct.TermProducts[0].DailyRatePercent += 0.0001m;
        service.UpdateConfig(changedProduct);
        Assert(service.ConfigRevision == 2 && service.Config.TermProducts[0].DailyRatePercent == changedProduct.TermProducts[0].DailyRatePercent, "a real product change still advances the config revision and becomes active");

        Assert(!new BankConfig { ActivityDailyRatePercent = 1.01m }.ValidateAndNormalize().IsValid, "activity rate above the safe maximum is rejected");
        var highTermRate = new BankConfig(); highTermRate.TermProducts[0].DailyRatePercent = 1.01m;
        Assert(!highTermRate.ValidateAndNormalize().IsValid, "term rate above the safe maximum is rejected");
        var highLoanRate = new BankConfig(); highLoanRate.LoanProducts[0].DailyRatePercent = 10.01m;
        Assert(!highLoanRate.ValidateAndNormalize().IsValid, "loan rate above the safe maximum is rejected");
        Assert(!new BankConfig { OverdueRateMultiplier = 10.01m }.ValidateAndNormalize().IsValid, "overdue multiplier above the safe maximum is rejected");

        var duplicateDisabled = new BankConfig();
        Assert(duplicateDisabled.ValidateAndNormalize().IsValid, "disabled slots may share the same positive placeholder duration");
        duplicateDisabled.TermProducts[5].Days = 7;
        duplicateDisabled.TermProducts[5].Enabled = true;
        Assert(!duplicateDisabled.ValidateAndNormalize().IsValid, "enabled product durations must remain unique");
        Assert(BankConfig.FloorRateToFourDecimalPlaces(0.12349m) == 0.1234m, "rate text values round downward to four decimal places");
    }

    private static void TestContractIdsAfterReload()
    {
        var wallet = new TestWallet(10000, 0);
        var service = NewService();
        Assert(service.CreateTermDeposit(1, wallet, 0, 1000, 5, "first", 0).Status == TransactionStatus.Applied, "first term opens");
        var firstId = service.State.SharedAccount.TermDeposits[0].Id;
        var reloaded = new BankService(new BankConfig(), service.State.Clone());
        Assert(reloaded.CreateTermDeposit(1, wallet, 0, 1000, 5, "second", reloaded.State.Revision).Status == TransactionStatus.Applied, "second term opens after reload");
        var secondId = reloaded.State.SharedAccount.TermDeposits[1].Id;
        Assert(firstId != secondId, "contract IDs remain unique across save reloads");
    }

    private static void TestMalformedStateSafety()
    {
        var state = new BankState { SharedAccount = null, PersonalAccounts = null, ProcessedTransactionIds = null };
        Assert(BankStateValidator.Validate(state).Count > 0, "malformed save state is detected");
        var clone = state.Clone();
        Assert(clone.SharedAccount != null && clone.PersonalAccounts != null && clone.ProcessedTransactionIds != null, "malformed state can be cloned safely for rollback diagnostics");
        var malformedContractState = new BankState();
        malformedContractState.SharedAccount.TermDeposits.Add(null);
        var malformedContractClone = malformedContractState.Clone();
        Assert(malformedContractClone.SharedAccount.TermDeposits.Count == 1 && malformedContractClone.SharedAccount.TermDeposits[0] == null, "cloning does not silently discard malformed contract slots before validation");
        var malformedRemainder = new BankState();
        malformedRemainder.SharedAccount.ActiveInterestRemainder = 1m;
        Assert(BankStateValidator.Validate(malformedRemainder).Count > 0, "out-of-range interest remainders are rejected");
        Assert(BankStateValidator.Validate(new BankState()).Count == 0, "fresh save state validates");
    }

    private static void TestInterestRemainderSchemaMigration()
    {
        var legacy = new BankState
        {
            SchemaVersion = 1,
            WalletMode = WalletMode.Shared,
            SharedAccount = new Account { Id = "shared", ActiveBalance = 4321 }
        };
        BankState migrated;
        Assert(BankStateMigrator.TryMigrateToCurrent(legacy, out migrated), "schema one save migrates to the fractional-interest schema");
        Assert(legacy.SchemaVersion == BankState.CurrentSchemaVersion && object.ReferenceEquals(legacy, migrated), "schema migration upgrades only the loaded in-memory state before validation");
        Assert(migrated.SharedAccount.ActiveBalance == 4321 && migrated.SharedAccount.ActiveInterestRemainder == 0m, "schema migration preserves whole-gold assets and initializes remainders to zero");
        Assert(BankStateValidator.Validate(migrated).Count == 0, "migrated fractional-interest state validates");
        var malformedLegacy = new BankState { SchemaVersion = 1, SharedAccount = null };
        Assert(BankStateMigrator.TryMigrateToCurrent(malformedLegacy, out migrated) && BankStateValidator.Validate(migrated).Count > 0, "schema migration does not hide malformed legacy account containers");
        Assert(!BankStateMigrator.TryMigrateToCurrent(new BankState { SchemaVersion = 99 }, out migrated), "unknown future schemas remain unsupported");
    }

    private static void TestInvalidAndOverflowInputs()
    {
        var service = NewService(); var wallet = new TestWallet(5000, 0);
        Assert(service.Deposit(1, wallet, 0, "zero", 0).Status == TransactionStatus.Rejected, "zero deposit rejected atomically");
        Assert(service.Deposit(1, wallet, -1, "negative", 0).Status == TransactionStatus.Rejected, "negative deposit rejected atomically");
        Assert(service.RequestLoan(1, wallet, 0, 1500, 0, "nonmultiple", 0).Status == TransactionStatus.Rejected, "non-thousand loan rejected");
        Assert(service.RequestLoan(1, wallet, 0, 6000, 0, "overlimit", 0).Status == TransactionStatus.Rejected, "over-limit loan rejected");
        Assert(wallet.Balance == 5000 && service.State.Revision == 0, "rejected inputs leave wallet and revision unchanged");

        var state = new BankState(); state.SharedAccount.ActiveBalance = 10;
        var fullWallet = new TestWallet(long.MaxValue, 0);
        var overflowService = new BankService(new BankConfig(), state);
        Assert(overflowService.Withdraw(1, fullWallet, 1, "overflow", 0).Status == TransactionStatus.Rejected, "wallet overflow is rejected");
        Assert(fullWallet.Balance == long.MaxValue && overflowService.State.SharedAccount.ActiveBalance == 10, "wallet overflow rollback is atomic");
        Assert(service.Deposit(1, wallet, 1, new string('x', 129), service.State.Revision).Status == TransactionStatus.Rejected, "oversized transaction IDs are rejected in the core boundary");
    }

    private static void TestSecurityAndGrowthLimits()
    {
        var service = NewService();
        var wallet = new TestWallet(1000, 0);
        for (var i = 0; i < BankLimits.MaximumTermDepositsPerAccount; i++)
        {
            var result = service.CreateTermDeposit(1, wallet, 0, 1, 0, "term-limit-" + i, service.State.Revision);
            Assert(result.Status == TransactionStatus.Applied, "term deposit within the per-account limit opens");
        }
        var rejected = service.CreateTermDeposit(1, wallet, 0, 1, 0, "term-limit-rejected", service.State.Revision);
        Assert(rejected.Status == TransactionStatus.Rejected && rejected.Message == "term-limit-reached", "the 101st open term deposit is rejected atomically");
        Assert(service.State.SharedAccount.TermDeposits.Count == BankLimits.MaximumTermDepositsPerAccount && wallet.Balance == 900, "term limit rejection preserves balances");

        var tooManyAccounts = new BankState { WalletMode = WalletMode.Personal };
        for (var i = 0; i <= BankLimits.MaximumPersonalAccounts; i++)
            tooManyAccounts.PersonalAccounts[i] = new Account { Id = "player-" + i };
        Assert(BankStateValidator.Validate(tooManyAccounts).Count > 0, "oversized personal-account maps are rejected");

        var fullAccountMap = new BankState { WalletMode = WalletMode.Personal };
        for (var i = 0; i < BankLimits.MaximumPersonalAccounts; i++)
            fullAccountMap.PersonalAccounts[i] = new Account { Id = "player-" + i };
        var cappedAccountService = new BankService(new BankConfig(), fullAccountMap);
        var cappedWallet = new TestWallet(100, 0);
        var accountRejected = cappedAccountService.Deposit(999, cappedWallet, 1, "account-limit", 0);
        Assert(accountRejected.Status == TransactionStatus.Rejected && accountRejected.Message == "account-limit-reached", "a new personal account is rejected after the bounded map is full");
        Assert(cappedAccountService.State.PersonalAccounts.Count == BankLimits.MaximumPersonalAccounts && cappedWallet.Balance == 100, "personal account limit rejection rolls back without creating an entry");

        var oversizedTerms = new BankState();
        oversizedTerms.SharedAccount.TermDeposits.Add(NewLargeTerm("large-a"));
        oversizedTerms.SharedAccount.TermDeposits.Add(NewLargeTerm("large-b"));
        Assert(BankStateValidator.Validate(oversizedTerms).Count > 0, "aggregate term balances above long range are rejected");

        var defensiveSnapshot = NewService();
        defensiveSnapshot.State.SharedAccount.TermDeposits.Add(NewLargeTerm("snapshot-a"));
        defensiveSnapshot.State.SharedAccount.TermDeposits.Add(NewLargeTerm("snapshot-b"));
        Assert(defensiveSnapshot.Snapshot(1, new TestWallet(0, 0)).TermBalance == long.MaxValue, "snapshot aggregation saturates instead of throwing");
    }

    private static void TestBusinessAmountGranularity()
    {
        var service = NewService();
        var wallet = new TestWallet(10, 0);
        Assert(service.Deposit(1, wallet, 1, "one-gold-deposit", 0).Status == TransactionStatus.Applied, "active savings accepts an arbitrary positive integer amount");
        Assert(service.Withdraw(1, wallet, 1, "one-gold-withdrawal", 1).Status == TransactionStatus.Applied, "active withdrawal accepts an arbitrary positive integer amount");
        Assert(service.CreateTermDeposit(1, wallet, 0, 1, 0, "one-gold-term", 2).Status == TransactionStatus.Applied, "term deposits accept an arbitrary positive integer amount");

        var loanService = NewService();
        var loanWallet = new TestWallet(0, 0);
        Assert(loanService.RequestLoan(1, loanWallet, 0, 1000, 0, "minimum-loan", 0).Status == TransactionStatus.Applied, "the documented 1,000g minimum loan is accepted");
        var rejectedService = NewService();
        Assert(rejectedService.RequestLoan(1, new TestWallet(0, 0), 0, 999, 0, "subminimum-loan", 0).Message == "loan-must-be-1000-multiple", "a subminimum loan is rejected by the 1,000g multiple rule");
        Assert(rejectedService.RequestLoan(1, new TestWallet(0, 0), 0, 1500, 0, "fractional-thousand-loan", 0).Message == "loan-must-be-1000-multiple", "only loans require 1,000g increments");
    }

    private static void TestWithdrawalShortcutRounding()
    {
        Assert(BankMath.RoundedWithdrawalFraction(0, 1) == 0, "withdraw-all shortcut keeps an empty active balance at zero");
        Assert(BankMath.RoundedWithdrawalFraction(12345, 1) == 12345, "withdraw-all shortcut selects the complete active balance");
        Assert(BankMath.RoundedWithdrawalFraction(3, 2) == 2, "withdraw-half rounds midpoint values away from zero");
        Assert(BankMath.RoundedWithdrawalFraction(10, 4) == 3, "withdraw-quarter rounds to the nearest whole gold");
        Assert(BankMath.RoundedWithdrawalFraction(1, 4) == 1, "a nonzero balance always produces a legal positive shortcut amount");
        Assert(BankMath.RoundedWithdrawalFraction(long.MaxValue, 4) == 2305843009213693952L, "withdraw fractions support the full long balance range without overflow");
    }

    private static void TestLargeTransactionsAndProductEnablement()
    {
        const long largeAmount = 250_000_000;
        var service = NewService();
        var wallet = new TestWallet(largeAmount, 0);
        Assert(service.Deposit(1, wallet, largeAmount, "large-deposit", 0).Status == TransactionStatus.Applied, "active deposits above the old 100,000,000g limit are supported");
        Assert(service.Withdraw(1, wallet, largeAmount, "large-withdraw", 1).Status == TransactionStatus.Applied, "active withdrawals above the old 100,000,000g limit are supported");

        var termWallet = new TestWallet(largeAmount, 0);
        Assert(NewService().CreateTermDeposit(1, termWallet, 0, largeAmount, 0, "large-term", 0).Status == TransactionStatus.Applied, "term deposits above the old limit are supported");

        var loanConfig = new BankConfig { BaseCredit = 500_000_000m, AbsoluteCreditCap = 500_000_000m };
        var loanService = new BankService(loanConfig);
        Assert(loanService.RequestLoan(1, new TestWallet(0, 0), 0, 200_000_000, 0, "large-loan", 0).Status == TransactionStatus.Applied, "loans above the old limit are supported when credit and wallet capacity allow them");

        var disabledConfig = new BankConfig();
        disabledConfig.TermProducts[0].Enabled = false;
        var disabledService = new BankService(disabledConfig);
        Assert(disabledService.CreateTermDeposit(1, new TestWallet(1000, 0), 0, 1000, 0, "disabled-term", 0).Message == "term-product-invalid", "unchecked product slots cannot create new contracts");
    }

    private static void TestDiagnosticsGate()
    {
        var enabled = false;
        var messages = new List<string>();
        var service = new BankService(new BankConfig(), new BankState(), () => enabled, messages.Add);
        var wallet = new TestWallet(10, 0);
        Assert(service.Deposit(1, wallet, 5, "diag-off", 0).Status == TransactionStatus.Applied && messages.Count == 0, "disabled diagnostics do not emit core transaction messages");

        enabled = true;
        Assert(service.Withdraw(1, wallet, 1, "diag-on", 1).Status == TransactionStatus.Applied, "transactions remain functional with diagnostics enabled");
        Assert(messages.Exists(x => x.Contains("core-transaction: begin") && x.Contains("operation=withdraw"))
            && messages.Exists(x => x.Contains("core-transaction: applied") && x.Contains("operation=withdraw")), "enabled diagnostics emit the begin and applied ends of the transaction chain");

        var emitted = messages.Count;
        enabled = false;
        service.Snapshot(1, wallet);
        Assert(messages.Count == emitted, "turning diagnostics off stops subsequent snapshot messages");
    }

    private static TermDeposit NewLargeTerm(string id)
    {
        return new TermDeposit
        {
            Id = id,
            Principal = 1,
            Balance = long.MaxValue / 2 + 1,
            TermDays = 7,
            LockedTermRatePercent = 0.1m,
            LockedActivityRatePercent = 0.01m,
            OpenDay = 0,
            MaturityDay = 7,
            DaysSettled = 0
        };
    }
}
