using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Netcode;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using SimpleBank.Core;

namespace SimpleBank
{
    internal sealed class ModEntry : Mod
    {
        internal const string UniqueId = "Akiba275.SimpleBank";
        internal const string AtmMapProperty = "SimpleBankAtmArea";
        internal const string AtmMapPropertyValue = "14,8,2,2";
        internal const string AtmAssetName = "Mods/Akiba275/SimpleBank/Atm";
        private const int AtmSpriteId = 2751408;
        private const int AtmMarkerRetryTicks = 120;
        private const double SlowAtmUpdateThresholdMilliseconds = 4d;
        // Content Patcher only marks this area. The main mod draws an exact
        // 32x32-pixel sprite at 4x scale over the untouched wall tiles.
        internal static readonly Point AtmOriginTile = new Point(14, 8);
        internal static readonly Point[] AtmActionTiles = { new Point(14, 9), new Point(15, 9) };
        private readonly PerScreen<ScreenState> screenState = new PerScreen<ScreenState>(() => new ScreenState());
        private BankConfig config;
        private SaveRepository save;
        private ITranslationHelper i18n;
        private ModDiagnostics diagnostics;
        private BankConfig configBeforeMenu;
        private InputConfig inputConfig;
        private InputConfig inputConfigBeforeMenu;

        // SMAPI runs the same Mod instance for every local split-screen player.
        // Every field which represents a loaded world, local menu, lifecycle
        // subscription, or multiplayer role must therefore be screen-local.
        // Keeping these behind properties makes accidental cross-screen access
        // visible while preserving the existing event-handler code.
        private ScreenState CurrentScreen { get { return screenState.Value; } }
        private BankService bank { get { return CurrentScreen.Bank; } set { CurrentScreen.Bank = value; } }
        private MultiplayerCoordinator multiplayer
        {
            get
            {
                if (CurrentScreen.Multiplayer == null)
                {
                    CurrentScreen.Multiplayer = new MultiplayerCoordinator(Helper, Monitor, diagnostics);
                    CurrentScreen.Multiplayer.AtmAvailabilityChanged += QueueAtmRefresh;
                    CurrentScreen.Multiplayer.BankStateChanged += CheckLoanOverdueNotification;
                    CurrentScreen.Multiplayer.CollectionNoticeReceived += ShowCollectionNotice;
                }
                return CurrentScreen.Multiplayer;
            }
        }
        private OverdueCollectionCoordinator collection
        {
            get
            {
                if (CurrentScreen.Collection == null)
                    CurrentScreen.Collection = new OverdueCollectionCoordinator(Monitor, diagnostics, multiplayer);
                return CurrentScreen.Collection;
            }
        }
        private bool warnedAtmMarkerMissing { get { return CurrentScreen.WarnedAtmMarkerMissing; } set { CurrentScreen.WarnedAtmMarkerMissing = value; } }
        private bool pendingAtmRefresh { get { return CurrentScreen.PendingAtmRefresh; } set { CurrentScreen.PendingAtmRefresh = value; } }
        private int atmMarkerRetryTicks { get { return CurrentScreen.AtmMarkerRetryTicks; } set { CurrentScreen.AtmMarkerRetryTicks = value; } }
        private FarmerTeam observedWalletTeam { get { return CurrentScreen.ObservedWalletTeam; } set { CurrentScreen.ObservedWalletTeam = value; } }
        private Farmer observedWalletFarmer { get { return CurrentScreen.ObservedWalletFarmer; } set { CurrentScreen.ObservedWalletFarmer = value; } }
        private bool restoringWalletMode { get { return CurrentScreen.RestoringWalletMode; } set { CurrentScreen.RestoringWalletMode = value; } }
        private bool restoringWalletModeRequest { get { return CurrentScreen.RestoringWalletModeRequest; } set { CurrentScreen.RestoringWalletModeRequest = value; } }
        private string lastAtmBlockReason { get { return CurrentScreen.LastAtmBlockReason; } set { CurrentScreen.LastAtmBlockReason = value; } }

        public override void Entry(IModHelper helper)
        {
            i18n = helper.Translation;
            try { config = helper.ReadConfig<BankConfig>() ?? new BankConfig(); }
            catch (Exception ex)
            {
                Monitor.Log("Could not parse config.json; restored safe defaults: " + ex.Message, LogLevel.Warn);
                config = new BankConfig();
                helper.WriteConfig(config);
            }
            var configValidation = config.ValidateAndNormalize();
            if (!configValidation.IsValid)
            {
                Monitor.Log("Invalid config.json; restored safe defaults: " + string.Join("; ", configValidation.Errors), LogLevel.Warn);
                config = new BankConfig();
                helper.WriteConfig(config);
            }
            try { inputConfig = helper.Data.ReadJsonFile<InputConfig>("controls.json") ?? new InputConfig(); }
            catch (Exception ex)
            {
                Monitor.Log("Could not parse controls.json; restored safe defaults: " + ex.Message, LogLevel.Warn);
                inputConfig = new InputConfig();
                helper.Data.WriteJsonFile("controls.json", inputConfig);
            }
            inputConfig.RestoreMissingDefaults();
            diagnostics = new ModDiagnostics(Monitor, () => config != null && config.DebugLogging);
            save = new SaveRepository(helper, Monitor, diagnostics);
            if (diagnostics.Enabled)
                diagnostics.Log("lifecycle: entry, modId=" + ModManifest.UniqueID + ", atmEnabled=" + config.EnableAtm + ", debugLogging=" + config.DebugLogging);
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.DayStarted += OnDayStarted;
            helper.Events.GameLoop.Saving += OnSaving;
            helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Input.ButtonsChanged += OnButtonsChanged;
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.Content.AssetRequested += OnAssetRequested;
            helper.Events.Content.AssetsInvalidated += OnAssetsInvalidated;
            helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
            helper.Events.Multiplayer.PeerConnected += OnPeerConnected;
            helper.Events.Multiplayer.PeerDisconnected += OnPeerDisconnected;
            helper.Events.Player.Warped += OnWarped;
            RegisterAdminCommands();
        }

        private void RegisterAdminCommands()
        {
            Helper.ConsoleCommands.Add("simplebank_debt", "Usage: simplebank_debt [playerId]", OnDebtCommand);
            Helper.ConsoleCommands.Add("simplebank_pay", "Usage: simplebank_pay <playerId> <amount> confirm", OnPayDebtCommand);
            Helper.ConsoleCommands.Add("simplebank_cancel_debt", "Usage: simplebank_cancel_debt <playerId> confirm", OnCancelDebtCommand);
            Helper.ConsoleCommands.Add("simplebank_credit_ban", "Usage: simplebank_credit_ban <playerId> <days|permanent> confirm", OnCreditBanCommand);
            Helper.ConsoleCommands.Add("simplebank_credit_clear", "Usage: simplebank_credit_clear <playerId> confirm", OnCreditClearCommand);
        }

        private bool TryBeginAdminCommand(string[] args, int requiredCount, out long playerId)
        {
            playerId = 0;
            if (!Context.IsWorldReady || !Context.IsMainPlayer || bank == null)
            {
                Monitor.Log("This Simple Bank command requires a loaded save on the authoritative host.", LogLevel.Warn);
                return false;
            }
            if (args == null || args.Length < requiredCount || !long.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out playerId) || playerId <= 0)
            {
                Monitor.Log("A valid stable multiplayer player ID is required.", LogLevel.Warn);
                return false;
            }
            return true;
        }

        private void OnDebtCommand(string command, string[] args)
        {
            long playerId;
            if ((args == null || args.Length == 0) && Context.IsWorldReady && Game1.player != null)
                playerId = Game1.player.UniqueMultiplayerID;
            else if (!TryBeginAdminCommand(args, 1, out playerId)) return;
            if (!Context.IsMainPlayer || bank == null) return;
            var account = bank.GetAccount(playerId);
            var loan = account.Loan;
            var operatorId = Game1.player == null ? 0 : Game1.player.UniqueMultiplayerID;
            var auditPrefix = "Simple Bank admin query: operatorId=" + operatorId + ", targetPlayerId=" + playerId + ", day=" + (int)Game1.stats.DaysPlayed;
            if (loan == null)
            {
                Monitor.Log(auditPrefix + ", originalDebt=0, loan=none, creditRestriction=" + account.CreditRestriction.Mode, LogLevel.Info);
                return;
            }
            Monitor.Log(auditPrefix + ", originalDebt=" + loan.TotalOwed + ", loanId=" + loan.Id + ", status=" + loan.Status
                + ", principal=" + loan.Principal + ", interest=" + loan.PayableInterest + ", total=" + loan.TotalOwed
                + ", collectionStartDay=" + loan.CollectionStartDay + ", defaultDay=" + loan.DefaultDay
                + ", creditRestriction=" + account.CreditRestriction.Mode + ".", LogLevel.Info);
        }

        private void OnPayDebtCommand(string command, string[] args)
        {
            long playerId;
            if (!TryBeginAdminCommand(args, 2, out playerId)) return;
            long amount;
            if (!long.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out amount) || amount <= 0)
            {
                Monitor.Log("A positive whole-gold payment amount is required.", LogLevel.Warn);
                return;
            }
            if (!HasAdminConfirmation(args, 2)) return;
            var hostFarmer = Game1.player;
            if (hostFarmer == null || hostFarmer.team == null)
            {
                Monitor.Log("The authoritative host wallet is unavailable.", LogLevel.Warn);
                return;
            }
            var originalDebt = GetDebtTotal(playerId);
            var result = bank.RepayLoan(playerId, new GameWalletAdapter(hostFarmer, bank.State.WalletMode, diagnostics), amount,
                (int)Game1.stats.DaysPlayed, "admin-pay-" + Guid.NewGuid().ToString("N"), bank.State.Revision);
            FinishAdminMutation("voluntary payment", playerId, originalDebt, result);
        }

        private void OnCancelDebtCommand(string command, string[] args)
        {
            long playerId;
            if (!TryBeginAdminCommand(args, 1, out playerId)) return;
            if (!HasAdminConfirmation(args, 1)) return;
            var originalDebt = GetDebtTotal(playerId);
            var result = bank.CancelDebt(playerId, (int)Game1.stats.DaysPlayed, "admin-cancel-" + Guid.NewGuid().ToString("N"), bank.State.Revision);
            FinishAdminMutation("debt cancellation", playerId, originalDebt, result);
        }

        private void OnCreditBanCommand(string command, string[] args)
        {
            long playerId;
            if (!TryBeginAdminCommand(args, 2, out playerId)) return;
            if (!HasAdminConfirmation(args, 2)) return;
            CreditRestrictionMode mode;
            int? untilDay = null;
            if (string.Equals(args[1], "permanent", StringComparison.OrdinalIgnoreCase)) mode = CreditRestrictionMode.Permanent;
            else
            {
                int days;
                if (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out days) || days <= 0)
                {
                    Monitor.Log("Specify a positive number of days or 'permanent'.", LogLevel.Warn);
                    return;
                }
                var currentDay = (int)Game1.stats.DaysPlayed;
                if (days > int.MaxValue - currentDay)
                {
                    Monitor.Log("The requested credit-restriction end date is too large.", LogLevel.Warn);
                    return;
                }
                mode = CreditRestrictionMode.UntilDay;
                untilDay = currentDay + days;
            }
            var originalDebt = GetDebtTotal(playerId);
            var result = bank.SetCreditRestriction(playerId, mode, untilDay, "admin-credit-ban-" + Guid.NewGuid().ToString("N"), bank.State.Revision);
            FinishAdminMutation("credit restriction", playerId, originalDebt, result);
        }

        private void OnCreditClearCommand(string command, string[] args)
        {
            long playerId;
            if (!TryBeginAdminCommand(args, 1, out playerId)) return;
            if (!HasAdminConfirmation(args, 1)) return;
            var originalDebt = GetDebtTotal(playerId);
            var result = bank.ClearCreditRestriction(playerId, "admin-credit-clear-" + Guid.NewGuid().ToString("N"), bank.State.Revision);
            FinishAdminMutation("credit restriction clear", playerId, originalDebt, result);
        }

        private bool HasAdminConfirmation(string[] args, int index)
        {
            if (args != null && args.Length > index && string.Equals(args[index], "confirm", StringComparison.OrdinalIgnoreCase)) return true;
            Monitor.Log("No change was made. Review the command, then append 'confirm' to execute it.", LogLevel.Warn);
            return false;
        }

        private long GetDebtTotal(long playerId)
        {
            var loan = bank.GetAccount(playerId).Loan;
            return loan == null ? 0 : loan.TotalOwed;
        }

        private void FinishAdminMutation(string operation, long playerId, long originalDebt, TransactionResult result)
        {
            var operatorId = Game1.player == null ? 0 : Game1.player.UniqueMultiplayerID;
            Monitor.Log("Simple Bank admin audit: operation=" + operation + ", operatorId=" + operatorId + ", targetPlayerId=" + playerId
                + ", day=" + (int)Game1.stats.DaysPlayed + ", originalDebt=" + originalDebt + ", status=" + result.Status
                + ", message=" + result.Message + ", revision=" + result.NewRevision + ".",
                result.Status == TransactionStatus.Applied ? LogLevel.Info : result.Message == "wallet-rollback-failed" ? LogLevel.Error : LogLevel.Warn);
            if (result.Status != TransactionStatus.Applied) return;
            collection.RefreshWalletBaselines();
            multiplayer.PublishStateChanged();
            multiplayer.BroadcastSnapshot();
        }

        private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            if (diagnostics.Enabled)
                diagnostics.Log("lifecycle: save-loaded, screenId=" + Context.ScreenId + ", isMainPlayer=" + Context.IsMainPlayer + ", isMultiplayer=" + Context.IsMultiplayer + ", bankNull=" + (bank == null));
            if (bank != null)
                return;

            var currentMode = Game1.player.team.useSeparateWallets.Value ? WalletMode.Personal : WalletMode.Shared;
            var day = (int)Game1.stats.DaysPlayed;
            var data = Context.IsMainPlayer ? save.Read(config, day) : null;
            if (data == null) data = new BankState { WalletMode = currentMode, DataInvalid = Context.IsMainPlayer && (save.HasUnsupportedSchema || save.HasInvalidData) };
            else
                data.ModeMismatch = data.WalletMode != currentMode;
            bank = new BankService(config, data, () => diagnostics.Enabled,
                message => diagnostics.Log("screenId=" + Context.ScreenId + ", " + message));
            if (bank.State.DataInvalid) Monitor.Log(i18n.Get("error.save-data-invalid"), LogLevel.Error);
            multiplayer.Attach(bank);
            collection.Attach(bank);
            AttachWalletModeGuard();
            EnsureWalletModeAndPersist();
            CheckLoanOverdueNotification();
            multiplayer.BroadcastSnapshot();
            QueueAtmRefresh("save-loaded");
            if (diagnostics.Enabled)
                diagnostics.Log("lifecycle: save-ready, screenId=" + Context.ScreenId + ", playerId=" + Game1.player.UniqueMultiplayerID + ", mode=" + bank.State.WalletMode + ", revision=" + bank.State.Revision + ", dataInvalid=" + bank.State.DataInvalid + ", modeMismatch=" + bank.State.ModeMismatch);
        }

        private void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            if (!Context.IsMainPlayer || bank == null) return;
            if (!EnsureWalletModeAndPersist())
            {
                multiplayer.BroadcastSnapshot();
                return;
            }
            var day = (int)Game1.stats.DaysPlayed;
            var activeBefore = diagnostics.Enabled ? TotalActiveBalance(bank.State) : 0;
            var result = bank.SettleToDay(day, "settlement-" + day, bank.State.Revision);
            if (diagnostics.Enabled)
            {
                var activeAfter = TotalActiveBalance(bank.State);
                diagnostics.Log("daily-settlement: screenId=" + Context.ScreenId
                    + ", day=" + day
                    + ", status=" + result.Status
                    + ", activeBefore=" + activeBefore.ToString(CultureInfo.InvariantCulture)
                    + ", activeAfter=" + activeAfter.ToString(CultureInfo.InvariantCulture)
                    + ", activeDelta=" + (activeAfter - activeBefore).ToString(CultureInfo.InvariantCulture)
                    + ", revision=" + bank.State.Revision);
            }
            if (result.Status == TransactionStatus.Rejected) Monitor.Log(i18n.Get("error.settlement", new { message = i18n.Get("error." + result.Message) }), LogLevel.Error);
            else
            {
                if (collection.OnDayStarted(day)) multiplayer.PublishStateChanged();
                CheckLoanOverdueNotification();
            }
            multiplayer.BroadcastSnapshot();
        }

        private void CheckLoanOverdueNotification()
        {
            if (!Context.IsWorldReady || bank == null || Game1.player == null || bank.State.DataInvalid)
                return;

            Account account;
            if (bank.State.WalletMode == WalletMode.Shared)
                account = bank.State.SharedAccount;
            else if (!bank.State.PersonalAccounts.TryGetValue(Game1.player.UniqueMultiplayerID, out account))
                return;

            var loan = account == null ? null : account.Loan;
            if (loan == null || loan.Status == LoanStatus.Current || loan.Status == LoanStatus.Settled || loan.Status == LoanStatus.Cancelled || string.IsNullOrWhiteSpace(loan.Id))
                return;
            var notificationKey = loan.Id + ":" + loan.Status;
            if (!CurrentScreen.NotifiedOverdueLoanIds.Add(notificationKey))
                return;

            var daysOverdue = Math.Max(1, (int)Game1.stats.DaysPlayed - loan.MaturityDay);
            var message = i18n.Get("message.loan-overdue", new
            {
                days = daysOverdue,
                principal = loan.Principal,
                interest = loan.PayableInterest,
                status = i18n.Get("menu.loan.status." + loan.Status.ToString().ToLowerInvariant())
            });
            Game1.addHUDMessage(new HUDMessage(message, HUDMessage.error_type));
            if (diagnostics.Enabled)
                diagnostics.Log("loan-notification: overdue-shown, screenId=" + Context.ScreenId
                    + ", playerId=" + Game1.player.UniqueMultiplayerID
                    + ", loanId=" + loan.Id
                    + ", daysOverdue=" + daysOverdue
                    + ", principal=" + loan.Principal
                    + ", interest=" + loan.PayableInterest
                    + ", mode=" + bank.State.WalletMode);
        }

        private void ShowCollectionNotice(CollectionNotice notice)
        {
            if (!Context.IsWorldReady || notice == null || Game1.player == null || notice.PlayerId != Game1.player.UniqueMultiplayerID) return;
            var sourceKey = notice.Operation == "active-setoff"
                ? "message.collection.source.active"
                : notice.Operation == "income-garnishment" ? "message.collection.source.income" : "message.collection.source.wallet";
            var amount = checked(notice.ActiveSetoffAmount + notice.WalletCollectionAmount);
            Game1.addHUDMessage(new HUDMessage(i18n.Get("message.collection-payment", new
            {
                source = i18n.Get(sourceKey),
                amount,
                interest = notice.InterestPaid,
                principal = notice.PrincipalPaid,
                remaining = notice.RemainingDebt
            }), HUDMessage.error_type));
        }

        private void OnSaving(object sender, SavingEventArgs e)
        {
            if (!Context.IsMainPlayer || bank == null) return;
            if (diagnostics.Enabled)
                diagnostics.Log("lifecycle: saving, screenId=" + Context.ScreenId + ", mode=" + bank.State.WalletMode + ", revision=" + bank.State.Revision + ", canWrite=" + save.CanWrite);
            EnsureWalletModeAndPersist();
            if (save.CanWrite) save.Write(bank.State);
        }

        private bool EnsureWalletModeAndPersist()
        {
            if (!Context.IsMainPlayer || bank == null) return true;
            TransactionResult result;
            try
            {
                result = multiplayer.EnsureWalletMode();
            }
            catch (Exception ex)
            {
                Monitor.Log("Wallet-mode reconciliation failed unexpectedly: " + ex.Message, LogLevel.Error);
                if (save.CanWrite) save.Write(bank.State);
                ShowWalletModeRejection(TransactionResult.Rejected("wallet-mode-change-rolled-back", bank.State.Revision));
                return false;
            }
            if (result == null) return true;
            if ((result.StateChanged || result.Status != TransactionStatus.Applied) && save.CanWrite)
                save.Write(bank.State);
            if (result.Status == TransactionStatus.Applied) return true;
            if (save.CanWrite) save.Write(bank.State);
            ShowWalletModeRejection(result);
            Monitor.Log(i18n.Get("error.wallet-mode-mismatch") + " [" + result.Message + "]", LogLevel.Error);
            return false;
        }

        private void OnReturnedToTitle(object sender, ReturnedToTitleEventArgs e)
        {
            if (diagnostics.Enabled)
                diagnostics.Log("lifecycle: returned-to-title, screenId=" + Context.ScreenId + ", isMainPlayer=" + Context.IsMainPlayer + ", isWorldReady=" + Context.IsWorldReady + ", isMultiplayer=" + Context.IsMultiplayer);
            DetachWalletModeGuard();
            if (CurrentScreen.Collection != null) CurrentScreen.Collection.Detach();
            bank = null;
            CurrentScreen.NotifiedOverdueLoanIds.Clear();
            warnedAtmMarkerMissing = false;
            pendingAtmRefresh = false;
            atmMarkerRetryTicks = 0;
            multiplayer.Detach();
        }

        private void AttachWalletModeGuard()
        {
            DetachWalletModeGuard();
            if (!Context.IsMainPlayer || Game1.player == null || Game1.player.team == null) return;
            // The vanilla ledger and Game1.newDay both read Game1.player on
            // the host. Subscribe to that exact Farmer instance instead of
            // assuming MasterPlayer is the same object reference.
            observedWalletFarmer = Game1.player;
            observedWalletTeam = observedWalletFarmer.team;
            observedWalletTeam.useSeparateWallets.fieldChangeEvent += OnWalletModeChanged;
            observedWalletFarmer.changeWalletTypeTonight.fieldChangeEvent += OnWalletModeChangeRequested;
            if (diagnostics.Enabled)
                diagnostics.Log("wallet-mode: guard-attached, screenId=" + Context.ScreenId + ", playerId=" + observedWalletFarmer.UniqueMultiplayerID + ", separateWallets=" + observedWalletTeam.useSeparateWallets.Value);
        }

        private void DetachWalletModeGuard()
        {
            if (observedWalletTeam != null)
                observedWalletTeam.useSeparateWallets.fieldChangeEvent -= OnWalletModeChanged;
            if (observedWalletFarmer != null)
                observedWalletFarmer.changeWalletTypeTonight.fieldChangeEvent -= OnWalletModeChangeRequested;
            observedWalletTeam = null;
            observedWalletFarmer = null;
            restoringWalletMode = false;
            restoringWalletModeRequest = false;
        }

        private void OnWalletModeChangeRequested(NetBool field, bool oldValue, bool newValue)
        {
            if (restoringWalletModeRequest || !Context.IsMainPlayer || bank == null || oldValue || !newValue)
                return;

            var team = Game1.player == null ? observedWalletTeam : Game1.player.team;
            if (team == null) return;
            var currentMode = GetWalletMode(team.useSeparateWallets.Value);
            var targetMode = OppositeWalletMode(currentMode);
            var check = bank.CheckWalletModeMigration(targetMode);
            if (diagnostics.Enabled)
                diagnostics.Log("wallet-mode: request-preflight, screenId=" + Context.ScreenId + ", sourceMode=" + currentMode + ", targetMode=" + targetMode + ", status=" + check.Status + ", message=" + check.Message + ", revision=" + bank.State.Revision);
            if (check.Status == TransactionStatus.Applied)
                return;

            restoringWalletModeRequest = true;
            try { field.Value = false; }
            finally { restoringWalletModeRequest = false; }
            ShowWalletModeRejection(check);
        }

        private void OnWalletModeChanged(NetBool field, bool oldValue, bool newValue)
        {
            if (restoringWalletMode || !Context.IsMainPlayer || bank == null || oldValue == newValue)
                return;

            restoringWalletMode = true;
            if (diagnostics.Enabled)
                diagnostics.Log("wallet-mode: topology-changed, screenId=" + Context.ScreenId + ", oldSeparate=" + oldValue + ", newSeparate=" + newValue + ", bankMode=" + bank.State.WalletMode + ", revision=" + bank.State.Revision);
            TransactionResult result;
            try
            {
                result = multiplayer.EnsureWalletMode();
            }
            catch (Exception ex)
            {
                Monitor.Log("Wallet-mode topology callback failed unexpectedly: " + ex.Message, LogLevel.Error);
                result = TransactionResult.Rejected("wallet-mode-change-rolled-back", bank.State.Revision);
            }
            finally
            {
                restoringWalletMode = false;
            }
            if (result != null && result.Status == TransactionStatus.Applied)
            {
                if (result.StateChanged && save.CanWrite) save.Write(bank.State);
                multiplayer.BroadcastSnapshot();
                QueueAtmRefresh("wallet-mode-changed");
                return;
            }

            if (save.CanWrite) save.Write(bank.State);
            ShowWalletModeRejection(result ?? TransactionResult.Rejected("wallet-mode-change-rolled-back", bank.State.Revision));
        }

        private void ShowWalletModeRejection(TransactionResult result)
        {
            if (result == null)
                result = TransactionResult.Rejected("wallet-mode-change-rolled-back", bank == null ? 0 : bank.State.Revision);
            if (bank != null && bank.State.ModeMismatch)
                bank.ClearModeMismatch();
            var message = GetWalletModeRejectionMessage(result);
            if (Context.IsWorldReady)
            {
                // The pending vanilla ledger request has already been reset,
                // but the player should receive a modal in-game explanation,
                // not only a transient HUD line or a console warning.
                Game1.drawObjectDialogue(message);
                Game1.addHUDMessage(new HUDMessage(message, HUDMessage.error_type));
            }
            Monitor.Log("Rejected the vanilla wallet-mode request: " + result.Message + ". " + (result.Diagnostic ?? ""), LogLevel.Warn);
        }

        private string GetWalletModeRejectionMessage(TransactionResult result)
        {
            if (result != null && result.Message == "wallet-mode-migration-open-products")
                return i18n.Get("error." + result.Message);
            return i18n.Get("error.wallet-mode-change-rolled-back");
        }

        private void CancelPendingWalletModeRequest()
        {
            if (restoringWalletModeRequest) return;
            var farmer = Game1.player ?? observedWalletFarmer;
            if (farmer == null || !farmer.changeWalletTypeTonight.Value) return;

            restoringWalletModeRequest = true;
            try { farmer.changeWalletTypeTonight.Value = false; }
            finally { restoringWalletModeRequest = false; }
        }

        private static WalletMode GetWalletMode(bool useSeparateWallets)
        {
            return useSeparateWallets ? WalletMode.Personal : WalletMode.Shared;
        }

        private static WalletMode OppositeWalletMode(WalletMode mode)
        {
            return mode == WalletMode.Personal ? WalletMode.Shared : WalletMode.Personal;
        }

        private static decimal TotalActiveBalance(BankState state)
        {
            if (state == null) return 0;
            if (state.WalletMode == WalletMode.Shared)
                return state.SharedAccount == null ? 0 : state.SharedAccount.ActiveBalance;

            decimal total = 0;
            if (state.PersonalAccounts != null)
                foreach (var account in state.PersonalAccounts.Values)
                    if (account != null)
                        total += account.ActiveBalance;
            return total;
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (bank == null || !IsAtmEnabled() || !Context.IsWorldReady || Game1.activeClickableMenu != null || Game1.currentLocation == null || Game1.eventUp || Game1.player.isMoving())
            {
                LogAtmOpenBlocked(e);
                return;
            }
            if (Game1.currentLocation.NameOrUniqueName != "ManorHouse" || !IsPlayerNearAtm(Game1.player))
            {
                LogAtmOpenBlocked(e);
                return;
            }

            var shouldOpen = e.Button == SButton.MouseRight
                ? IsAtmVisualTile(e.Cursor.GrabTile)
                : e.Button.IsActionButton() && IsFacingAtm(Game1.player);
            if (!shouldOpen)
            {
                LogAtmOpenBlocked(e);
                return;
            }

            Helper.Input.Suppress(e.Button);
            if (diagnostics.Enabled)
                diagnostics.Log("atm-access: open-approved, screenId=" + Context.ScreenId + ", playerId=" + Game1.player.UniqueMultiplayerID + ", button=" + e.Button + ", tile=" + Game1.player.TilePoint + ", facing=" + Game1.player.FacingDirection + ", revision=" + bank.State.Revision);
            Game1.activeClickableMenu = new AtmMenu(bank, multiplayer, Game1.player, i18n, inputConfig, diagnostics);
        }

        private void LogAtmOpenBlocked(ButtonPressedEventArgs e)
        {
            if (!diagnostics.Enabled) return;
            if (Game1.currentLocation == null || Game1.currentLocation.NameOrUniqueName != "ManorHouse") return;
            if (e.Button != SButton.MouseRight && !e.Button.IsActionButton()) return;

            var reason = bank == null ? "bank-null"
                : !IsAtmEnabled() ? "atm-disabled"
                : !Context.IsWorldReady ? "world-not-ready"
                : Game1.activeClickableMenu != null ? "menu-open"
                : Game1.currentLocation == null ? "location-null"
                : Game1.eventUp ? "event-up"
                : Game1.player.isMoving() ? "player-moving"
                : !IsPlayerNearAtm(Game1.player) ? "not-near-atm"
                : !IsFacingAtm(Game1.player) ? "not-facing"
                : "unknown";
            if (reason == lastAtmBlockReason) return;
            lastAtmBlockReason = reason;
            diagnostics.Log("atm-access: open-blocked, reason=" + reason + ", screenId=" + Context.ScreenId + ", playerId=" + (Game1.player == null ? 0 : Game1.player.UniqueMultiplayerID) + ", isMainPlayer=" + Context.IsMainPlayer + ", isMultiplayer=" + Context.IsMultiplayer);
        }

        private void OnButtonsChanged(object sender, ButtonsChangedEventArgs e)
        {
            var menu = Game1.activeClickableMenu as AtmMenu;
            if (menu == null || inputConfig == null) return;

            if (TryHandleMenuBinding(inputConfig.KeyboardClose, menu.HandleBack)) return;
            if (TryHandleMenuBinding(inputConfig.ControllerBack, menu.HandleBack)) return;
            if (TryHandleMenuBinding(inputConfig.KeyboardNextPage, () => menu.HandlePage(1))) return;
            if (TryHandleMenuBinding(inputConfig.ControllerNextPage, () => menu.HandlePage(1))) return;
            if (TryHandleMenuBinding(inputConfig.ControllerPreviousPage, () => menu.HandlePage(-1))) return;
            if (TryHandleMenuBinding(inputConfig.ControllerUp, () => menu.HandleNavigation(0, -1))) return;
            if (TryHandleMenuBinding(inputConfig.ControllerDown, () => menu.HandleNavigation(0, 1))) return;
            if (TryHandleMenuBinding(inputConfig.ControllerLeft, () => menu.HandleNavigation(-1, 0))) return;
            if (TryHandleMenuBinding(inputConfig.ControllerRight, () => menu.HandleNavigation(1, 0))) return;
            TryHandleMenuBinding(inputConfig.ControllerConfirm, menu.HandleConfirm);
        }

        private bool TryHandleMenuBinding(KeybindList binding, Action action)
        {
            if (binding == null || !binding.JustPressed()) return false;
            Helper.Input.SuppressActiveKeybinds(binding);
            action();
            return true;
        }

        private void OnModMessageReceived(object sender, StardewModdingAPI.Events.ModMessageReceivedEventArgs e)
        {
            multiplayer.HandleMessage(e);
        }

        private void OnPeerConnected(object sender, PeerConnectedEventArgs e)
        {
            if (diagnostics.Enabled)
                diagnostics.Log("multiplayer-lifecycle: peer-connected, screenId=" + Context.ScreenId + ", peerId=" + e.Peer.PlayerID + ", isMainPlayer=" + Context.IsMainPlayer + ", bankNull=" + (bank == null));
            if (Context.IsMainPlayer && bank != null)
            {
                collection.OnPeerConnected(e.Peer.PlayerID, (int)Game1.stats.DaysPlayed);
                multiplayer.SendSnapshotTo(e.Peer.PlayerID);
            }
        }

        private void OnPeerDisconnected(object sender, PeerDisconnectedEventArgs e)
        {
            if (diagnostics.Enabled)
                diagnostics.Log("multiplayer-lifecycle: peer-disconnected, screenId=" + Context.ScreenId + ", peerId=" + e.Peer.PlayerID + ", isMainPlayer=" + Context.IsMainPlayer + ", bankNull=" + (bank == null));
            multiplayer.ForgetPeer(e.Peer.PlayerID);
            if (CurrentScreen.Collection != null) CurrentScreen.Collection.OnPeerDisconnected(e.Peer.PlayerID);
        }

        internal static bool IsPlayerNearAtm(Farmer farmer)
        {
            if (farmer == null || farmer.currentLocation == null || farmer.currentLocation.NameOrUniqueName != "ManorHouse") return false;
            if (!HasAtmMarker(farmer.currentLocation)) return false;
            var playerTile = new Vector2(farmer.TilePoint.X, farmer.TilePoint.Y);
            return DistanceToAtm(playerTile) <= 1.5f;
        }

        private static bool IsFacingAtm(Farmer farmer)
        {
            var facing = farmer.TilePoint;
            if (farmer.FacingDirection == Game1.up) facing.Y--;
            else if (farmer.FacingDirection == Game1.right) facing.X++;
            else if (farmer.FacingDirection == Game1.down) facing.Y++;
            else if (farmer.FacingDirection == Game1.left) facing.X--;
            foreach (var tile in AtmActionTiles)
                if (facing == tile) return true;
            return false;
        }

        private static bool IsAtmVisualTile(Vector2 tile)
        {
            return tile.X >= AtmOriginTile.X && tile.X < AtmOriginTile.X + 2
                && tile.Y >= AtmOriginTile.Y && tile.Y < AtmOriginTile.Y + 2;
        }

        private static bool HasAtmMarker(GameLocation location)
        {
            return location != null && location.Map != null
                && location.Map.Properties.ContainsKey(AtmMapProperty)
                && location.Map.Properties[AtmMapProperty].ToString() == AtmMapPropertyValue;
        }

        private static float DistanceToAtm(Vector2 tile)
        {
            var distance = float.MaxValue;
            foreach (var actionTile in AtmActionTiles)
                distance = Math.Min(distance, Vector2.Distance(tile, new Vector2(actionTile.X, actionTile.Y)));
            return distance;
        }

        private void OnWarped(object sender, WarpedEventArgs e)
        {
            if (!e.IsLocalPlayer) return;
            e.OldLocation?.removeTemporarySpritesWithIDLocal(AtmSpriteId);
            if (e.NewLocation == null || e.NewLocation.NameOrUniqueName != "ManorHouse") return;
            if (diagnostics.Enabled)
                diagnostics.Log("atm-performance: manor-warp, screenId=" + Context.ScreenId
                    + ", markerPresent=" + HasAtmMarker(e.NewLocation)
                    + ", pendingBefore=" + pendingAtmRefresh
                    + ", spriteCount=" + e.NewLocation.temporarySprites.Count);
            QueueAtmRefresh("warped-to-manor");
        }

        private void OnAssetRequested(object sender, AssetRequestedEventArgs e)
        {
            if (e.NameWithoutLocale.IsEquivalentTo(AtmAssetName))
            {
                if (diagnostics.Enabled)
                    diagnostics.Log("content: atm-texture-requested, asset=" + e.NameWithoutLocale);
                e.LoadFromModFile<Texture2D>("assets/atm.png", AssetLoadPriority.Medium);
            }
        }

        private void OnAssetsInvalidated(object sender, AssetsInvalidatedEventArgs e)
        {
            if (e.NamesWithoutLocale.Any(name => name.IsEquivalentTo("Maps/ManorHouse")))
            {
                if (!Context.IsWorldReady && bank == null)
                {
                    if (diagnostics.Enabled)
                        diagnostics.Log("content: manor-map-invalidation-ignored, screenId=" + Context.ScreenId + ", reason=no-loaded-world");
                    return;
                }
                if (diagnostics.Enabled)
                    diagnostics.Log("content: manor-map-invalidated, screenId=" + Context.ScreenId);
                QueueAtmRefresh("manor-map-invalidated");
            }
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (Context.IsWorldReady && Context.IsMainPlayer && bank != null)
                collection.UpdateTicked((int)Game1.stats.DaysPlayed);
            var measureAtmUpdate = diagnostics.Enabled && Context.IsWorldReady && Game1.currentLocation != null
                && Game1.currentLocation.NameOrUniqueName == "ManorHouse";
            var updateStartedAt = measureAtmUpdate ? Stopwatch.GetTimestamp() : 0;
            try
            {
            // Net fields can be updated by vanilla/network code without
            // passing through the local ledger callback. Keep the actual game
            // topology and the authoritative bank topology aligned as a
            // last-resort guard, and immediately roll back a rejected change.
            if (Context.IsWorldReady && Context.IsMainPlayer && bank != null && Game1.player != null && Game1.player.team != null)
            {
                // Save loading normally establishes these references once,
                // but the host can replace the Farmer/Team net objects while
                // a multiplayer world is being rebuilt. Rebind before the
                // fallback check so the next request is observed by the same
                // objects vanilla will read.
                if (Game1.player != observedWalletFarmer || Game1.player.team != observedWalletTeam)
                    AttachWalletModeGuard();

                // Fallback for the ledger preflight: if a switch request is
                // still pending but the bank would reject it, cancel it so the
                // next ledger interaction behaves as if no request was made.
                var request = Game1.player.changeWalletTypeTonight;
                if (request != null && request.Value)
                {
                    var currentMode = GetWalletMode(Game1.player.team.useSeparateWallets.Value);
                    var check = bank.CheckWalletModeMigration(OppositeWalletMode(currentMode));
                    if (check.Status != TransactionStatus.Applied)
                    {
                        CancelPendingWalletModeRequest();
                        ShowWalletModeRejection(check);
                    }
                }

                var gameMode = GetWalletMode(Game1.player.team.useSeparateWallets.Value);
                if (gameMode != bank.State.WalletMode || bank.State.ModeMismatch)
                    EnsureWalletModeAndPersist();
            }

            if (!pendingAtmRefresh || !Context.IsWorldReady) return;
            var location = Game1.getLocationFromName("ManorHouse");
            if (location == null) return;

            if (IsAtmEnabled() && !HasAtmMarker(location) && atmMarkerRetryTicks > 0)
            {
                atmMarkerRetryTicks--;
                return;
            }

            pendingAtmRefresh = false;
            RefreshAtmSprite(location);
            if (!IsAtmEnabled() || HasAtmMarker(location))
            {
                warnedAtmMarkerMissing = false;
                return;
            }
            if (!warnedAtmMarkerMissing)
            {
                warnedAtmMarkerMissing = true;
                Monitor.Log("The Simple Bank ATM marker is missing from ManorHouse (14,8)-(15,9); the CP patch may be disabled or another map patch may have replaced it.", LogLevel.Warn);
            }
            }
            finally
            {
                if (measureAtmUpdate)
                    LogSlowAtmUpdate(updateStartedAt);
            }
        }

        private void QueueAtmRefresh()
        {
            QueueAtmRefresh("atm-availability-changed");
        }

        private void QueueAtmRefresh(string reason)
        {
            if (pendingAtmRefresh)
            {
                if (diagnostics.Enabled)
                {
                    CurrentScreen.AtmRefreshCoalescedRequests++;
                    if (CurrentScreen.AtmRefreshCoalescedRequests == 10
                        || CurrentScreen.AtmRefreshCoalescedRequests % 60 == 0)
                    {
                        diagnostics.Log("atm-performance: refresh-queue-storm, screenId=" + Context.ScreenId
                            + ", sequence=" + CurrentScreen.AtmRefreshSequence
                            + ", coalesced=" + CurrentScreen.AtmRefreshCoalescedRequests
                            + ", latestReason=" + reason);
                    }
                }
                return;
            }

            pendingAtmRefresh = true;
            atmMarkerRetryTicks = AtmMarkerRetryTicks;
            if (diagnostics.Enabled)
            {
                CurrentScreen.AtmRefreshSequence++;
                CurrentScreen.AtmRefreshCoalescedRequests = 0;
                CurrentScreen.AtmRefreshQueuedAt = Stopwatch.GetTimestamp();
                diagnostics.Log("atm-performance: refresh-queued, screenId=" + Context.ScreenId
                    + ", sequence=" + CurrentScreen.AtmRefreshSequence
                    + ", reason=" + reason);
            }
        }

        private void RefreshAtmSprite(GameLocation location)
        {
            if (location == null || location.NameOrUniqueName != "ManorHouse") return;
            var traceRefresh = diagnostics.Enabled;
            var startedAt = traceRefresh ? Stopwatch.GetTimestamp() : 0;
            var totalStartedAt = traceRefresh ? CurrentScreen.AtmRefreshQueuedAt : 0;
            var spriteCountBefore = traceRefresh ? location.temporarySprites.Count : 0;
            var markerPresent = HasAtmMarker(location);
            location.removeTemporarySpritesWithIDLocal(AtmSpriteId);
            if (!IsAtmEnabled() || !markerPresent)
            {
                if (traceRefresh)
                    LogAtmRefreshCompleted(startedAt, totalStartedAt, spriteCountBefore, location.temporarySprites.Count, markerPresent, false);
                return;
            }

            var position = new Vector2(AtmOriginTile.X * Game1.tileSize, AtmOriginTile.Y * Game1.tileSize);
            var bottomPixel = (AtmOriginTile.Y + 2) * Game1.tileSize - 1;
            var sprite = new TemporaryAnimatedSprite(
                AtmAssetName,
                new Rectangle(0, 0, 32, 32),
                999999f,
                1,
                int.MaxValue,
                position,
                false,
                false,
                bottomPixel / 10000f,
                0f,
                Color.White,
                4f,
                0f,
                0f,
                0f,
                false)
            {
                id = AtmSpriteId
            };
            location.temporarySprites.Add(sprite);
            if (traceRefresh)
                LogAtmRefreshCompleted(startedAt, totalStartedAt, spriteCountBefore, location.temporarySprites.Count, markerPresent, true);
        }

        private void LogAtmRefreshCompleted(long startedAt, long totalStartedAt, int spriteCountBefore, int spriteCountAfter, bool markerPresent, bool spriteAdded)
        {
            diagnostics.Log("atm-performance: refresh-completed, screenId=" + Context.ScreenId
                + ", sequence=" + CurrentScreen.AtmRefreshSequence
                + ", refreshMs=" + ElapsedMilliseconds(startedAt).ToString("0.###", CultureInfo.InvariantCulture)
                + ", queuedMs=" + ElapsedMilliseconds(totalStartedAt).ToString("0.###", CultureInfo.InvariantCulture)
                + ", markerWaitTicks=" + (AtmMarkerRetryTicks - atmMarkerRetryTicks)
                + ", coalesced=" + CurrentScreen.AtmRefreshCoalescedRequests
                + ", markerPresent=" + markerPresent
                + ", spriteAdded=" + spriteAdded
                + ", spritesBefore=" + spriteCountBefore
                + ", spritesAfter=" + spriteCountAfter);
        }

        private void LogSlowAtmUpdate(long startedAt)
        {
            var elapsed = ElapsedMilliseconds(startedAt);
            if (elapsed < SlowAtmUpdateThresholdMilliseconds) return;

            var now = Stopwatch.GetTimestamp();
            if (CurrentScreen.LastSlowAtmUpdateLogAt != 0
                && now - CurrentScreen.LastSlowAtmUpdateLogAt < Stopwatch.Frequency * 2)
            {
                CurrentScreen.SuppressedSlowAtmUpdates++;
                return;
            }

            diagnostics.Log("atm-performance: slow-update, screenId=" + Context.ScreenId
                + ", elapsedMs=" + elapsed.ToString("0.###", CultureInfo.InvariantCulture)
                + ", pendingRefresh=" + pendingAtmRefresh
                + ", markerRetryTicks=" + atmMarkerRetryTicks
                + ", suppressedSinceLast=" + CurrentScreen.SuppressedSlowAtmUpdates);
            CurrentScreen.LastSlowAtmUpdateLogAt = now;
            CurrentScreen.SuppressedSlowAtmUpdates = 0;
        }

        private static double ElapsedMilliseconds(long startedAt)
        {
            if (startedAt <= 0) return 0;
            return (Stopwatch.GetTimestamp() - startedAt) * 1000d / Stopwatch.Frequency;
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            var contentPatcher = Helper.ModRegistry.GetApi<IContentPatcherApi>("Pathoschild.ContentPatcher");
            if (contentPatcher == null)
                Monitor.Log("Content Patcher API was unavailable; the ATM map token could not be registered.", LogLevel.Error);
            else
            {
                contentPatcher.RegisterToken(ModManifest, "EnableAtm", () => new[] { IsAtmEnabled() ? "true" : "false" });
                if (diagnostics.Enabled)
                    diagnostics.Log("integration: content-patcher-token-registered, token=EnableAtm");
            }

            var api = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (api == null) { Monitor.Log(i18n.Get("gmcm.missing"), LogLevel.Info); return; }
            if (diagnostics.Enabled)
                diagnostics.Log("integration: gmcm-registering");
            configBeforeMenu = config.Clone();
            inputConfigBeforeMenu = inputConfig.Clone();
            api.Register(ModManifest, reset: delegate { config = new BankConfig(); inputConfig = new InputConfig(); }, save: SaveConfigFromMenu);
            api.AddSectionTitle(ModManifest, () => i18n.Get("config.atm.section"), () => i18n.Get("config.atm.section.tooltip"));
            api.AddBoolOption(ModManifest, () => config.EnableAtm, value => config.EnableAtm = value, () => i18n.Get("config.atm-enabled.name"), () => i18n.Get("config.atm-enabled.tooltip"));
            api.AddSectionTitle(ModManifest, () => i18n.Get("config.debug.section"), () => i18n.Get("config.debug.section.tooltip"));
            api.AddBoolOption(ModManifest, () => config.DebugLogging, value => config.DebugLogging = value, () => i18n.Get("config.debug-enabled.name"), () => i18n.Get("config.debug-enabled.tooltip"));
            api.AddSectionTitle(ModManifest, () => i18n.Get("config.controls.section"), () => i18n.Get("config.controls.section.tooltip"));
            api.AddKeybindList(ModManifest, () => inputConfig.KeyboardNextPage, value => inputConfig.KeyboardNextPage = value, () => i18n.Get("config.keyboard-page.name"), () => i18n.Get("config.keyboard-page.tooltip"));
            api.AddKeybindList(ModManifest, () => inputConfig.KeyboardClose, value => inputConfig.KeyboardClose = value, () => i18n.Get("config.keyboard-close.name"), () => i18n.Get("config.keyboard-close.tooltip"));
            api.AddKeybindList(ModManifest, () => inputConfig.ControllerPreviousPage, value => inputConfig.ControllerPreviousPage = value, () => i18n.Get("config.controller-previous-page.name"), () => i18n.Get("config.controller-previous-page.tooltip"));
            api.AddKeybindList(ModManifest, () => inputConfig.ControllerNextPage, value => inputConfig.ControllerNextPage = value, () => i18n.Get("config.controller-next-page.name"), () => i18n.Get("config.controller-next-page.tooltip"));
            api.AddKeybindList(ModManifest, () => inputConfig.ControllerConfirm, value => inputConfig.ControllerConfirm = value, () => i18n.Get("config.controller-confirm.name"), () => i18n.Get("config.controller-confirm.tooltip"));
            api.AddKeybindList(ModManifest, () => inputConfig.ControllerBack, value => inputConfig.ControllerBack = value, () => i18n.Get("config.controller-back.name"), () => i18n.Get("config.controller-back.tooltip"));
            api.AddKeybindList(ModManifest, () => inputConfig.ControllerUp, value => inputConfig.ControllerUp = value, () => i18n.Get("config.controller-up.name"), () => i18n.Get("config.controller-up.tooltip"));
            api.AddKeybindList(ModManifest, () => inputConfig.ControllerDown, value => inputConfig.ControllerDown = value, () => i18n.Get("config.controller-down.name"), () => i18n.Get("config.controller-down.tooltip"));
            api.AddKeybindList(ModManifest, () => inputConfig.ControllerLeft, value => inputConfig.ControllerLeft = value, () => i18n.Get("config.controller-left.name"), () => i18n.Get("config.controller-left.tooltip"));
            api.AddKeybindList(ModManifest, () => inputConfig.ControllerRight, value => inputConfig.ControllerRight = value, () => i18n.Get("config.controller-right.name"), () => i18n.Get("config.controller-right.tooltip"));

            api.AddSectionTitle(ModManifest, () => i18n.Get("config.deposit.section"), () => i18n.Get("config.deposit.section.tooltip"));
            api.AddTextOption(ModManifest, () => FormatConfigDecimal(config.ActivityDailyRatePercent), value => SetRateText(value, BankLimits.MaximumDepositDailyRatePercent, parsed => config.ActivityDailyRatePercent = parsed), () => i18n.Get("config.activity-rate.name"), () => i18n.Get("config.activity-rate.tooltip"));
            for (var i = 0; i < 10; i++)
            {
                var termIndex = i;
                api.AddBoolOption(ModManifest, () => config.TermProducts[termIndex].Enabled, value => config.TermProducts[termIndex].Enabled = value, () => i18n.Get("config.term-enabled.name", new { slot = termIndex + 1 }), () => i18n.Get("config.term-enabled.tooltip", new { slot = termIndex + 1 }));
                api.AddNumberOption(ModManifest, () => config.TermProducts[termIndex].Days, value => config.TermProducts[termIndex].Days = value, () => i18n.Get("config.term-days.name", new { slot = termIndex + 1 }), () => i18n.Get("config.term-days.tooltip", new { slot = termIndex + 1 }), 1, 1120, 1);
                api.AddTextOption(ModManifest, () => FormatConfigDecimal(config.TermProducts[termIndex].DailyRatePercent), value => SetRateText(value, BankLimits.MaximumDepositDailyRatePercent, parsed => config.TermProducts[termIndex].DailyRatePercent = parsed), () => i18n.Get("config.term-rate.name", new { slot = termIndex + 1 }), () => i18n.Get("config.term-rate.tooltip", new { slot = termIndex + 1 }));
            }

            api.AddSectionTitle(ModManifest, () => i18n.Get("config.loan.section"), () => i18n.Get("config.loan.section.tooltip"));
            for (var i = 0; i < 10; i++)
            {
                var loanIndex = i;
                api.AddBoolOption(ModManifest, () => config.LoanProducts[loanIndex].Enabled, value => config.LoanProducts[loanIndex].Enabled = value, () => i18n.Get("config.loan-enabled.name", new { slot = loanIndex + 1 }), () => i18n.Get("config.loan-enabled.tooltip", new { slot = loanIndex + 1 }));
                api.AddNumberOption(ModManifest, () => config.LoanProducts[loanIndex].Days, value => config.LoanProducts[loanIndex].Days = value, () => i18n.Get("config.loan-days.name", new { slot = loanIndex + 1 }), () => i18n.Get("config.loan-days.tooltip", new { slot = loanIndex + 1 }), 1, 1120, 1);
                api.AddTextOption(ModManifest, () => FormatConfigDecimal(config.LoanProducts[loanIndex].DailyRatePercent), value => SetRateText(value, BankLimits.MaximumLoanDailyRatePercent, parsed => config.LoanProducts[loanIndex].DailyRatePercent = parsed), () => i18n.Get("config.loan-rate.name", new { slot = loanIndex + 1 }), () => i18n.Get("config.loan-rate.tooltip", new { slot = loanIndex + 1 }));
            }
            api.AddNumberOption(ModManifest, () => (float)config.OverdueRateMultiplier, value => config.OverdueRateMultiplier = (decimal)value, () => i18n.Get("config.overdue-multiplier.name"), () => i18n.Get("config.overdue-multiplier.tooltip"), 1f, 10f, 0.05f);
            api.AddSectionTitle(ModManifest, () => i18n.Get("config.collection.section"), () => i18n.Get("config.collection.section.tooltip"));
            api.AddBoolOption(ModManifest, () => config.EnableOverdueCollection, value => config.EnableOverdueCollection = value, () => i18n.Get("config.collection-enabled.name"), () => i18n.Get("config.collection-enabled.tooltip"));
            api.AddNumberOption(ModManifest, () => config.OverdueGraceDays, value => config.OverdueGraceDays = value, () => i18n.Get("config.collection-grace.name"), () => i18n.Get("config.collection-grace.tooltip"), 0, BankLimits.MaximumCollectionDays, 1);
            api.AddBoolOption(ModManifest, () => config.EnableActiveBalanceSetoff, value => config.EnableActiveBalanceSetoff = value, () => i18n.Get("config.collection-active-setoff.name"), () => i18n.Get("config.collection-active-setoff.tooltip"));
            api.AddTextOption(ModManifest, () => FormatConfigInteger(config.MinimumActiveBalanceReserve), value => SetMoneyText(value, parsed => config.MinimumActiveBalanceReserve = (long)parsed), () => i18n.Get("config.collection-active-reserve.name"), () => i18n.Get("config.collection-active-reserve.tooltip"));
            api.AddTextOption(ModManifest, () => FormatConfigInteger(config.MinimumWalletReserve), value => SetMoneyText(value, parsed => config.MinimumWalletReserve = (long)parsed), () => i18n.Get("config.collection-wallet-reserve.name"), () => i18n.Get("config.collection-wallet-reserve.tooltip"));
            api.AddNumberOption(ModManifest, () => (float)config.IncomeGarnishmentPercent, value => config.IncomeGarnishmentPercent = (decimal)value, () => i18n.Get("config.collection-garnishment.name"), () => i18n.Get("config.collection-garnishment.tooltip"), 0f, 100f, 1f);
            api.AddNumberOption(ModManifest, () => (float)config.MaximumOverdueInterestPercent, value => config.MaximumOverdueInterestPercent = (decimal)value, () => i18n.Get("config.collection-interest-cap.name"), () => i18n.Get("config.collection-interest-cap.tooltip"), 0f, (float)BankLimits.MaximumOverdueInterestCapPercent, 5f);
            api.AddNumberOption(ModManifest, () => config.DefaultAfterDays, value => config.DefaultAfterDays = value, () => i18n.Get("config.collection-default-days.name"), () => i18n.Get("config.collection-default-days.tooltip"), 1, BankLimits.MaximumCollectionDays, 1);

            api.AddSectionTitle(ModManifest, () => i18n.Get("config.credit.section"), () => i18n.Get("config.credit.section.tooltip"));
            api.AddTextOption(ModManifest, () => FormatConfigInteger(config.BaseCredit), value => SetMoneyText(value, parsed => config.BaseCredit = parsed), () => i18n.Get("config.base-credit.name"), () => i18n.Get("config.base-credit.tooltip"));
            api.AddNumberOption(ModManifest, () => (float)config.EarningsRatio * 100, value => config.EarningsRatio = (decimal)value / 100m, () => i18n.Get("config.earnings-ratio.name"), () => i18n.Get("config.earnings-ratio.tooltip"), 0f, 100f, 1f);
            api.AddTextOption(ModManifest, () => FormatConfigInteger(config.AbsoluteCreditCap), value => SetMoneyText(value, parsed => config.AbsoluteCreditCap = parsed), () => i18n.Get("config.credit-cap.name"), () => i18n.Get("config.credit-cap.tooltip"));
        }

        private static string FormatConfigDecimal(decimal value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static string FormatConfigInteger(decimal value)
        {
            return Math.Floor(value).ToString("0", CultureInfo.InvariantCulture);
        }

        private static void SetRateText(string text, decimal maximum, Action<decimal> setValue)
        {
            decimal parsed;
            if (!TryParseConfigDecimal(text, out parsed) || parsed < 0 || parsed > maximum) return;
            setValue(BankConfig.FloorRateToFourDecimalPlaces(parsed));
        }

        private static void SetMoneyText(string text, Action<decimal> setValue)
        {
            decimal parsed;
            if (!TryParseConfigDecimal(text, out parsed) || parsed < 0 || parsed > long.MaxValue) return;
            setValue(Math.Floor(parsed));
        }

        private static bool TryParseConfigDecimal(string text, out decimal value)
        {
            return decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value)
                || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
        }

        private void SaveConfigFromMenu()
        {
            var atmSettingChanged = configBeforeMenu != null && configBeforeMenu.EnableAtm != config.EnableAtm;
            var validation = config.ValidateAndNormalize();
            if (!validation.IsValid)
            {
                Monitor.Log("Invalid config from GMCM; keeping previous valid values: " + string.Join("; ", validation.Errors), LogLevel.Warn);
                if (configBeforeMenu != null) config = configBeforeMenu.Clone();
                if (inputConfigBeforeMenu != null) inputConfig = inputConfigBeforeMenu.Clone();
                return;
            }
            inputConfig.RestoreMissingDefaults();
            Helper.WriteConfig(config);
            Helper.Data.WriteJsonFile("controls.json", inputConfig);
            if (bank != null && Context.IsMainPlayer)
            {
                bank.UpdateConfig(config);
                multiplayer.BroadcastSnapshot();
            }
            if (atmSettingChanged)
            {
                Helper.GameContent.InvalidateCache("Maps/ManorHouse");
                QueueAtmRefresh("gmcm-atm-setting-changed");
            }
            if (diagnostics.Enabled)
                diagnostics.Log("config: gmcm-saved, atmEnabled=" + config.EnableAtm + ", atmChanged=" + atmSettingChanged + ", debugLogging=" + config.DebugLogging + ", activityRate=" + config.ActivityDailyRatePercent + ", enabledTerms=" + config.TermProducts.Take(10).Count(x => x.Enabled) + ", enabledLoans=" + config.LoanProducts.Take(10).Count(x => x.Enabled));
            configBeforeMenu = config.Clone();
            inputConfigBeforeMenu = inputConfig.Clone();
        }

        private bool IsAtmEnabled()
        {
            return bank == null ? config.EnableAtm : bank.IsAtmEnabled;
        }

        private sealed class ScreenState
        {
            public BankService Bank;
            public MultiplayerCoordinator Multiplayer;
            public OverdueCollectionCoordinator Collection;
            public bool WarnedAtmMarkerMissing;
            public bool PendingAtmRefresh;
            public int AtmMarkerRetryTicks;
            public FarmerTeam ObservedWalletTeam;
            public Farmer ObservedWalletFarmer;
            public bool RestoringWalletMode;
            public bool RestoringWalletModeRequest;
            public string LastAtmBlockReason;
            public int AtmRefreshSequence;
            public int AtmRefreshCoalescedRequests;
            public long AtmRefreshQueuedAt;
            public long LastSlowAtmUpdateLogAt;
            public int SuppressedSlowAtmUpdates;
            public readonly HashSet<string> NotifiedOverdueLoanIds = new HashSet<string>(StringComparer.Ordinal);
        }

    }

    public interface IContentPatcherApi
    {
        void RegisterToken(IManifest mod, string name, Func<IEnumerable<string>> getValue);
    }

    public interface IGenericModConfigMenuApi
    {
        void Register(IManifest mod, Action reset = null, Action save = null, bool titleScreenOnly = false);
        void AddSectionTitle(IManifest mod, Func<string> text, Func<string> tooltip = null);
        void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue, Func<string> name, Func<string> tooltip = null, string fieldId = null);
        void AddNumberOption(IManifest mod, Func<float> getValue, Action<float> setValue, Func<string> name, Func<string> tooltip = null, float? min = null, float? max = null, float? interval = null, Func<float, string> formatValue = null, string fieldId = null);
        void AddNumberOption(IManifest mod, Func<int> getValue, Action<int> setValue, Func<string> name, Func<string> tooltip = null, int? min = null, int? max = null, int? interval = null, Func<int, string> formatValue = null, string fieldId = null);
        void AddTextOption(IManifest mod, Func<string> getValue, Action<string> setValue, Func<string> name, Func<string> tooltip = null, string[] allowedValues = null, Func<string, string> formatAllowedValue = null, string fieldId = null);
        void AddKeybindList(IManifest mod, Func<KeybindList> getValue, Action<KeybindList> setValue, Func<string> name, Func<string> tooltip = null, string fieldId = null);
    }
}
