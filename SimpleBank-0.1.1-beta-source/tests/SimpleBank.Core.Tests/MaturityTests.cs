using System;
using System.Linq;
using SimpleBank.Core;

public static partial class BankTests
{
    private static void TestTermMaturityReceipts()
    {
        var config = new BankConfig { ActivityDailyRatePercent = 0m };
        config.TermProducts[0].Days = 1;
        config.TermProducts[0].DailyRatePercent = 0.1m;
        foreach (var personal in new[] { false, true })
        {
            var state = new BankState { LastSettlementDay = 0, WalletMode = personal ? WalletMode.Personal : WalletMode.Shared };
            if (personal) state.PersonalAccounts[123] = new Account { Id = "player-123", ActiveBalance = 1500 };
            else state.SharedAccount.ActiveBalance = 1500;
            var bank = new BankService(config, state);
            bank.CreateTermDepositFromActive(123, 0, 500, 0, "term-one", bank.State.Revision);
            bank.CreateTermDepositFromActive(123, 0, 1000, 0, "term-two", bank.State.Revision);
            var result = bank.SettleToDay(1, "maturity", bank.State.Revision);
            Assert(result.Status == TransactionStatus.Applied && result.TermMaturities.Count == 2,
                "only committed term maturities produce settlement receipts");
            Assert(result.TermMaturities[0].Amount == 501 && result.TermMaturities[1].Amount == 1001
                && result.TermMaturities.All(receipt => receipt.Day == 1 && receipt.PlayerId == (personal ? (long?)123 : null)),
                "receipts carry actual final rounding, settlement day and stable account ownership");
            Assert(bank.GetAccount(123).ActiveBalance == 1502 && bank.GetAccount(123).TermDeposits.Count == 0
                && bank.State.SchemaVersion == 3 && BankStateValidator.Validate(bank.State).Count == 0,
                "receipt generation preserves maturity balances and schema-3 validation");
            Assert(bank.SettleToDay(1, "maturity", bank.State.Revision).TermMaturities.Count == 0
                && bank.SettleToDay(1, "same-day", bank.State.Revision).TermMaturities.Count == 0,
                "duplicate settlement IDs and same-day settlement with new IDs produce no extra receipts");
        }

        var catchup = new BankService(config, new BankState { LastSettlementDay = 0,
            SharedAccount = new Account { Id = "shared", ActiveBalance = 1000 } });
        catchup.CreateTermDepositFromActive(1, 0, 1000, 0, "catchup-term", 0);
        var caughtUp = catchup.SettleToDay(3, "catchup", catchup.State.Revision);
        Assert(caughtUp.TermMaturities.Single().Day == 1 && caughtUp.TermMaturities.Single().Amount == 1001,
            "catch-up settlement preserves the original maturity day instead of labeling old deposits as due today");

        config.TermProducts[0].DailyRatePercent = 0m;
        var rollback = new BankService(config, new BankState { LastSettlementDay = 0,
            SharedAccount = new Account { Id = "shared", ActiveBalance = 2000 } });
        rollback.CreateTermDepositFromActive(1, 0, 1000, 0, "rollback-one", 0);
        rollback.CreateTermDepositFromActive(1, 0, 1000, 0, "rollback-two", 1);
        rollback.State.SharedAccount.ActiveBalance = long.MaxValue - 1500;
        var rejected = rollback.SettleToDay(1, "rollback", rollback.State.Revision);
        Assert(rejected.Status == TransactionStatus.Rejected && rejected.TermMaturities.Count == 0
            && rollback.State.SharedAccount.TermDeposits.Count == 2 && rollback.State.LastSettlementDay == 0
            && rollback.State.SharedAccount.ActiveBalance == long.MaxValue - 1500,
            "overflow after an earlier maturity rolls back all balances and discards every receipt");

        var early = new BankService(config, new BankState { LastSettlementDay = 0,
            SharedAccount = new Account { Id = "shared", ActiveBalance = 1000 } });
        early.CreateTermDepositFromActive(1, 0, 1000, 0, "early-term", 0);
        early.WithdrawTermEarly(1, new TestWallet(0, 0), early.State.SharedAccount.TermDeposits[0].Id, 0, "early", 1);
        Assert(early.SettleToDay(1, "early-day", early.State.Revision).TermMaturities.Count == 0,
            "a deposit withdrawn early never produces a maturity receipt");
    }
}
