using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using SimpleBank;
using SimpleBank.Core;

public static partial class Program
{
    private static void TestTermFundingMenu()
    {
        foreach (var locale in new[] { "zh", "default" })
        {
            var f = new FundingFixture(false, locale);
            var menu = f.OpenHostMenu();
            ClickFundingButton(menu, f.Text.Get("menu.tab.term"));
            ClickFundingButton(menu, f.Text.Get("menu.keypad.max"));
            Assert(DrawnFundingText(menu).Contains("5,000g"), "a newly opened menu defaults MAX to wallet funds: " + locale);
            ClickFundingButton(menu, f.Text.Get("menu.term.source.active"));
            Assert(DrawnFundingText(menu).Contains("5,000g") && DrawnFundingText(menu).Contains(f.Text.Get("menu.term.available", new { amount = "10,000g" })),
                "changing source preserves the entered amount and displays the selected available balance: " + locale);
            ClickFundingButton(menu, f.Text.Get("menu.keypad.max"));
            ClickFundingButton(menu, f.Text.Get("menu.term.source.wallet"));
            Assert(DrawnFundingText(menu).Contains("10,000g") && DrawnFundingText(menu).Contains(f.Text.Get("error.wallet-insufficient")),
                "switching to a smaller source reports insufficiency without silently shrinking the amount: " + locale);
            ClickFundingButton(menu, f.Text.Get("action.term"));
            Assert(!DrawnFundingText(menu).Contains(f.Text.Get("menu.confirm.button")) && f.Bank.State.Revision == 0,
                "insufficient selected funds cannot open a confirmation or fall back to the other source");
            ClickFundingButton(menu, f.Text.Get("menu.term.source.active"));
            ClickFundingButton(menu, f.Text.Get("menu.tab.active"));
            ClickFundingButton(menu, f.Text.Get("menu.tab.term"));
            ClickFundingButton(menu, f.Text.Get("menu.keypad.max"));
            Assert(DrawnFundingText(menu).Contains("10,000g"), "source selection survives page changes within one menu");
            foreach (var viewport in new[] { new Rectangle(0, 0, 1280, 720), new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 684, 604) })
            {
                Game1.uiViewport = viewport;
                DrawnFundingText(menu);
                Assert(Utility.DrawnTexts.Where(x => x.Text == f.Text.Get("menu.term.source.active") || x.Text == f.Text.Get("menu.term.source.wallet"))
                    .All(x => x.Position.X >= menu.xPositionOnScreen && x.Position.X + Game1.smallFont.MeasureString(x.Text).X <= menu.xPositionOnScreen + menu.width
                        && x.Position.Y > menu.yPositionOnScreen && x.Position.Y + 16 < menu.yPositionOnScreen + menu.height - 58),
                    "funding controls stay within the workspace after viewport changes (stub geometry only)");
            }
            ClickFundingButton(menu, f.Text.Get("action.term"));
            Assert(DrawnFundingText(menu).Any(x => x.Contains(f.Text.Get("menu.term.source.active")) && x.Contains("10,000g") && x.Contains("0.01")),
                "confirmation identifies source, principal and locked daily rate");
            menu.receiveKeyPress(Keys.D2);
            Assert(!DrawnFundingText(menu).Contains(f.Text.Get("menu.confirm.button")) && DrawnFundingText(menu).Contains("2g"),
                "keyboard amount edits cancel the old confirmation before editing");
            ClickFundingButton(menu, f.Text.Get("action.term"));
            menu.receiveScrollWheelAction(-1);
            Assert(!DrawnFundingText(menu).Contains(f.Text.Get("menu.confirm.button")), "changing product cancels pending confirmation");
            ClickFundingButton(menu, f.Text.Get("action.term"));
            ClickFundingButton(menu, f.Text.Get("menu.term.source.wallet"));
            Assert(!DrawnFundingText(menu).Contains(f.Text.Get("menu.confirm.button")), "changing source cancels pending confirmation");

            ClickFundingButton(menu, f.Text.Get("menu.term.source.wallet"));
            menu.HandleNavigation(1, 0);
            Assert(menu.currentlySnappedComponent.name == f.Text.Get("menu.term.source.active"), "controller navigation reaches the other source");
            menu.HandleConfirm();
            ClickFundingButton(menu, f.Text.Get("menu.keypad.max"));
            ClickFundingButton(menu, f.Text.Get("action.term"));
            ClickFundingButton(menu, f.Text.Get("menu.confirm.button"));
            Assert(f.Bank.State.SharedAccount.ActiveBalance == 0 && f.Bank.State.SharedAccount.ActiveInterestRemainder == 0.4m
                && f.Bank.State.SharedAccount.TermDeposits.Single().Principal == 10000 && f.Team.GetIndividualMoney(f.Host) == 5000
                && f.Host.totalMoneyEarned == 111 && f.Remote.totalMoneyEarned == 222 && f.Team.totalMoneyEarned.Value == 333,
                "ATM active funding commits once without wallet writes or earned-money changes");

            menu.exitThisMenu();
            var reopened = f.OpenHostMenu();
            ClickFundingButton(reopened, f.Text.Get("menu.tab.term"));
            ClickFundingButton(reopened, f.Text.Get("menu.keypad.max"));
            Assert(DrawnFundingText(reopened).Contains("5,000g"), "reopening ATM resets funding selection to wallet");
            ClickFundingButton(reopened, f.Text.Get("menu.term.source.active"));
            ClickFundingButton(reopened, f.Text.Get("action.term"));
            Assert(DrawnFundingText(reopened).Contains(f.Text.Get("error.active-insufficient")) && f.Team.GetIndividualMoney(f.Host) == 5000
                && f.Bank.State.SharedAccount.TermDeposits.Count == 1, "empty active funds never fall back to an available wallet");
            ClickFundingButton(reopened, f.Text.Get("menu.keypad.max"));
            Assert(!DrawnFundingText(reopened).Contains(f.Text.Get("error.active-insufficient")), "correcting the amount clears stale insufficient-funds feedback");
            ClickFundingButton(reopened, f.Text.Get("menu.term.source.wallet"));
            ClickFundingButton(reopened, f.Text.Get("menu.keypad.max"));
            ClickFundingButton(reopened, f.Text.Get("action.term"));
            ClickFundingButton(reopened, f.Text.Get("menu.confirm.button"));
            Assert(f.Team.GetIndividualMoney(f.Host) == 0 && f.Bank.State.SharedAccount.TermDeposits.Count == 2
                && f.Bank.State.SharedAccount.ActiveBalance == 0 && f.Team.totalMoneyEarned.Value == 333,
                "default wallet funding still works through the menu without changing historical earnings");
            reopened.exitThisMenu();
        }
        Game1.uiViewport = new Rectangle(0, 0, 1920, 1080);
    }

    private static void TestTermFundingMultiplayer()
    {
        foreach (var personal in new[] { false, true })
        {
            var f = new FundingFixture(personal, "zh");
            var hostMenu = f.OpenHostMenu();
            ClickFundingButton(hostMenu, f.Text.Get("menu.tab.term"));
            // Model a second local screen using its own menu and a client bank replica.
            f.HostCoordinator.SendSnapshotTo(f.Remote.UniqueMultiplayerID);
            var initial = (BankSyncMessage)f.HostWire.Messages.Last().Payload;
            var clientBank = new BankService(initial.Config, initial.State.Clone());
            var clientWire = new FundingHelper(f.Text);
            var clientCoordinator = new MultiplayerCoordinator(clientWire, f.Monitor, f.Diagnostics);
            f.UseClient();
            clientCoordinator.Attach(clientBank);
            var clientMenu = new AtmMenu(clientBank, clientCoordinator, f.Remote, f.Text, new InputConfig(), f.Diagnostics);
            Game1.activeClickableMenu = clientMenu;
            ClickFundingButton(clientMenu, f.Text.Get("menu.tab.term"));
            ClickFundingButton(clientMenu, f.Text.Get("menu.term.source.active"));
            ClickFundingButton(clientMenu, f.Text.Get("menu.keypad.max"));
            ClickFundingButton(clientMenu, f.Text.Get("action.term"));
            ClickFundingButton(clientMenu, f.Text.Get("menu.confirm.button"));
            var sent = clientWire.Messages.Single(x => x.Type == MultiplayerCoordinator.RequestMessage);
            var request = (BankRequest)sent.Payload;
            var funded = personal ? 6000 : 10000;
            Assert(request.Operation == "term-active" && request.Amount == funded && sent.Recipients.Single() == f.Host.UniqueMultiplayerID
                && clientBank.GetAccount(f.Remote.UniqueMultiplayerID).TermDeposits.Count == 0 && f.Bank.State.Revision == 0,
                "client sends a targeted source-specific request without modifying authoritative or replica funds");
            ClickFundingButton(clientMenu, f.Text.Get("menu.term.source.wallet"));
            clientMenu.receiveKeyPress(Keys.D1);
            clientMenu.receiveScrollWheelAction(-1);
            clientMenu.HandlePage(1);
            Assert(clientWire.Messages.Count(x => x.Type == MultiplayerCoordinator.RequestMessage) == 1
                && DrawnFundingText(clientMenu).Contains(f.Text.Get("message.waiting-host"))
                && DrawnFundingText(clientMenu).Contains(funded.ToString("N0") + "g"),
                "pending remote requests lock source, amount, product and page editing");

            f.UseHost();
            Game1.activeClickableMenu = hostMenu;
            ClickFundingButton(hostMenu, f.Text.Get("menu.keypad.max"));
            Assert(DrawnFundingText(hostMenu).Contains((personal ? 1000 : 5000).ToString("N0") + "g"),
                "another screen's source selection does not change the host menu selection");
            f.HostWire.Messages.Clear();
            request.AbsoluteDay = 9999;
            DeliverFundingMessage(f.HostCoordinator, f.Remote.UniqueMultiplayerID, sent);
            var account = f.Bank.GetAccount(f.Remote.UniqueMultiplayerID);
            Assert(account.ActiveBalance == 0 && account.TermDeposits.Single().Principal == funded
                && account.TermDeposits[0].OpenDay == 10 && account.ActiveInterestRemainder == 0.4m,
                "host uses authoritative account funds and game date for the remote term");
            Assert(f.Team.GetIndividualMoney(f.Remote) == (personal ? 3000 : 5000)
                && f.Remote.totalMoneyEarned == 222 && f.Team.totalMoneyEarned.Value == 333,
                "remote active funding preserves personal/shared wallets and earnings");
            if (personal)
                Assert(f.Bank.GetAccount(f.Host.UniqueMultiplayerID).ActiveBalance == 10000
                    && f.Bank.GetAccount(f.Host.UniqueMultiplayerID).TermDeposits.Count == 0, "personal remote funding cannot touch the host account");
            var sync = f.HostWire.Messages.First(x => x.Type == MultiplayerCoordinator.SyncMessage);
            var response = f.HostWire.Messages.Last(x => x.Type == MultiplayerCoordinator.ResponseMessage);
            Assert(sync.Recipients.Single() == f.Remote.UniqueMultiplayerID && response.Recipients.Single() == f.Remote.UniqueMultiplayerID
                && ((BankResponse)response.Payload).Status == TransactionStatus.Applied,
                "host publishes the targeted snapshot before acknowledging success");
            var syncState = ((BankSyncMessage)sync.Payload).State;
            Assert(personal ? syncState.PersonalAccounts.Count == 1 && syncState.PersonalAccounts.ContainsKey(f.Remote.UniqueMultiplayerID)
                : syncState.PersonalAccounts.Count == 0, "transaction snapshots preserve account filtering");
            f.UseClient();
            Game1.activeClickableMenu = clientMenu;
            DeliverFundingMessage(clientCoordinator, f.Host.UniqueMultiplayerID, sync);
            DeliverFundingMessage(clientCoordinator, f.Host.UniqueMultiplayerID, response);
            Assert(clientBank.GetAccount(f.Remote.UniqueMultiplayerID).ActiveBalance == 0
                && clientBank.GetAccount(f.Remote.UniqueMultiplayerID).TermDeposits.Count == 1
                && DrawnFundingText(clientMenu).Contains(f.Text.Get("message.term-active.success")),
                "accepted snapshots refresh the UI even when the game wallet never changes");
            ClickFundingButton(clientMenu, f.Text.Get("menu.keypad.max"));
            Assert(DrawnFundingText(clientMenu).Contains("0g"), "MAX uses the refreshed active balance after host success");
            ClickFundingButton(clientMenu, f.Text.Get("action.term"));
            Assert(DrawnFundingText(clientMenu).Contains(f.Text.Get("error.amount-must-be-positive")),
                "a disabled source change during the request cannot silently switch subsequent MAX back to the wallet");

            f.UseHost();
            request.ExpectedRevision = 0;
            DeliverFundingMessage(f.HostCoordinator, f.Remote.UniqueMultiplayerID, sent);
            Assert(((BankResponse)f.HostWire.Messages.Last().Payload).Status == TransactionStatus.StaleRevision && f.Bank.State.Revision == 1,
                "replayed old requests cannot double-debit and receive a fresh snapshot");
            request.ExpectedRevision = 1;
            DeliverFundingMessage(f.HostCoordinator, f.Remote.UniqueMultiplayerID, sent);
            Assert(((BankResponse)f.HostWire.Messages.Last().Payload).Status == TransactionStatus.Duplicate && account.TermDeposits.Count == 1,
                "replayed transaction IDs remain harmless at the current revision");
            request.PlayerId = f.Host.UniqueMultiplayerID;
            DeliverFundingMessage(f.HostCoordinator, f.Remote.UniqueMultiplayerID, sent);
            Assert(((BankResponse)f.HostWire.Messages.Last().Payload).Status == TransactionStatus.Rejected && f.Bank.State.Revision == 1,
                "a remote sender cannot choose another player's account");
            DeliverFundingMessage(f.HostCoordinator, f.Remote.UniqueMultiplayerID, sync);
            Assert(f.Bank.State.Revision == 1 && f.Bank.GetAccount(f.Remote.UniqueMultiplayerID).TermDeposits.Count == 1,
                "the authoritative host never applies a client snapshot");
            clientMenu.exitThisMenu();
            hostMenu.exitThisMenu();
        }
        Context.ScreenId = 0;
        Context.IsMainPlayer = true;
    }

    private static List<string> DrawnFundingText(AtmMenu menu)
    {
        Utility.DrawnTexts.Clear();
        menu.draw(new SpriteBatch(new GraphicsDevice()));
        return Utility.DrawnTexts.Select(x => x.Text).ToList();
    }

    private static void ClickFundingButton(AtmMenu menu, string label)
    {
        DrawnFundingText(menu);
        // Tabs are drawn before equally named summary cards; click the interactive label.
        var text = Utility.DrawnTexts.FirstOrDefault(x => x.Text == label);
        if (text == null) throw new Exception("Missing visible control: " + label + "; drawn: " + string.Join(" | ", Utility.DrawnTexts.Select(x => x.Text)));
        menu.receiveLeftClick((int)text.Position.X + 2, (int)text.Position.Y + 8);
    }

    private static void DeliverFundingMessage(MultiplayerCoordinator receiver, long sender, FundingMessage message)
    {
        var serialized = JsonSerializer.Serialize(message.Payload, message.Payload.GetType());
        receiver.HandleMessage(new ModMessageReceivedEventArgs
        {
            FromModID = ModEntry.UniqueId, FromPlayerID = sender, Type = message.Type,
            Payload = JsonSerializer.Deserialize(serialized, message.Payload.GetType())
        });
    }

    private sealed class FundingFixture
    {
        public readonly FarmerTeam Team = new FarmerTeam();
        public readonly Farmer Host;
        public readonly Farmer Remote;
        public readonly BankService Bank;
        public readonly FundingTranslations Text;
        public readonly FundingHelper HostWire;
        public readonly HarnessMonitor Monitor = new HarnessMonitor();
        public readonly ModDiagnostics Diagnostics;
        public readonly MultiplayerCoordinator HostCoordinator;

        public FundingFixture(bool personal, string locale)
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            Text = new FundingTranslations(locale);
            HostWire = new FundingHelper(Text);
            Diagnostics = new ModDiagnostics(Monitor, () => false);
            Team.useSeparateWallets.Value = personal;
            Team.totalMoneyEarned.Value = 333;
            var location = new GameLocation { NameOrUniqueName = "ManorHouse", Map = new GameMap() };
            location.Map.Properties[ModEntry.AtmMapProperty] = ModEntry.AtmMapPropertyValue;
            Host = new Farmer { UniqueMultiplayerID = 101, team = Team, currentLocation = location, TilePoint = new Point(14, 10), totalMoneyEarned = 111 };
            Remote = new Farmer { UniqueMultiplayerID = 202, team = Team, currentLocation = location, TilePoint = new Point(15, 10), totalMoneyEarned = 222 };
            Team.SetIndividualMoney(Host, personal ? 1000 : 5000);
            if (personal) Team.SetIndividualMoney(Remote, 3000);
            Game1.MasterPlayer = Host;
            Game1.stats.DaysPlayed = 10;
            Game1.OnlineFarmers.Clear();
            Game1.OnlineFarmers.AddRange(new[] { Host, Remote });
            // Exercise the smallest supported menu layout with real input handlers.
            Game1.uiViewport = new Rectangle(0, 0, 684, 604);
            var state = new BankState { WalletMode = personal ? WalletMode.Personal : WalletMode.Shared, LastSettlementDay = 10 };
            if (personal)
            {
                state.PersonalAccounts[101] = new Account { Id = "player-101", ActiveBalance = 10000, ActiveInterestRemainder = 0.4m };
                state.PersonalAccounts[202] = new Account { Id = "player-202", ActiveBalance = 6000, ActiveInterestRemainder = 0.4m };
            }
            else { state.SharedAccount.ActiveBalance = 10000; state.SharedAccount.ActiveInterestRemainder = 0.4m; }
            Bank = new BankService(new BankConfig(), state);
            UseHost();
            HostCoordinator = new MultiplayerCoordinator(HostWire, Monitor, Diagnostics);
            HostCoordinator.Attach(Bank);
        }

        public void UseHost() { Context.ScreenId = 0; Context.IsMainPlayer = true; Game1.player = Host; }
        public void UseClient() { Context.ScreenId = 1; Context.IsMainPlayer = false; Game1.player = Remote; }
        public AtmMenu OpenHostMenu()
        {
            UseHost();
            var menu = new AtmMenu(Bank, HostCoordinator, Host, Text, new InputConfig(), Diagnostics);
            Game1.activeClickableMenu = menu;
            return menu;
        }
    }

    private sealed class FundingTranslations : ITranslationHelper
    {
        private readonly Dictionary<string, string> values;
        public FundingTranslations(string locale)
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !Directory.Exists(Path.Combine(root.FullName, "i18n"))) root = root.Parent;
            if (root == null) throw new Exception("Cannot find test translations");
            values = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(root.FullName, "i18n", locale + ".json")));
        }
        public string Get(string key) { return values[key]; }
        public string Get(string key, object tokens)
        {
            var value = Get(key);
            foreach (var token in JsonSerializer.SerializeToElement(tokens).EnumerateObject())
                value = value.Replace("{{" + token.Name + "}}", token.Value.ToString());
            return value;
        }
    }

    private sealed class FundingMessage
    {
        public object Payload;
        public string Type;
        public long[] Recipients;
    }

    private sealed class FundingHelper : IModHelper, IMultiplayerHelper
    {
        public readonly List<FundingMessage> Messages = new List<FundingMessage>();
        public FundingHelper(ITranslationHelper text) { Translation = text; }
        public IModEvents Events => null;
        public IInputHelper Input => null;
        public IMultiplayerHelper Multiplayer => this;
        public IDataHelper Data => null;
        public IGameContentHelper GameContent => null;
        public IModRegistry ModRegistry => null;
        public ITranslationHelper Translation { get; }
        public IConsoleCommandHelper ConsoleCommands => null;
        public T ReadConfig<T>() where T : class, new() { return new T(); }
        public void WriteConfig<T>(T config) where T : class, new() { }
        public void SendMessage<T>(T message, string type, string[] modIDs, long[] playerIDs = null)
        {
            Messages.Add(new FundingMessage { Payload = message, Type = type, Recipients = playerIDs });
        }
        public IMultiplayerPeer GetConnectedPlayer(long id) { return null; }
        public IEnumerable<IMultiplayerPeer> GetConnectedPlayers() { return Array.Empty<IMultiplayerPeer>(); }
        public long GetNewID() { return 1; }
    }
}
