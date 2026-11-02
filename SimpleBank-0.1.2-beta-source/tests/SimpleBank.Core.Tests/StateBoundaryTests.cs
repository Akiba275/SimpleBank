using System;
using System.Linq;
using System.Text.Json;
using SimpleBank.Core;

public static partial class BankTests
{
    private static void TestStateEntryValidation()
    {
        var service = new BankService(new BankConfig(), new BankState { Revision = 4 });
        service.State.SharedAccount.ActiveBalance = 345;
        var original = service.State;
        var originalJson = JsonSerializer.Serialize(original);
        var corruptions = new Action<BankState>[]
        {
            s => s.SharedAccount = null,
            s => s.PersonalAccounts = null,
            s => s.PersonalAccounts[9] = null,
            s => s.SharedAccount.Id = " ",
            s => s.SharedAccount.TermDeposits = null,
            s => s.SharedAccount.TermDeposits.Add(null),
            s => s.SharedAccount.TermDeposits.Add(new TermDeposit { Id = " " }),
            s => s.SharedAccount.CreditRestriction = null,
            s => s.ProcessedTransactionIds = null,
            s => s.ProcessedTransactionIds.Add(" "),
            s => s.ProcessedTransactionIds.AddRange(new[] { "duplicate", "duplicate" }),
            s => s.ProcessedTransactionIds.AddRange(Enumerable.Range(0, 257).Select(i => "tx-" + i))
        };
        foreach (var corrupt in corruptions)
        {
            var invalid = new BankState();
            corrupt(invalid);
            var before = JsonSerializer.Serialize(invalid);
            var constructorRejected = false;
            try { new BankService(new BankConfig(), invalid); }
            catch (ArgumentException) { constructorRejected = true; }
            Assert(constructorRejected && JsonSerializer.Serialize(invalid) == before,
                "construction rejects malformed state without repairing or discarding input data");
            var replacementRejected = false;
            try { service.ReplaceState(invalid); }
            catch (ArgumentException) { replacementRejected = true; }
            Assert(replacementRejected && ReferenceEquals(service.State, original)
                && JsonSerializer.Serialize(service.State) == originalJson && JsonSerializer.Serialize(invalid) == before,
                "invalid state replacement preserves both the active state and rejected input");
        }

        var valid = new BankState { WalletMode = WalletMode.Personal, Revision = 7 };
        valid.PersonalAccounts[9] = new Account { Id = "player-9", ActiveBalance = 543, ActiveInterestRemainder = 0.75m };
        valid.ProcessedTransactionIds.Add("accepted");
        var expected = JsonSerializer.Serialize(valid);
        Assert(JsonSerializer.Serialize(new BankService(new BankConfig(), valid).State) == expected,
            "construction preserves all valid state values");
        service.ReplaceState(valid);
        Assert(JsonSerializer.Serialize(service.State) == expected && !ReferenceEquals(service.State, valid)
            && !ReferenceEquals(service.State.PersonalAccounts[9], valid.PersonalAccounts[9]),
            "valid replacement preserves values while retaining defensive cloning");
        valid.PersonalAccounts[9].ActiveBalance = 0;
        valid.ProcessedTransactionIds.Clear();
        Assert(JsonSerializer.Serialize(service.State) == expected, "later changes to replacement input cannot mutate the active state");
    }

    private static void TestLoanPhaseEntryBoundaries()
    {
        var days = new[] { 17, 18, 26, 27, 29, 30, 31 };
        var enabledStatuses = new[] { LoanStatus.Current, LoanStatus.Overdue, LoanStatus.Overdue,
            LoanStatus.Collection, LoanStatus.Collection, LoanStatus.Defaulted, LoanStatus.Defaulted };
        foreach (var enabled in new[] { false, true })
        for (var index = 0; index < days.Length; index++)
        {
            var day = days[index];
            var expected = enabled ? enabledStatuses[index] : index == 0 ? LoanStatus.Current : LoanStatus.Overdue;
            var config = new BankConfig { EnableOverdueCollection = enabled, OverdueGraceDays = 9, DefaultAfterDays = 3 };
            var legacy = new BankState { SchemaVersion = 2, LastSettlementDay = day };
            legacy.SharedAccount.Loan = new LoanContract
            {
                Id = "legacy", Principal = 1000, TermDays = 7, LockedRatePercent = 0.15m,
                OverdueMultiplier = 1.5m, OpenDay = 10, MaturityDay = 17, LastSettledDay = day,
                Status = LoanStatus.Current
            };
            BankState migrated;
            Assert(BankStateMigrator.TryMigrateToCurrent(legacy, config, day, out migrated)
                && migrated.SharedAccount.Loan.Status == expected && BankStateValidator.Validate(migrated).Count == 0,
                "schema migration derives the expected loan phase at day " + day + ", collection=" + enabled);

            var service = new BankService(config, new BankState { LastSettlementDay = 10 });
            var wallet = new TestWallet(0, 100000);
            Assert(service.RequestLoan(1, wallet, 0, 1000, 10, "loan", 0).Status == TransactionStatus.Applied
                && service.SettleToDay(day, "day", service.State.Revision).Status == TransactionStatus.Applied
                && service.State.SharedAccount.Loan.Status == expected,
                "daily settlement derives the expected loan phase at day " + day + ", collection=" + enabled);
        }

        foreach (var cancelled in new[] { false, true })
        {
            var service = new BankService(new BankConfig(), new BankState { LastSettlementDay = 10 });
            var wallet = new TestWallet(0, 100000);
            service.RequestLoan(1, wallet, 0, 1000, 10, "loan", 0);
            var result = cancelled ? service.CancelDebt(1, 10, "resolve", service.State.Revision)
                : service.RepayLoan(1, wallet, 1000, "resolve", service.State.Revision);
            Assert(result.Status == TransactionStatus.Applied, "terminal loan fixture resolves through the public transaction path");
            var expected = JsonSerializer.Serialize(service.State.SharedAccount.Loan);
            Assert(service.SettleToDay(60, "later", service.State.Revision).Status == TransactionStatus.Applied
                && JsonSerializer.Serialize(service.State.SharedAccount.Loan) == expected,
                "daily settlement never reactivates or accrues interest on a resolved loan");
        }

        var settledLegacy = new BankState { SchemaVersion = 2, LastSettlementDay = 20 };
        settledLegacy.SharedAccount.Loan = new LoanContract
        {
            Id = "settled-legacy", Principal = 0, TermDays = 7, OpenDay = 10,
            MaturityDay = 17, LastSettledDay = 20, OverdueMultiplier = 1.5m, Status = LoanStatus.Settled
        };
        BankState settled;
        Assert(BankStateMigrator.TryMigrateToCurrent(settledLegacy, new BankConfig(), 60, out settled)
            && settled.SharedAccount.Loan.Status == LoanStatus.Settled
            && settled.SharedAccount.Loan.Resolution == LoanResolutionKind.Repaid
            && BankStateValidator.Validate(settled).Count == 0,
            "migration preserves an already settled legacy loan rather than deriving an open phase");
    }
}
