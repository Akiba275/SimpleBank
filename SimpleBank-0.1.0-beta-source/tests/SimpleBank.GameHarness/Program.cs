using System;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;
using SimpleBank;
using SimpleBank.Core;

public static partial class Program
{
    private static int passed;

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        passed++;
    }

    public static int Main()
    {
        try { return RunTests(); }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static int RunTests()
    {
        var banks = new PerScreen<BankService>(() => null);

        Context.ScreenId = 0;
        banks.Value = new BankService(new BankConfig(), new BankState
        {
            WalletMode = WalletMode.Shared,
            SharedAccount = new Account { Id = "shared", ActiveBalance = 10000 }
        });
        var hostBank = banks.Value;

        Context.ScreenId = 1;
        Assert(banks.Value == null, "a split-screen farmhand does not reuse the host BankService");
        banks.Value = new BankService(new BankConfig(), new BankState { WalletMode = WalletMode.Personal });
        Assert(!object.ReferenceEquals(hostBank, banks.Value), "host and farmhand BankService instances are isolated");

        banks.Value = null;
        Assert(banks.Value == null, "returning the farmhand screen to title clears only that screen");

        Context.ScreenId = 0;
        Assert(object.ReferenceEquals(hostBank, banks.Value) && banks.Value.State.SharedAccount.ActiveBalance == 10000,
            "farmhand teardown preserves the host BankService and authoritative balance");

        TestCollectionLifecycleAndReconnect();
        TestDelayedPeerBindingSweep();
        TestIncomeSamplingAndStableIdRebind();
        TestSharedWalletUsesOneObserver();
        TestTermFundingMenu();
        TestTermFundingMultiplayer();

        Console.WriteLine("PASS: " + passed + " game-layer lifecycle assertions; game layer compiled.");
        return 0;
    }

    private static void TestDelayedPeerBindingSweep()
    {
        var team = new FarmerTeam();
        team.useSeparateWallets.Value = true;
        var host = new Farmer { UniqueMultiplayerID = 31, team = team };
        var remote = new Farmer { UniqueMultiplayerID = 32, team = team, totalMoneyEarned = 456 };
        team.SetIndividualMoney(host, 1000);
        team.SetIndividualMoney(remote, 2000);
        Game1.player = host;
        Game1.MasterPlayer = host;
        Game1.stats.DaysPlayed = 2;
        Game1.OnlineFarmers.Clear();
        Game1.OnlineFarmers.Add(host);

        var state = new BankState { WalletMode = WalletMode.Personal, LastSettlementDay = 2 };
        state.PersonalAccounts[32] = NewCollectionAccount("delayed-peer", 500, 0, null);
        var bank = new BankService(new BankConfig(), state);
        var monitor = new HarnessMonitor();
        var diagnostics = new ModDiagnostics(monitor, () => false);
        var collection = new OverdueCollectionCoordinator(monitor, diagnostics, new MultiplayerCoordinator(null, monitor, diagnostics));
        collection.Attach(bank);

        Assert(!collection.OnPeerConnected(32, 2) && !bank.State.PersonalAccounts[32].Loan.LastWalletSweepDay.HasValue,
            "a peer event that arrives before its Farmer object is visible is retained without sweeping the wrong wallet");
        Game1.OnlineFarmers.Add(remote);
        for (var tick = 0; tick < 60; tick++) collection.UpdateTicked(2);
        Assert(team.GetIndividualMoney(remote) == 1500 && bank.State.PersonalAccounts[32].Loan.Status == LoanStatus.Settled
            && remote.totalMoneyEarned == 456,
            "low-frequency stable-ID reconciliation completes one pending reconnect sweep after the Farmer object appears");
        collection.Detach();
    }

    private static void TestCollectionLifecycleAndReconnect()
    {
        Context.ScreenId = 0;
        Context.IsMainPlayer = true;
        Context.IsWorldReady = true;
        var team = new FarmerTeam();
        team.useSeparateWallets.Value = true;
        var host = new Farmer { UniqueMultiplayerID = 1, team = team, totalMoneyEarned = 111 };
        var remote = new Farmer { UniqueMultiplayerID = 2, team = team, totalMoneyEarned = 222 };
        team.SetIndividualMoney(host, 2000);
        team.SetIndividualMoney(remote, 2500);
        Game1.player = host;
        Game1.MasterPlayer = host;
        Game1.stats.DaysPlayed = 2;
        Game1.OnlineFarmers.Clear();
        Game1.OnlineFarmers.Add(host);

        var state = new BankState { WalletMode = WalletMode.Personal, LastSettlementDay = 2 };
        state.PersonalAccounts[1] = NewCollectionAccount("host", 1000, 300, null);
        state.PersonalAccounts[2] = NewCollectionAccount("remote", 1000, 300, null);
        var bank = new BankService(new BankConfig(), state);
        var monitor = new HarnessMonitor();
        var diagnostics = new ModDiagnostics(monitor, () => false);
        var multiplayer = new MultiplayerCoordinator(null, monitor, diagnostics);
        var collection = new OverdueCollectionCoordinator(monitor, diagnostics, multiplayer);
        collection.Attach(bank);

        Assert(team.GetIndividualMoney(host) == 2000 && bank.State.Revision == 0, "SaveLoaded-style attachment establishes baselines without sweeping stock wallet balances");
        Assert(collection.OnDayStarted(2), "DayStarted performs collection after the loan is already settled to the day");
        Assert(bank.State.PersonalAccounts[1].ActiveBalance == 100 && team.GetIndividualMoney(host) == 1200 && bank.State.PersonalAccounts[1].Loan.Status == LoanStatus.Settled,
            "online personal account uses active setoff first and then its own wallet reserve");
        Assert(bank.State.PersonalAccounts[2].ActiveBalance == 100 && bank.State.PersonalAccounts[2].Loan.Principal == 800 && !bank.State.PersonalAccounts[2].Loan.LastWalletSweepDay.HasValue,
            "offline personal account receives active setoff but does not falsely checkpoint or debit a wallet");
        var revision = bank.State.Revision;
        Assert(!collection.OnDayStarted(2) && bank.State.Revision == revision, "repeating DayStarted on the same date is financially and revision idempotent");

        Game1.OnlineFarmers.Add(remote);
        Assert(collection.OnPeerConnected(2, 2), "reconnect detects and runs a missing same-day wallet sweep");
        Assert(team.GetIndividualMoney(remote) == 1700 && bank.State.PersonalAccounts[2].Loan.Status == LoanStatus.Settled && bank.State.PersonalAccounts[2].ActiveBalance == 100,
            "reconnect sweep uses stable multiplayer ID and never repeats the completed active setoff");
        Assert(host.totalMoneyEarned == 111 && remote.totalMoneyEarned == 222, "scheduled collection preserves personal totalMoneyEarned values");
        collection.Detach();
    }

    private static void TestIncomeSamplingAndStableIdRebind()
    {
        var team = new FarmerTeam();
        team.useSeparateWallets.Value = true;
        var original = new Farmer { UniqueMultiplayerID = 77, team = team, totalMoneyEarned = 123 };
        team.SetIndividualMoney(original, 1000);
        Game1.player = original;
        Game1.MasterPlayer = original;
        Game1.OnlineFarmers.Clear();
        Game1.OnlineFarmers.Add(original);
        var state = new BankState { WalletMode = WalletMode.Personal, LastSettlementDay = 2 };
        state.PersonalAccounts[77] = NewCollectionAccount("income", 1000, 0, 2);
        var bank = new BankService(new BankConfig(), state);
        var monitor = new HarnessMonitor();
        var diagnostics = new ModDiagnostics(monitor, () => false);
        var collection = new OverdueCollectionCoordinator(monitor, diagnostics, new MultiplayerCoordinator(null, monitor, diagnostics));
        collection.Attach(bank);

        team.SetIndividualMoney(original, 1040);
        collection.UpdateTicked(2);
        Assert(team.GetIndividualMoney(original) == 1030 && bank.State.PersonalAccounts[77].Loan.Principal == 990,
            "tick sampler garnishes only the configured share of a positive wallet delta");
        Assert(original.totalMoneyEarned == 123, "income garnishment leaves the authoritative historical earnings field unchanged");
        var revision = bank.State.Revision;
        collection.UpdateTicked(2);
        Assert(bank.State.Revision == revision, "a tick with no positive delta performs no transaction or revision change");

        var replacement = new Farmer { UniqueMultiplayerID = 77, team = team, totalMoneyEarned = 123 };
        Game1.player = replacement;
        Game1.MasterPlayer = replacement;
        Game1.OnlineFarmers.Clear();
        Game1.OnlineFarmers.Add(replacement);
        Assert(!collection.OnPeerConnected(77, 2), "same-day rebind uses the stable ID without duplicating the wallet stock sweep");
        team.SetIndividualMoney(replacement, 1060);
        collection.UpdateTicked(2);
        Assert(team.GetIndividualMoney(replacement) == 1053 && bank.State.PersonalAccounts[77].Loan.Principal == 983,
            "replacement Farmer objects rebind safely while the stable multiplayer ID and debt remain unchanged");
        collection.Detach();
    }

    private static void TestSharedWalletUsesOneObserver()
    {
        var team = new FarmerTeam();
        var host = new Farmer { UniqueMultiplayerID = 901, team = team, totalMoneyEarned = 10 };
        var remote = new Farmer { UniqueMultiplayerID = 902, team = team, totalMoneyEarned = 20 };
        team.SetIndividualMoney(host, 1000);
        Game1.player = host;
        Game1.MasterPlayer = host;
        Game1.OnlineFarmers.Clear();
        Game1.OnlineFarmers.Add(host);
        Game1.OnlineFarmers.Add(remote);
        var state = new BankState { WalletMode = WalletMode.Shared, LastSettlementDay = 2 };
        state.SharedAccount = NewCollectionAccount("shared", 1000, 0, 2);
        state.SharedAccount.Loan.LockedMinimumWalletReserve = 0;
        state.SharedAccount.Loan.LockedIncomeGarnishmentPercent = 50m;
        var bank = new BankService(new BankConfig(), state);
        var monitor = new HarnessMonitor();
        var diagnostics = new ModDiagnostics(monitor, () => false);
        var collection = new OverdueCollectionCoordinator(monitor, diagnostics, new MultiplayerCoordinator(null, monitor, diagnostics));
        collection.Attach(bank);
        team.SetIndividualMoney(remote, 1100);
        collection.UpdateTicked(2);
        Assert(team.GetIndividualMoney(host) == 1050 && team.GetIndividualMoney(remote) == 1050 && bank.State.SharedAccount.Loan.Principal == 950,
            "shared-wallet income is observed and garnished once even when multiple farmers are online");
        Assert(team.totalMoneyEarned.Value == 0 && host.totalMoneyEarned == 10 && remote.totalMoneyEarned == 20,
            "shared collection preserves both shared and personal historical earnings counters");
        collection.Detach();
    }

    private static Account NewCollectionAccount(string id, long principal, long activeBalance, int? lastWalletSweepDay)
    {
        return new Account
        {
            Id = id,
            ActiveBalance = activeBalance,
            Loan = new LoanContract
            {
                Id = id + "-loan",
                OriginalPrincipal = principal,
                Principal = principal,
                TermDays = 1,
                LockedRatePercent = 0m,
                OverdueMultiplier = 1m,
                LockedCollectionEnabled = true,
                LockedGraceDays = 0,
                LockedActiveBalanceSetoff = true,
                LockedMinimumActiveBalanceReserve = 100,
                LockedMinimumWalletReserve = 1000,
                LockedIncomeGarnishmentPercent = 25m,
                LockedMaximumOverdueInterestPercent = 100m,
                LockedDefaultAfterDays = 3,
                OpenDay = 0,
                MaturityDay = 1,
                CollectionStartDay = 2,
                DefaultDay = 5,
                LastSettledDay = 2,
                LastWalletSweepDay = lastWalletSweepDay,
                Status = LoanStatus.Collection
            }
        };
    }

    private sealed class HarnessMonitor : IMonitor
    {
        public void Log(string message, LogLevel level = LogLevel.Trace) { }
    }
}
