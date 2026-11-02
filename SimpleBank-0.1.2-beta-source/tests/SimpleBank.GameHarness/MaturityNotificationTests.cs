using System;
using System.Linq;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;
using SimpleBank;
using SimpleBank.Core;

public static partial class Program
{
    private static void TestMaturityNotifications()
    {
        foreach (var personal in new[] { false, true })
        foreach (var locale in new[] { "default", "zh" })
        {
            var f = new FundingFixture(personal, locale);
            var config = f.Bank.Config;
            config.ActivityDailyRatePercent = 0m;
            f.Bank.UpdateConfig(config);
            f.Bank.CreateTermDepositFromActive(101, 0, 1000, 10, "host-term", f.Bank.State.Revision);
            f.Bank.CreateTermDepositFromActive(202, 0, 2000, 10, "remote-term", f.Bank.State.Revision);
            f.Bank.RequestLoan(101, new GameWalletAdapter(f.Host, f.Bank.State.WalletMode, f.Diagnostics), 0, 1000, 10, "host-loan", f.Bank.State.Revision);
            if (personal)
                f.Bank.RequestLoan(202, new GameWalletAdapter(f.Remote, f.Bank.State.WalletMode, f.Diagnostics), 0, 2000, 10, "remote-loan", f.Bank.State.Revision);

            var notifiers = new PerScreen<MaturityNotifier>(() => new MaturityNotifier(f.Text, f.Diagnostics));
            var hostNotifier = notifiers.Value;
            Game1.HudMessages.Clear();
            Game1.stats.DaysPlayed = 16;
            var before = f.Bank.SettleToDay(16, "before-due", f.Bank.State.Revision);
            f.HostCoordinator.RecordTermMaturities(before, 16);
            hostNotifier.Check(f.Bank, 101, 16, f.HostCoordinator.GetLocalTermMaturities(16));
            Assert(Game1.HudMessages.Count == 0, "no deposit or loan notification before maturity: " + locale);

            Game1.stats.DaysPlayed = 17;
            hostNotifier.Check(f.Bank, 101, 17, f.HostCoordinator.GetLocalTermMaturities(17));
            Assert(Game1.HudMessages.Count == 0, "notifications wait for the host to settle today's interest");
            var result = f.Bank.SettleToDay(17, "due", f.Bank.State.Revision);
            f.HostCoordinator.RecordTermMaturities(result, 17);
            hostNotifier.Check(f.Bank, 101, 17, f.HostCoordinator.GetLocalTermMaturities(17));
            Assert(Game1.HudMessages.Count == 2 && Game1.HudMessages[0].Message == f.Text.Get("message.term-matured",
                    new { count = personal ? 1 : 2 })
                && Game1.HudMessages[1].Message == f.Text.Get("message.loan-due-today"),
                "maturity HUD shows the localized deposit count and loan repayment reminder: " + locale);
            var revision = f.Bank.State.Revision;
            var active = f.Bank.GetAccount(101).ActiveBalance;
            f.HostCoordinator.RecordTermMaturities(result, 17);
            f.HostCoordinator.RecordTermMaturities(f.Bank.SettleToDay(17, "due", revision), 17);
            hostNotifier.Check(f.Bank, 101, 17, f.HostCoordinator.GetLocalTermMaturities(17));
            Assert(Game1.HudMessages.Count == 2 && f.Bank.State.Revision == revision && f.Bank.GetAccount(101).ActiveBalance == active,
                "repeated checks, receipts and duplicate settlement never repeat HUD or mutate finances");

            // Model a late join: the client never saw the deposits before maturity.
            f.HostCoordinator.SendSnapshotTo(202);
            var sent = f.HostWire.Messages.Last();
            var snapshot = (BankSyncMessage)sent.Payload;
            Assert(sent.Recipients.Single() == 202 && snapshot.TermMaturities.Count == (personal ? 1 : 2)
                && snapshot.TermMaturities.All(receipt => receipt.PlayerId == (personal ? (long?)202 : null)),
                "maturity receipts are targeted and filtered by stable account owner");
            var clientBank = new BankService(config, new BankState());
            var clientWire = new FundingHelper(f.Text);
            var client = new MultiplayerCoordinator(clientWire, f.Monitor, f.Diagnostics);
            f.UseClient();
            var clientNotifier = notifiers.Value;
            client.Attach(clientBank);
            client.BankStateChanged += () => clientNotifier.Check(clientBank, client.LocalPlayerId,
                Game1.stats.DaysPlayed, client.GetLocalTermMaturities(Game1.stats.DaysPlayed));
            Game1.HudMessages.Clear();
            // A host snapshot can arrive before this screen advances its local date.
            Game1.stats.DaysPlayed = 16;
            DeliverFundingMessage(client, 101, sent);
            Assert(Game1.HudMessages.Count == 0, "a snapshot arriving before the local date is ready does not notify early");
            Game1.stats.DaysPlayed = 17;
            clientNotifier.Check(clientBank, client.LocalPlayerId, 17, client.GetLocalTermMaturities(17));
            Assert(Game1.HudMessages.Count == 2
                && Game1.HudMessages[0].Message == f.Text.Get("message.term-matured", new { count = personal ? 1 : 2 })
                && Game1.HudMessages[1].Message == f.Text.Get("message.loan-due-today"),
                "late-joining clients and separate local screens receive their own maturity notifications");
            DeliverFundingMessage(client, 101, sent);
            Assert(Game1.HudMessages.Count == 2, "repeated same-day snapshots are deduplicated per screen");

            var otherClientBank = new BankService(config, new BankState());
            var otherClient = new MultiplayerCoordinator(new FundingHelper(f.Text), f.Monitor, f.Diagnostics);
            otherClient.Attach(otherClientBank);
            DeliverFundingMessage(otherClient, 999, sent);
            Assert(!otherClient.GetLocalTermMaturities(17).Any() && otherClientBank.State.Revision == 0,
                "a non-host cannot inject maturity receipts or state");
            client.RecordTermMaturities(result, 17);
            Assert(client.GetLocalTermMaturities(17).Count() == (personal ? 1 : 2), "clients cannot publish host settlement receipts");

            // Reconnect in the same day receives the host's retained receipts again.
            client.Detach();
            clientNotifier.Reset();
            Assert(!client.GetLocalTermMaturities(17).Any(), "client teardown clears only its transient receipts");
            f.UseHost();
            Assert(f.HostCoordinator.GetLocalTermMaturities(17).Count() == (personal ? 1 : 2)
                && ReferenceEquals(hostNotifier, notifiers.Value) && f.Bank.State.Revision == revision,
                "farmhand teardown preserves the host's receipts, notifier and authoritative state");
            DeliverFundingMessage(f.HostCoordinator, 202, sent);
            Assert(f.Bank.State.Revision == revision, "the host never applies a client maturity snapshot");
            f.HostCoordinator.SendSnapshotTo(202);
            var reconnectSnapshot = f.HostWire.Messages.Last();
            f.UseClient();
            client.Attach(clientBank);
            Game1.HudMessages.Clear();
            DeliverFundingMessage(client, 101, reconnectSnapshot);
            Assert(Game1.HudMessages.Count == 2, "same-day reconnect replays both notices once in the new session");

            // A settled/cancelled loan cannot generate a due-today reminder on reload.
            foreach (var status in new[] { LoanStatus.Settled, LoanStatus.Cancelled })
            {
                clientBank.GetAccount(202).Loan.Status = status;
                clientNotifier.Reset();
                Game1.HudMessages.Clear();
                clientNotifier.Check(clientBank, 202, 17, Array.Empty<TermMaturity>());
                Assert(Game1.HudMessages.Count == 0, "resolved loans do not receive due-day reminders: " + status);
            }
            f.UseHost();
            Game1.stats.DaysPlayed = 18;
            var tomorrow = f.Bank.SettleToDay(18, "tomorrow", f.Bank.State.Revision);
            f.HostCoordinator.RecordTermMaturities(tomorrow, 18);
            hostNotifier.Check(f.Bank, 101, 18, f.HostCoordinator.GetLocalTermMaturities(18));
            Assert(Game1.HudMessages.Count == 0 && !f.HostCoordinator.GetLocalTermMaturities(17).Any()
                && f.Bank.GetAccount(101).Loan.Status == LoanStatus.Overdue,
                "old maturity notices expire next day while the original overdue transition remains intact");
            f.HostCoordinator.SendSnapshotTo(202);
            Assert(((BankSyncMessage)f.HostWire.Messages.Last().Payload).TermMaturities.Count == 0,
                "later snapshots never announce yesterday's deposit as maturing today");
            Assert(f.Host.totalMoneyEarned == 111 && f.Remote.totalMoneyEarned == 222 && f.Team.totalMoneyEarned.Value == 333,
                "maturity notification flow preserves historical earnings");
        }
        Context.ScreenId = 0;
        Context.IsMainPlayer = true;
        Game1.HudMessages.Clear();
    }
}
