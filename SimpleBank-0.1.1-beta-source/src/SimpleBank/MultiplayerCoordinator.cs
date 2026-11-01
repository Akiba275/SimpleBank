using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using SimpleBank.Core;

namespace SimpleBank
{
    internal sealed class MultiplayerCoordinator
    {
        internal const string RequestMessage = "bank-transaction-request";
        internal const string ResponseMessage = "bank-transaction-response";
        internal const string SyncMessage = "bank-state-sync";
        internal const string SyncRequestMessage = "bank-state-request";
        internal const string CollectionNoticeMessage = "loan-collection-notice";
        private readonly IModHelper helper;
        private readonly IMonitor monitor;
        private readonly ModDiagnostics diagnostics;
        private readonly RequestLimiter transactionLimiter = new RequestLimiter(8, 2000);
        private readonly RequestLimiter syncLimiter = new RequestLimiter(2, 5000);
        private BankService bank;
        private bool isAuthoritativeHost;
        private long localPlayerId;
        private long masterPlayerId;
        private bool restoringGameWalletMode;
        private readonly List<TermMaturity> termMaturities = new List<TermMaturity>();
        public long LocalPlayerId { get { return localPlayerId; } }
        public event Action<BankResponse> ResponseReceived;
        public event Action BankStateChanged;
        public event Action AtmAvailabilityChanged;
        public event Action<CollectionNotice> CollectionNoticeReceived;

        public MultiplayerCoordinator(IModHelper helper, IMonitor monitor, ModDiagnostics diagnostics)
        {
            this.helper = helper;
            this.monitor = monitor;
            this.diagnostics = diagnostics;
        }

        public void Attach(BankService bank)
        {
            this.bank = bank;
            termMaturities.Clear();
            this.isAuthoritativeHost = Context.IsMainPlayer;
            this.localPlayerId = Game1.player == null ? 0 : Game1.player.UniqueMultiplayerID;
            this.masterPlayerId = Game1.MasterPlayer == null ? 0 : Game1.MasterPlayer.UniqueMultiplayerID;
            if (diagnostics.Enabled)
                diagnostics.Log("multiplayer-lifecycle: attach, screenId=" + Context.ScreenId
                    + ", authoritativeHost=" + isAuthoritativeHost
                    + ", localPlayerId=" + localPlayerId
                    + ", masterPlayerId=" + masterPlayerId
                    + ", revision=" + bank.State.Revision);
            if (!isAuthoritativeHost)
                SendToHost("sync", SyncRequestMessage);
        }

        public void Detach()
        {
            if (diagnostics.Enabled)
                diagnostics.Log("multiplayer-lifecycle: detach, screenId=" + Context.ScreenId
                    + ", authoritativeHost=" + isAuthoritativeHost
                    + ", localPlayerId=" + localPlayerId
                    + ", bankNull=" + (bank == null));
            bank = null;
            isAuthoritativeHost = false;
            localPlayerId = 0;
            masterPlayerId = 0;
            transactionLimiter.Clear();
            syncLimiter.Clear();
            termMaturities.Clear();
        }

        public void ForgetPeer(long playerId)
        {
            transactionLimiter.Forget(playerId);
            syncLimiter.Forget(playerId);
        }

        public bool TryExecuteEarlyWithdrawal(Farmer farmer, string depositId, int absoluteDay, string transactionId, out TransactionResult result)
        {
            if (diagnostics.Enabled)
                diagnostics.Log("transaction-submit: operation=term-early, localHost=" + Context.IsMainPlayer + ", playerId=" + farmer.UniqueMultiplayerID + ", depositId=" + depositId + ", day=" + absoluteDay + ", revision=" + bank.State.Revision);
            if (!Context.IsMainPlayer)
            {
                SendToHost(new BankRequest { Operation = "term-early", PlayerId = farmer.UniqueMultiplayerID, DepositId = depositId, AbsoluteDay = absoluteDay, TransactionId = transactionId, ExpectedRevision = bank.State.Revision }, RequestMessage);
                result = null;
                return false;
            }
            bool migrationChanged;
            result = ApplyHostTransaction(farmer, new BankRequest { Operation = "term-early", PlayerId = farmer.UniqueMultiplayerID, DepositId = depositId, AbsoluteDay = absoluteDay, TransactionId = transactionId, ExpectedRevision = bank.State.Revision }, true, out migrationChanged);
            if (result.Status == TransactionStatus.Applied || migrationChanged)
            {
                NotifyBankStateChanged();
                BroadcastSnapshot();
            }
            return true;
        }

        public bool TryExecuteOrRequest(Farmer farmer, string operation, long amount, int productIndex, int absoluteDay, string transactionId, out TransactionResult result)
        {
            if (diagnostics.Enabled)
                diagnostics.Log("transaction-submit: operation=" + operation + ", localHost=" + Context.IsMainPlayer + ", playerId=" + farmer.UniqueMultiplayerID + ", amount=" + amount + ", productIndex=" + productIndex + ", day=" + absoluteDay + ", revision=" + bank.State.Revision);
            if (!Context.IsMainPlayer)
            {
                SendToHost(new BankRequest
                {
                    Operation = operation, PlayerId = farmer.UniqueMultiplayerID, Amount = amount, ProductIndex = productIndex,
                    AbsoluteDay = absoluteDay, TransactionId = transactionId, ExpectedRevision = bank.State.Revision
                }, RequestMessage);
                result = null;
                return false;
            }
            bool migrationChanged;
            result = ApplyHostTransaction(farmer, new BankRequest
            {
                Operation = operation, PlayerId = farmer.UniqueMultiplayerID, Amount = amount, ProductIndex = productIndex,
                AbsoluteDay = absoluteDay, TransactionId = transactionId, ExpectedRevision = bank.State.Revision
            }, true, out migrationChanged);
            if (result.Status == TransactionStatus.Applied || migrationChanged)
            {
                NotifyBankStateChanged();
                BroadcastSnapshot();
            }
            return true;
        }

        public void HandleMessage(ModMessageReceivedEventArgs e)
        {
            if (e.FromModID != ModEntry.UniqueId) return;
            if (bank == null)
            {
                if (diagnostics.Enabled)
                    diagnostics.Log("multiplayer-message-dropped: reason=bank-null, screenId=" + Context.ScreenId
                        + ", type=" + e.Type + ", fromPlayerId=" + e.FromPlayerID);
                return;
            }
            if (e.Type == SyncRequestMessage && isAuthoritativeHost)
            {
                var accepted = syncLimiter.TryAcquire(e.FromPlayerID);
                if (diagnostics.Enabled)
                    diagnostics.Log("multiplayer-sync: request-received, screenId=" + Context.ScreenId + ", playerId=" + e.FromPlayerID + ", accepted=" + accepted + ", revision=" + bank.State.Revision);
                if (accepted) SendSnapshotTo(e.FromPlayerID);
            }
            else if (e.Type == RequestMessage && isAuthoritativeHost)
            {
                if (!transactionLimiter.TryAcquire(e.FromPlayerID))
                {
                    if (diagnostics.Enabled)
                        diagnostics.Log("multiplayer-transaction: rate-limited, screenId=" + Context.ScreenId + ", playerId=" + e.FromPlayerID + ", type=" + e.Type);
                    return;
                }
                BankRequest request;
                if (!TryReadMessage(e, out request))
                {
                    SendResponse(e.FromPlayerID, new BankResponse { PlayerId = e.FromPlayerID, Status = TransactionStatus.Rejected, Message = "invalid-player" });
                    return;
                }
                if (!IsValidRequest(request, e.FromPlayerID))
                {
                    if (diagnostics.Enabled)
                        diagnostics.Log("multiplayer-transaction: invalid-request, screenId=" + Context.ScreenId + ", senderId=" + e.FromPlayerID + ", claimedPlayerId=" + (request == null ? 0 : request.PlayerId) + ", operation=" + (request == null ? "none" : request.Operation));
                    SendResponse(e.FromPlayerID, new BankResponse { PlayerId = e.FromPlayerID, Status = TransactionStatus.Rejected, Message = "invalid-player" });
                    return;
                }
                request.AbsoluteDay = (int)Game1.stats.DaysPlayed;
                if (diagnostics.Enabled)
                    diagnostics.Log("transaction-received: screenId=" + Context.ScreenId + ", operation=" + request.Operation + ", playerId=" + request.PlayerId + ", amount=" + request.Amount + ", expectedRevision=" + request.ExpectedRevision + ", authoritativeRevision=" + bank.State.Revision);
                var farmer = Game1.getOnlineFarmers().FirstOrDefault(x => x.UniqueMultiplayerID == request.PlayerId);
                bool migrationChanged = false;
                var result = farmer == null
                    ? TransactionResult.Rejected("player-not-online", bank.State.Revision)
                    : ApplyHostTransaction(farmer, request, false, out migrationChanged);
                if (result.Status == TransactionStatus.Applied || migrationChanged)
                {
                    // Publish the authoritative state before acknowledging the
                    // request, so the client cannot render success over stale data.
                    NotifyBankStateChanged();
                    BroadcastSnapshot();
                }
                else if (result.Status == TransactionStatus.StaleRevision)
                {
                    SendSnapshotTo(request.PlayerId);
                }
                SendResponse(request.PlayerId, new BankResponse { PlayerId = request.PlayerId, Status = result.Status, Message = result.Message });
                if (diagnostics.Enabled)
                    diagnostics.Log("multiplayer-response: sent, screenId=" + Context.ScreenId
                        + ", playerId=" + request.PlayerId
                        + ", status=" + result.Status
                        + ", message=" + result.Message
                        + ", revision=" + bank.State.Revision);
            }
            else if (e.Type == ResponseMessage && !isAuthoritativeHost && IsFromHost(e.FromPlayerID))
            {
                BankResponse response;
                if (!TryReadMessage(e, out response)) return;
                if (response != null && response.PlayerId == localPlayerId)
                {
                    if (diagnostics.Enabled)
                        diagnostics.Log("multiplayer-response: received, screenId=" + Context.ScreenId
                            + ", localPlayerId=" + localPlayerId
                            + ", status=" + response.Status
                            + ", message=" + response.Message);
                    ResponseReceived?.Invoke(response);
                }
            }
            else if (e.Type == CollectionNoticeMessage && !isAuthoritativeHost && IsFromHost(e.FromPlayerID))
            {
                CollectionNotice notice;
                if (TryReadMessage(e, out notice) && notice != null && notice.PlayerId == localPlayerId)
                    CollectionNoticeReceived?.Invoke(notice);
            }
            else if (e.Type == SyncMessage && !isAuthoritativeHost && IsFromHost(e.FromPlayerID))
            {
                BankSyncMessage sync;
                if (!TryReadMessage(e, out sync)) return;
                if (sync != null && sync.State != null && sync.State.Revision >= bank.State.Revision)
                {
                    try
                    {
                        if (sync.Config != null)
                        {
                            var atmChanged = bank.IsAtmEnabled != sync.Config.EnableAtm;
                            bank.UpdateConfig(sync.Config);
                            if (atmChanged)
                            {
                                helper.GameContent.InvalidateCache("Maps/ManorHouse");
                                AtmAvailabilityChanged?.Invoke();
                            }
                        }
                        bank.ReplaceState(sync.State);
                        termMaturities.Clear();
                        if (sync.TermMaturities != null)
                        {
                            foreach (var receipt in sync.TermMaturities)
                            {
                                if (receipt == null || string.IsNullOrWhiteSpace(receipt.DepositId)
                                    || receipt.DepositId.Length > 128 || receipt.Amount <= 0
                                    || receipt.Day != sync.State.LastSettlementDay
                                    || (receipt.PlayerId.HasValue && receipt.PlayerId != localPlayerId))
                                {
                                    monitor.Log("Rejected an invalid term maturity receipt for player " + localPlayerId + ".", LogLevel.Warn);
                                    continue;
                                }
                                termMaturities.Add(receipt);
                            }
                        }
                        if (diagnostics.Enabled)
                            diagnostics.Log("multiplayer-sync: applied, screenId=" + Context.ScreenId
                                + ", localPlayerId=" + localPlayerId
                                + ", revision=" + bank.State.Revision);
                        NotifyBankStateChanged();
                    }
                    catch (Exception ex)
                    {
                        monitor.Log("Rejected an invalid bank snapshot from the host: " + ex.Message, LogLevel.Error);
                    }
                }
                else if (diagnostics.Enabled)
                {
                    diagnostics.Log("multiplayer-sync: ignored, screenId=" + Context.ScreenId + ", reason=" + (sync == null ? "null-message" : sync.State == null ? "null-state" : "stale-revision") + ", incomingRevision=" + (sync == null || sync.State == null ? -1 : sync.State.Revision) + ", localRevision=" + bank.State.Revision);
                }
            }
        }

        public void BroadcastSnapshot()
        {
            if (bank == null || !Context.IsMainPlayer) return;
            var configSnapshot = bank.Config;
            var hostId = masterPlayerId;
            var recipientCount = 0;
            foreach (var farmer in Game1.getOnlineFarmers())
            {
                if (farmer.UniqueMultiplayerID != hostId)
                {
                    SendSnapshotTo(farmer.UniqueMultiplayerID, configSnapshot);
                    recipientCount++;
                }
            }
            if (diagnostics.Enabled)
                diagnostics.Log("multiplayer-sync: broadcast, screenId=" + Context.ScreenId + ", revision=" + bank.State.Revision + ", recipients=" + recipientCount);
        }

        public void SendSnapshotTo(long playerId)
        {
            if (bank == null || !Context.IsMainPlayer) return;
            SendSnapshotTo(playerId, bank.Config);
        }

        public void PublishStateChanged()
        {
            NotifyBankStateChanged();
        }

        public void RecordTermMaturities(TransactionResult result, int day)
        {
            if (!isAuthoritativeHost || bank == null || result == null || result.Status != TransactionStatus.Applied) return;
            termMaturities.RemoveAll(receipt => receipt.Day != day);
            foreach (var receipt in result.TermMaturities)
            {
                if (receipt.Day == day && !termMaturities.Any(existing => existing.DepositId == receipt.DepositId && existing.PlayerId == receipt.PlayerId))
                    termMaturities.Add(receipt);
            }
        }

        public IEnumerable<TermMaturity> GetLocalTermMaturities(int day)
        {
            return GetTermMaturities(localPlayerId, day);
        }

        private IEnumerable<TermMaturity> GetTermMaturities(long playerId, int day)
        {
            return termMaturities.Where(receipt => receipt.Day == day && (!receipt.PlayerId.HasValue || receipt.PlayerId == playerId));
        }

        public void PublishCollectionNotice(long playerId, string operation, TransactionResult result)
        {
            if (bank == null || !Context.IsMainPlayer || result == null) return;
            var notice = new CollectionNotice
            {
                PlayerId = playerId,
                Operation = operation,
                ActiveSetoffAmount = result.ActiveSetoffAmount,
                WalletCollectionAmount = result.WalletCollectionAmount,
                InterestPaid = result.InterestPaid,
                PrincipalPaid = result.PrincipalPaid,
                RemainingDebt = result.RemainingDebt
            };
            if (playerId == localPlayerId) CollectionNoticeReceived?.Invoke(notice);
            else helper.Multiplayer.SendMessage(notice, CollectionNoticeMessage, new[] { ModEntry.UniqueId }, new[] { playerId });
        }

        private void SendSnapshotTo(long playerId, BankConfig configSnapshot)
        {
            if (diagnostics.Enabled)
                diagnostics.Log("multiplayer-sync: snapshot-sent, screenId=" + Context.ScreenId + ", playerId=" + playerId + ", mode=" + bank.State.WalletMode + ", revision=" + bank.State.Revision);
            helper.Multiplayer.SendMessage(new BankSyncMessage
            {
                State = CreateClientState(playerId), Config = configSnapshot,
                TermMaturities = GetTermMaturities(playerId, (int)Game1.stats.DaysPlayed).ToList()
            }, SyncMessage, new[] { ModEntry.UniqueId }, new[] { playerId });
        }

        private BankState CreateClientState(long playerId)
        {
            var source = bank.State;
            var filtered = new BankState
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
            Account account;
            if (source.WalletMode == WalletMode.Personal && source.PersonalAccounts.TryGetValue(playerId, out account))
                filtered.PersonalAccounts[playerId] = account.Clone();
            return filtered;
        }

        private void SendResponse(long playerId, BankResponse response)
        {
            helper.Multiplayer.SendMessage(response, ResponseMessage, new[] { ModEntry.UniqueId }, new[] { playerId });
        }

        private void SendToHost<T>(T message, string type)
        {
            if (diagnostics.Enabled)
                diagnostics.Log("multiplayer-message: sent-to-host, screenId=" + Context.ScreenId + ", localPlayerId=" + localPlayerId + ", hostPlayerId=" + masterPlayerId + ", type=" + type);
            if (masterPlayerId == 0) helper.Multiplayer.SendMessage(message, type, new[] { ModEntry.UniqueId });
            else helper.Multiplayer.SendMessage(message, type, new[] { ModEntry.UniqueId }, new[] { masterPlayerId });
        }

        internal TransactionResult EnsureWalletMode()
        {
            if (bank == null || !Context.IsMainPlayer || Game1.player == null || Game1.player.team == null)
                return null;

            var targetMode = Game1.player.team.useSeparateWallets.Value ? WalletMode.Personal : WalletMode.Shared;
            if (diagnostics.Enabled)
                diagnostics.Log("wallet-mode: ensure, screenId=" + Context.ScreenId + ", bankMode=" + bank.State.WalletMode + ", gameMode=" + targetMode + ", modeMismatch=" + bank.State.ModeMismatch + ", revision=" + bank.State.Revision);
            if (bank.State.WalletMode == targetMode && !bank.State.ModeMismatch)
            {
                if (diagnostics.Enabled)
                    diagnostics.Log("wallet-mode: already-aligned, screenId=" + Context.ScreenId + ", mode=" + targetMode + ", revision=" + bank.State.Revision);
                return TransactionResult.Applied("wallet-mode-already-set", bank.State.Revision);
            }

            // Validate before setting the transient mismatch flag. The
            // vanilla ledger may still be in the middle of its overnight
            // request; a rejected request must leave the bank available once
            // the game field is restored to the previous topology.
            var check = bank.CheckWalletModeMigration(targetMode);
            if (diagnostics.Enabled)
                diagnostics.Log("wallet-mode: migration-check, screenId=" + Context.ScreenId + ", targetMode=" + targetMode + ", status=" + check.Status + ", message=" + check.Message + ", revision=" + bank.State.Revision);
            if (check.Status != TransactionStatus.Applied)
            {
                // The vanilla ledger normally rolls this field back through
                // ModEntry. Keep the safety boundary here too, so a missed
                // NetBool callback cannot leave the game using a wallet graph
                // that the bank rejected.
                RestoreGameWalletModeToBankState();
                CancelPendingWalletModeChange();
                return check;
            }

            // The host submits and displays transactions through Game1.player,
            // so the migrated personal account must be keyed by that same
            // stable multiplayer ID.  MasterPlayer can be a different object
            // reference while the vanilla overnight routine is rebuilding the
            // farmer/team graph; using its ID here could otherwise strand the
            // migrated assets under a different dictionary key than the one
            // every later deposit uses.
            var ownerId = localPlayerId;
            // Only a real topology change needs the transient mismatch lock.
            // Same-mode reconciliation can clear a stale transient marker
            // without blocking transactions or changing the bank revision.
            if (bank.State.WalletMode != targetMode)
                bank.State.ModeMismatch = true;
            var result = bank.MigrateWalletMode(targetMode, ownerId, bank.State.Revision);
            if (result.Status != TransactionStatus.Applied)
            {
                bank.ClearModeMismatch();
                RestoreGameWalletModeToBankState();
                CancelPendingWalletModeChange();
                monitor.Log("Wallet-mode migration was not applied: " + result.Message + ". "
                    + (result.Diagnostic ?? "Bank assets remain protected and transactions stay locked."), LogLevel.Error);
                return result;
            }

            if (result.StateChanged)
            {
                NotifyBankStateChanged();
                if (diagnostics.Enabled)
                    diagnostics.Log("wallet-mode-migration: screenId=" + Context.ScreenId
                        + ", sourceMode=" + result.SourceMode
                        + ", targetMode=" + result.TargetMode
                        + ", activeMoved=" + result.ActiveBalanceMoved
                        + ", sourceAccounts=" + result.SourceAccountCount
                        + ", termsMoved=" + result.TermDepositsMoved
                        + ", loansMoved=" + result.LoanContractsMoved
                        + ", revision=" + result.NewRevision);
            }
            return result;
        }

        private void NotifyBankStateChanged()
        {
            BankStateChanged?.Invoke();
        }

        private bool TryReadMessage<T>(ModMessageReceivedEventArgs e, out T message)
        {
            try { message = e.ReadAs<T>(); return true; }
            catch (Exception ex)
            {
                message = default(T);
                monitor.Log("Rejected malformed Simple Bank multiplayer message '" + e.Type + "': " + ex.Message, LogLevel.Warn);
                return false;
            }
        }

        private TransactionResult ApplyHostTransaction(Farmer farmer, BankRequest request, bool isLocalRequest, out bool migrationChanged)
        {
            migrationChanged = false;
            if (request.PlayerId != farmer.UniqueMultiplayerID) return TransactionResult.Rejected("invalid-player", bank.State.Revision);
            if (!bank.IsAtmEnabled || !ModEntry.IsPlayerNearAtm(farmer)) return TransactionResult.Rejected("atm-access-required", bank.State.Revision);
            var requestedRevision = request.ExpectedRevision;
            var migration = EnsureWalletMode();
            if (migration != null && migration.Status != TransactionStatus.Applied)
                return TransactionResult.Rejected("wallet-mode-mismatch", bank.State.Revision);
            // A mode migration is itself revisioned. The first transaction
            // after the vanilla wallet switch must use the post-migration
            // revision, otherwise the host rejects its own valid request as
            // stale before the account mutation can run.
            if (migration != null && migration.StateChanged)
            {
                migrationChanged = true;
                if (isLocalRequest)
                    request.ExpectedRevision = migration.NewRevision;
                else if (requestedRevision != migration.NewRevision)
                    return new TransactionResult { Status = TransactionStatus.StaleRevision, Message = "stale-revision", NewRevision = migration.NewRevision };
            }
            var wallet = new GameWalletAdapter(farmer, bank.State.WalletMode, diagnostics);
            TransactionResult result;
            switch (request.Operation)
            {
                case "deposit": result = bank.Deposit(request.PlayerId, wallet, request.Amount, request.TransactionId, request.ExpectedRevision); break;
                case "withdraw": result = bank.Withdraw(request.PlayerId, wallet, request.Amount, request.TransactionId, request.ExpectedRevision); break;
                case "term": result = bank.CreateTermDeposit(request.PlayerId, wallet, request.ProductIndex, request.Amount, request.AbsoluteDay, request.TransactionId, request.ExpectedRevision); break;
                case "term-active": result = bank.CreateTermDepositFromActive(request.PlayerId, request.ProductIndex, request.Amount, request.AbsoluteDay, request.TransactionId, request.ExpectedRevision); break;
                case "loan": result = bank.RequestLoan(request.PlayerId, wallet, request.ProductIndex, request.Amount, request.AbsoluteDay, request.TransactionId, request.ExpectedRevision); break;
                case "repay": result = bank.RepayLoan(request.PlayerId, wallet, request.Amount, request.AbsoluteDay, request.TransactionId, request.ExpectedRevision); break;
                case "term-early": result = bank.WithdrawTermEarly(request.PlayerId, wallet, request.DepositId, request.AbsoluteDay, request.TransactionId, request.ExpectedRevision); break;
                default: result = TransactionResult.Rejected("unknown-action", bank.State.Revision); break;
            }
            if (diagnostics.Enabled)
                diagnostics.Log("transaction-result: operation=" + request.Operation + ", playerId=" + request.PlayerId + ", status=" + result.Status + ", message=" + result.Message + ", revision=" + result.NewRevision);
            if (result.Message == "wallet-rollback-failed")
                monitor.Log("Simple Bank could not restore a wallet after a failed " + request.Operation + " transaction for player " + request.PlayerId + ".", LogLevel.Error);
            return result;
        }

        private void RestoreGameWalletModeToBankState()
        {
            if (restoringGameWalletMode || bank == null || !Context.IsMainPlayer || Game1.player == null || Game1.player.team == null)
                return;

            var expectedSeparate = bank.State.WalletMode == WalletMode.Personal;
            if (Game1.player.team.useSeparateWallets.Value == expectedSeparate) return;

            restoringGameWalletMode = true;
            try { Game1.player.team.useSeparateWallets.Value = expectedSeparate; }
            catch (Exception ex) { monitor.Log("Could not restore the game's wallet mode after a rejected Simple Bank migration: " + ex.Message, LogLevel.Error); }
            finally { restoringGameWalletMode = false; }
        }

        private static void CancelPendingWalletModeChange()
        {
            var hostPlayer = Game1.MasterPlayer ?? Game1.player;
            if (hostPlayer != null && hostPlayer.changeWalletTypeTonight.Value)
                hostPlayer.changeWalletTypeTonight.Value = false;
        }

        private bool IsFromHost(long playerId)
        {
            return masterPlayerId != 0 && playerId == masterPlayerId;
        }

        private static bool IsValidRequest(BankRequest request, long senderId)
        {
            if (request == null || request.PlayerId != senderId) return false;
            if (string.IsNullOrWhiteSpace(request.TransactionId) || request.TransactionId.Length > 128) return false;
            if (string.IsNullOrWhiteSpace(request.Operation) || request.Operation.Length > 32) return false;
            if (request.DepositId != null && request.DepositId.Length > 128) return false;
            return request.Operation == "deposit" || request.Operation == "withdraw" || request.Operation == "term" || request.Operation == "term-active"
                || request.Operation == "loan" || request.Operation == "repay" || request.Operation == "term-early";
        }

        private sealed class RequestLimiter
        {
            private readonly int maximumRequests;
            private readonly long windowMilliseconds;
            private readonly Dictionary<long, Window> windows = new Dictionary<long, Window>();

            public RequestLimiter(int maximumRequests, long windowMilliseconds)
            {
                this.maximumRequests = maximumRequests;
                this.windowMilliseconds = windowMilliseconds;
            }

            public bool TryAcquire(long playerId)
            {
                var now = Environment.TickCount64;
                Window window;
                if (!windows.TryGetValue(playerId, out window) || now - window.StartedAt >= windowMilliseconds || now < window.StartedAt)
                {
                    windows[playerId] = new Window { StartedAt = now, Count = 1 };
                    return true;
                }
                if (window.Count >= maximumRequests) return false;
                window.Count++;
                return true;
            }

            public void Forget(long playerId) { windows.Remove(playerId); }
            public void Clear() { windows.Clear(); }

            private sealed class Window
            {
                public long StartedAt { get; set; }
                public int Count { get; set; }
            }
        }
    }

    internal sealed class BankRequest
    {
        public string Operation { get; set; }
        public long PlayerId { get; set; }
        public long Amount { get; set; }
        public int ProductIndex { get; set; }
        public int AbsoluteDay { get; set; }
        public string DepositId { get; set; }
        public string TransactionId { get; set; }
        public long ExpectedRevision { get; set; }
    }

    internal sealed class BankResponse
    {
        public long PlayerId { get; set; }
        public TransactionStatus Status { get; set; }
        public string Message { get; set; }
    }

    internal sealed class BankSyncMessage
    {
        public BankState State { get; set; }
        public BankConfig Config { get; set; }
        public List<TermMaturity> TermMaturities { get; set; } = new List<TermMaturity>();
    }

    internal sealed class CollectionNotice
    {
        public long PlayerId { get; set; }
        public string Operation { get; set; }
        public long ActiveSetoffAmount { get; set; }
        public long WalletCollectionAmount { get; set; }
        public long InterestPaid { get; set; }
        public long PrincipalPaid { get; set; }
        public long RemainingDebt { get; set; }
    }
}
