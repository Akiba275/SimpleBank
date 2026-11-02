using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using SimpleBank;
using SimpleBank.Core;

public static partial class Program
{
    private static void TestWalletModePersistence()
    {
        foreach (var personal in new[] { false, true })
        foreach (var blocked in new[] { false, true })
        {
            var f = new FundingFixture(personal, "zh");
            if (blocked)
                Assert(f.Bank.CreateTermDepositFromActive(101, 0, 1000, 10, "blocking-term", 0).Status == TransactionStatus.Applied,
                    "wallet migration fixture opens a blocking contract");
            var originalJson = JsonSerializer.Serialize(f.Bank.State);
            var helper = new PersistenceHelper(f.Text, f.Bank.State);
            f.Team.useSeparateWallets.Value = !personal;
            f.Host.changeWalletTypeTonight.Value = true;
            Context.IsWorldReady = true;
            Game1.HudMessages.Clear();
            var mod = new ModEntry();
            mod.InitializeForTest(helper, f.Monitor);
            helper.EventsHub.Load();
            // Restoring the NetBool invokes a topology callback which saves the transient lock.
            // The outer load reconciliation then saves the cleared state once, not twice.
            var expectedWrites = blocked ? 2 : 1;
            Assert(helper.Writes.Count == expectedWrites && helper.Writes.Count(s => !s.ModeMismatch) == 1,
                "load reconciliation preserves the callback save and writes its final state once, personal=" + personal
                + ", blocked=" + blocked + ", writes=" + helper.Writes.Count
                + ", states=" + string.Join(";", helper.Writes.Select(s => s.WalletMode + "/" + s.ModeMismatch + "/" + s.Revision)));
            var written = helper.Writes.Last();
            Assert(BankStateValidator.Validate(written).Count == 0 && !written.ModeMismatch,
                "wallet reconciliation persists a valid unlocked bank state");
            if (blocked)
            {
                Assert(JsonSerializer.Serialize(written) == originalJson && f.Team.useSeparateWallets.Value == personal
                    && !f.Host.changeWalletTypeTonight.Value,
                    "rejected migration preserves all assets and restores the game topology and request flag");
                Assert(Game1.HudMessages.Any(m => m.Message == f.Text.Get("error.wallet-mode-migration-open-products")),
                    "rejected migration still explains the open-contract restriction to the player");
                f.Host.changeWalletTypeTonight.Value = true;
                Assert(!f.Host.changeWalletTypeTonight.Value && helper.Writes.Count == expectedWrites,
                    "a later ledger interaction is handled as a fresh request without duplicate persistence");
            }
            else
            {
                Assert(written.WalletMode == (personal ? WalletMode.Shared : WalletMode.Personal)
                    && f.Team.useSeparateWallets.Value == !personal,
                    "eligible reconciliation applies the requested wallet topology");
                var destination = personal ? written.SharedAccount : written.PersonalAccounts[101];
                Assert(destination.ActiveBalance == (personal ? 16000 : 10000)
                    && destination.ActiveInterestRemainder == (personal ? 0.8m : 0.4m),
                    "eligible reconciliation persists the migrated balance and fractional interest");
            }
            helper.EventsHub.Save();
            Assert(helper.Writes.Count == expectedWrites + 1 && JsonSerializer.Serialize(helper.Writes.Last()) == JsonSerializer.Serialize(written),
                "the ordinary Saving event still persists the aligned state once");
            Assert(f.Host.totalMoneyEarned == 111 && f.Remote.totalMoneyEarned == 222 && f.Team.totalMoneyEarned.Value == 333,
                "wallet reconciliation never changes earned-money statistics");
            helper.EventsHub.Unload();
        }

        foreach (var invalid in new[] { new BankState { SchemaVersion = 99 }, new BankState { SharedAccount = null } })
        {
            var f = new FundingFixture(false, "zh");
            var helper = new PersistenceHelper(f.Text, invalid);
            var originalJson = helper.StoredJson;
            var mod = new ModEntry();
            mod.InitializeForTest(helper, f.Monitor);
            helper.EventsHub.Load();
            helper.EventsHub.Save();
            Assert(helper.Writes.Count == 0 && helper.StoredJson == originalJson,
                "unsupported or malformed persisted state remains untouched through load and save events");
            helper.EventsHub.Unload();
        }
        Context.IsWorldReady = false;
        Game1.HudMessages.Clear();
    }

    private sealed class PersistenceHelper : IModHelper, IDataHelper, IConsoleCommandHelper
    {
        public readonly PersistenceEvents EventsHub = new PersistenceEvents();
        public readonly List<BankState> Writes = new List<BankState>();
        public string StoredJson { get; private set; }
        public PersistenceHelper(ITranslationHelper text, BankState state)
        {
            Translation = text;
            StoredJson = JsonSerializer.Serialize(state);
            Multiplayer = new FundingHelper(text);
        }
        public IModEvents Events => EventsHub;
        public IInputHelper Input => null;
        public IMultiplayerHelper Multiplayer { get; }
        public IDataHelper Data => this;
        public IGameContentHelper GameContent => null;
        public IModRegistry ModRegistry => null;
        public ITranslationHelper Translation { get; }
        public IConsoleCommandHelper ConsoleCommands => this;
        public T ReadConfig<T>() where T : class, new() { return new T(); }
        public void WriteConfig<T>(T config) where T : class, new() { }
        public T ReadSaveData<T>(string key) { return JsonSerializer.Deserialize<T>(StoredJson); }
        public void WriteSaveData<T>(string key, T value)
        {
            StoredJson = JsonSerializer.Serialize(value);
            Writes.Add(JsonSerializer.Deserialize<BankState>(StoredJson));
        }
        public T ReadJsonFile<T>(string path) where T : class { return null; }
        public void WriteJsonFile<T>(string path, T value) where T : class { }
        public void Add(string name, string documentation, Action<string, string[]> callback) { }
    }

    private sealed class PersistenceEvents : IModEvents, IGameLoopEvents, IInputEvents,
        IMultiplayerEvents, IContentEvents, IPlayerEvents, IDisplayEvents
    {
        public IGameLoopEvents GameLoop => this;
        public IInputEvents Input => this;
        public IMultiplayerEvents Multiplayer => this;
        public IContentEvents Content => this;
        public IPlayerEvents Player => this;
        public IDisplayEvents Display => this;
        public event EventHandler<SaveLoadedEventArgs> SaveLoaded;
        public event EventHandler<SavingEventArgs> Saving;
        public event EventHandler<ReturnedToTitleEventArgs> ReturnedToTitle;
        public void Load() { SaveLoaded?.Invoke(this, new SaveLoadedEventArgs()); }
        public void Save() { Saving?.Invoke(this, new SavingEventArgs()); }
        public void Unload() { ReturnedToTitle?.Invoke(this, new ReturnedToTitleEventArgs()); }
        public event EventHandler<DayStartedEventArgs> DayStarted { add { } remove { } }
        public event EventHandler<UpdateTickedEventArgs> UpdateTicked { add { } remove { } }
        public event EventHandler<GameLaunchedEventArgs> GameLaunched { add { } remove { } }
        public event EventHandler<ButtonPressedEventArgs> ButtonPressed { add { } remove { } }
        public event EventHandler<ButtonsChangedEventArgs> ButtonsChanged { add { } remove { } }
        public event EventHandler<ModMessageReceivedEventArgs> ModMessageReceived { add { } remove { } }
        public event EventHandler<PeerConnectedEventArgs> PeerConnected { add { } remove { } }
        public event EventHandler<PeerDisconnectedEventArgs> PeerDisconnected { add { } remove { } }
        public event EventHandler<AssetRequestedEventArgs> AssetRequested { add { } remove { } }
        public event EventHandler<AssetsInvalidatedEventArgs> AssetsInvalidated { add { } remove { } }
        public event EventHandler<WarpedEventArgs> Warped { add { } remove { } }
        public event EventHandler<MenuChangedEventArgs> MenuChanged { add { } remove { } }
    }
}
