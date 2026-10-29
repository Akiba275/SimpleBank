using System;
using System.Collections.Generic;
using SimpleBank.Core;

public static partial class BankTests
{
    private static BankService FundedActiveService(long balance = 10000, decimal remainder = 0.4m)
    {
        return new BankService(new BankConfig(), new BankState
        {
            LastSettlementDay = 0,
            SharedAccount = new Account { Id = "shared", ActiveBalance = balance, ActiveInterestRemainder = remainder }
        });
    }

    private static void TestActiveTermFunding()
    {
        var service = FundedActiveService();
        var result = service.CreateTermDepositFromActive(1, 2, 6000, 0, "active-part", 0);
        var account = service.State.SharedAccount;
        Assert(result.Status == TransactionStatus.Applied && result.NewRevision == 1 && service.State.ProcessedTransactionIds.Count == 1,
            "active funding commits exactly one revision and transaction ID");
        Assert(account.ActiveBalance == 4000 && account.ActiveInterestRemainder == 0.4m && account.TermDeposits.Count == 1,
            "active funding transfers only whole gold and preserves fractional active interest");
        var term = account.TermDeposits[0];
        Assert(term.Principal == 6000 && term.Balance == 6000 && term.InterestRemainder == 0m && term.DaysSettled == 0,
            "new active-funded term starts with principal only and no immediate interest");
        Assert(term.TermDays == 28 && term.MaturityDay == 28 && term.LockedTermRatePercent == 0.020m && term.LockedActivityRatePercent == 0.005m,
            "active-funded contracts lock the selected term and early-withdrawal rates");
        Assert(service.CreateTermDepositFromActive(1, 2, 6000, 0, "active-part", 1).Status == TransactionStatus.Duplicate,
            "replayed active funding does not debit again");
        Assert(service.CreateTermDepositFromActive(2, 0, 4000, 0, "shared-stale", 0).Status == TransactionStatus.StaleRevision,
            "competing shared-account request with an older revision is rejected");
        Assert(service.CreateTermDepositFromActive(2, 0, 4000, 0, "shared-all", 1).Status == TransactionStatus.Applied,
            "another shared-account player can fund a term from the same current balance");
        Assert(account.ActiveBalance == 0 && account.ActiveInterestRemainder == 0.4m && account.TermDeposits.Count == 2,
            "transferring all available whole gold retains the active remainder");
        Assert(service.CreateTermDepositFromActive(1, 0, 1, 0, "active-empty", 2).Message == "active-insufficient",
            "fractional interest alone is not spendable");
        Assert(service.SettleToDay(1, "active-next-day", 2).Status == TransactionStatus.Applied
            && service.State.SharedAccount.ActiveInterestRemainder == 0.4m
            && service.State.SharedAccount.TermDeposits[0].DaysSettled == 1,
            "terms begin daily accrual on the following day without rounding the retained active remainder");
        var reloaded = new BankService(service.Config, service.State.Clone());
        Assert(reloaded.State.SchemaVersion == 3 && BankStateValidator.Validate(reloaded.State).Count == 0
            && reloaded.State.SharedAccount.TermDeposits.Count == 2 && reloaded.State.SharedAccount.ActiveInterestRemainder == 0.4m,
            "active-funded contracts remain valid schema-3 data after state reload");

        var walletService = FundedActiveService();
        var wallet = new TestWallet(6000, 123);
        Assert(walletService.CreateTermDeposit(1, wallet, 2, 6000, 0, "wallet-term", 0).Status == TransactionStatus.Applied
            && wallet.Balance == 0 && wallet.HistoricalTotalEarnings == 123 && walletService.State.SharedAccount.ActiveBalance == 10000,
            "wallet funding retains its original single-source behavior");
        var walletTerm = walletService.State.SharedAccount.TermDeposits[0];
        Assert(walletTerm.TermDays == term.TermDays && walletTerm.LockedTermRatePercent == term.LockedTermRatePercent
            && walletTerm.LockedActivityRatePercent == term.LockedActivityRatePercent,
            "both funding sources use the same contract terms");
        Assert(walletService.CreateTermDeposit(1, wallet, 0, 1, 0, "no-wallet-fallback", 1).Message == "wallet-insufficient"
            && walletService.State.SharedAccount.ActiveBalance == 10000,
            "insufficient wallet funds never fall back to active savings");

        var large = FundedActiveService((long)int.MaxValue + 123);
        Assert(large.CreateTermDepositFromActive(1, 0, (long)int.MaxValue + 123, 0, "large-active", 0).Status == TransactionStatus.Applied
            && large.State.SharedAccount.ActiveBalance == 0,
            "direct active funding is not constrained by the game's wallet capacity");
    }

    private static void TestActiveTermFailuresAndIsolation()
    {
        var service = FundedActiveService();
        foreach (var amount in new long[] { 0, -1, 10001 })
        {
            var result = service.CreateTermDepositFromActive(1, 0, amount, 0, "invalid-amount", 0);
            Assert(result.Status == TransactionStatus.Rejected && result.Message == (amount <= 0 ? "amount-must-be-positive" : "active-insufficient")
                && service.State.SharedAccount.ActiveBalance == 10000 && service.State.SharedAccount.TermDeposits.Count == 0
                && service.State.Revision == 0 && service.State.ProcessedTransactionIds.Count == 0,
                "invalid active funding leaves balances, contracts, revision and transaction IDs unchanged");
        }
        Assert(service.CreateTermDepositFromActive(1, 0, 1, 0, "", 0).Message == "transaction-id-required", "active funding requires a transaction ID");
        Assert(service.CreateTermDepositFromActive(1, 99, 1, 0, "bad-product", 0).Message == "term-product-invalid", "invalid term index is rejected");
        var config = service.Config;
        config.TermProducts[0].Enabled = false;
        service.UpdateConfig(config);
        Assert(service.CreateTermDepositFromActive(1, 0, 1, 0, "disabled-term", 0).Message == "term-product-invalid", "disabled products reject active funding");
        Assert(service.CreateTermDepositFromActive(1, 1, 1, int.MaxValue, "date-overflow", 0).Message == "amount-overflow"
            && service.State.SharedAccount.ActiveBalance == 10000, "date overflow cannot consume active funds");
        service.State.DataInvalid = true;
        Assert(service.CreateTermDepositFromActive(1, 1, 1, 0, "locked-save", 0).Message == "save-data-invalid", "save protection applies to active funding");
        service.State.DataInvalid = false;
        service.State.ModeMismatch = true;
        Assert(service.CreateTermDepositFromActive(1, 1, 1, 0, "locked-mode", 0).Message == "wallet-mode-mismatch", "wallet-mode reconciliation lock applies to active funding");

        var personalState = new BankState { WalletMode = WalletMode.Personal };
        personalState.PersonalAccounts[11] = new Account { Id = "player-11", ActiveBalance = 100 };
        personalState.PersonalAccounts[22] = new Account { Id = "player-22", ActiveBalance = 200 };
        var personal = new BankService(new BankConfig(), personalState);
        Assert(personal.CreateTermDepositFromActive(11, 0, 1, 0, "one-gold", 0).Status == TransactionStatus.Applied
            && personal.State.PersonalAccounts[11].ActiveBalance == 99 && personal.State.PersonalAccounts[22].ActiveBalance == 200
            && personal.State.PersonalAccounts[22].TermDeposits.Count == 0, "one-gold personal funding affects only the requesting account");
        Assert(personal.CreateTermDepositFromActive(33, 0, 1, 0, "missing-personal", 1).Message == "active-insufficient"
            && !personal.State.PersonalAccounts.ContainsKey(33), "an absent personal account behaves as an empty active balance");

        var full = FundedActiveService();
        for (var i = 0; i < BankLimits.MaximumTermDepositsPerAccount; i++)
            full.State.SharedAccount.TermDeposits.Add(new TermDeposit { Id = "full-" + i, Principal = 1, Balance = 1, TermDays = 7, MaturityDay = 7 });
        Assert(full.CreateTermDepositFromActive(1, 0, 1, 0, "full", 0).Message == "term-limit-reached"
            && full.State.SharedAccount.ActiveBalance == 10000 && full.State.SharedAccount.TermDeposits.Count == 100,
            "the open-contract limit applies atomically to active funding");
        var overflow = FundedActiveService();
        overflow.State.SharedAccount.TermDeposits.Add(new TermDeposit { Id = "max", Principal = long.MaxValue, Balance = long.MaxValue, TermDays = 7, MaturityDay = 7 });
        Assert(overflow.CreateTermDepositFromActive(1, 0, 1, 0, "aggregate-overflow", 0).Message == "amount-overflow"
            && overflow.State.SharedAccount.ActiveBalance == 10000 && BankStateValidator.Validate(overflow.State).Count == 0,
            "funding cannot create an unpersistable aggregate term balance");

        var rollback = FundedActiveService();
        rollback.State.Revision = long.MaxValue;
        for (var i = 0; i < 256; i++) rollback.State.ProcessedTransactionIds.Add("previous-" + i);
        Assert(rollback.CreateTermDepositFromActive(1, 0, 6000, 0, "revision-overflow", long.MaxValue).Message == "amount-overflow",
            "a commit failure after debit and contract creation is reported");
        Assert(rollback.State.SharedAccount.ActiveBalance == 10000 && rollback.State.SharedAccount.ActiveInterestRemainder == 0.4m
            && rollback.State.SharedAccount.TermDeposits.Count == 0 && rollback.State.Revision == long.MaxValue
            && rollback.State.ProcessedTransactionIds.Count == 256 && rollback.State.ProcessedTransactionIds[0] == "previous-0"
            && !rollback.State.ProcessedTransactionIds.Contains("revision-overflow"),
            "commit failure restores the debit, contract, remainder, revision and pruned transaction history");

        var logs = new List<string>();
        var enabled = false;
        var diagnosticService = new BankService(new BankConfig(), FundedActiveService().State, () => enabled, logs.Add);
        diagnosticService.CreateTermDepositFromActive(1, 0, 1, 0, "quiet", 0);
        Assert(logs.Count == 0, "active-funding diagnostics remain silent when disabled");
        enabled = true;
        diagnosticService.CreateTermDepositFromActive(1, 0, 1, 0, "logged", 1);
        Assert(logs.Exists(x => x.Contains("term-active") && x.Contains("logged") && x.Contains("Applied")),
            "enabled diagnostics retain operation, transaction ID and outcome");
    }

    private static void TestActiveTermSettlementAndCollection()
    {
        var service = FundedActiveService(1000);
        service.CreateTermDepositFromActive(1, 0, 1000, 0, "maturing", 0);
        Assert(service.SettleToDay(7, "maturity", 1).Status == TransactionStatus.Applied
            && service.State.SharedAccount.ActiveBalance == 1001 && service.State.SharedAccount.ActiveInterestRemainder == 0.4m
            && service.State.SharedAccount.TermDeposits.Count == 0, "active-funded maturity pays into active with the usual final rounding");
        var early = FundedActiveService(10000, 0m);
        early.CreateTermDepositFromActive(1, 0, 10000, 0, "early-open", 0);
        var id = early.State.SharedAccount.TermDeposits[0].Id;
        early.SettleToDay(2, "two-days", 1);
        var wallet = new TestWallet(0, 777);
        Assert(early.WithdrawTermEarly(1, wallet, id, 2, "early-close", 2).Status == TransactionStatus.Applied
            && early.State.SharedAccount.ActiveBalance == 10001 && early.State.SharedAccount.TermDeposits.Count == 0
            && wallet.Balance == 0 && wallet.HistoricalTotalEarnings == 777,
            "early closure uses completed-day active interest and returns to active rather than the wallet");

        var config = new BankConfig { OverdueGraceDays = 0, DefaultAfterDays = 1 };
        config.LoanProducts[0].Days = 1;
        var collection = new BankService(config, FundedActiveService().State);
        collection.RequestLoan(1, wallet, 0, 1000, 0, "collection-loan", 0);
        collection.CreateTermDepositFromActive(1, 0, 1000, 0, "before-collection", 1);
        var beforeCollectionId = collection.State.SharedAccount.TermDeposits[0].Id;
        collection.SettleToDay(2, "collection-day", 2);
        foreach (var day in new[] { 2, 3 })
        {
            collection.SettleToDay(day, "advance-" + day, collection.State.Revision);
            var revision = collection.State.Revision;
            var balance = collection.State.SharedAccount.ActiveBalance;
            Assert(collection.CreateTermDepositFromActive(1, 0, 1, day, "blocked-active-" + day, revision).Message == "collection-new-term-blocked"
                && collection.CreateTermDeposit(1, wallet, 0, 1, day, "blocked-wallet-" + day, revision).Message == "collection-new-term-blocked"
                && collection.State.Revision == revision && collection.State.SharedAccount.ActiveBalance == balance,
                "both collection and default block both funding sources without changes");
        }
        Assert(collection.WithdrawTermEarly(1, wallet, beforeCollectionId, 3, "early-to-debt", collection.State.Revision).Message == "term-closed-early-to-overdue-loan"
            && collection.State.SharedAccount.TermDeposits.Count == 0 && collection.State.SharedAccount.Loan.Principal < 1000,
            "early closure of an active-funded term still prioritizes compulsory debt repayment");
    }
}
