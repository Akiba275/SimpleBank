using System;
using System.Collections.Generic;
using StardewModdingAPI;
using StardewValley;
using SimpleBank.Core;

namespace SimpleBank
{
    /// <summary>Host-only orchestration for semantic collection checkpoints and lightweight income sampling.</summary>
    internal sealed class OverdueCollectionCoordinator
    {
        private const int ReconciliationIntervalTicks = 60;
        private readonly IMonitor monitor;
        private readonly ModDiagnostics diagnostics;
        private readonly MultiplayerCoordinator multiplayer;
        private readonly Dictionary<long, WalletBinding> bindings = new Dictionary<long, WalletBinding>();
        private readonly HashSet<long> collectionTargetIds = new HashSet<long>();
        private readonly HashSet<long> pendingReconnectSweepIds = new HashSet<long>();
        private readonly HashSet<string> warnedInvalidBindings = new HashSet<string>(StringComparer.Ordinal);
        private BankService bank;
        private long tickSequence;
        private int ticksUntilReconciliation;
        private bool applyingBankWalletWrite;

        public OverdueCollectionCoordinator(IMonitor monitor, ModDiagnostics diagnostics, MultiplayerCoordinator multiplayer)
        {
            this.monitor = monitor;
            this.diagnostics = diagnostics;
            this.multiplayer = multiplayer;
        }

        public void Attach(BankService bank)
        {
            multiplayer.BankStateChanged -= OnBankStateChanged;
            this.bank = bank;
            bindings.Clear();
            pendingReconnectSweepIds.Clear();
            RefreshCollectionTargets();
            ticksUntilReconciliation = ReconciliationIntervalTicks;
            if (Context.IsMainPlayer && Context.IsWorldReady)
                ReconcileBindings();
            multiplayer.BankStateChanged += OnBankStateChanged;
        }

        public void Detach()
        {
            multiplayer.BankStateChanged -= OnBankStateChanged;
            bindings.Clear();
            collectionTargetIds.Clear();
            pendingReconnectSweepIds.Clear();
            warnedInvalidBindings.Clear();
            bank = null;
            applyingBankWalletWrite = false;
            tickSequence = 0;
        }

        public bool OnDayStarted(int absoluteDay)
        {
            if (!CanCollect()) return false;
            RefreshCollectionTargets();
            ReconcileBindings();
            var changed = false;
            foreach (var playerId in new List<long>(collectionTargetIds))
            {
                var loan = bank.GetAccount(playerId).Loan;
                if (loan == null) continue;
                var result = bank.CollectScheduledPayment(playerId, null, absoluteDay, true, false,
                    "collection-active-" + loan.Id + "-" + absoluteDay, bank.State.Revision);
                changed |= HandleResult("active-setoff", playerId, result);
            }
            RefreshCollectionTargets();
            foreach (var pair in bindings)
            {
                if (!collectionTargetIds.Contains(pair.Key)) continue;
                var loan = bank.GetAccount(pair.Key).Loan;
                if (loan == null) continue;
                applyingBankWalletWrite = true;
                try
                {
                    var result = bank.CollectScheduledPayment(pair.Key, pair.Value.Wallet, absoluteDay, false, true,
                        "collection-wallet-" + loan.Id + "-" + absoluteDay, bank.State.Revision);
                    changed |= HandleResult("wallet-sweep", pair.Key, result);
                    pair.Value.Baseline = pair.Value.Wallet.Balance;
                }
                finally { applyingBankWalletWrite = false; }
            }
            RefreshCollectionTargets();
            return changed;
        }

        public bool OnPeerConnected(long playerId, int absoluteDay)
        {
            if (!CanCollect()) return false;
            RefreshCollectionTargets();
            if (!collectionTargetIds.Contains(playerId))
            {
                pendingReconnectSweepIds.Remove(playerId);
                return false;
            }
            pendingReconnectSweepIds.Add(playerId);
            BindPlayer(playerId);
            return TryRunPendingReconnectSweep(playerId, absoluteDay);
        }

        public void OnPeerDisconnected(long playerId)
        {
            bindings.Remove(playerId);
            pendingReconnectSweepIds.Remove(playerId);
        }

        public void UpdateTicked(int absoluteDay)
        {
            if (!CanCollect() || applyingBankWalletWrite) return;
            if (collectionTargetIds.Count == 0) return;
            tickSequence++;
            if (--ticksUntilReconciliation <= 0)
            {
                ReconcileBindings();
                ticksUntilReconciliation = ReconciliationIntervalTicks;
            }

            var changed = false;
            if (pendingReconnectSweepIds.Count > 0)
            {
                foreach (var playerId in new List<long>(pendingReconnectSweepIds))
                    changed |= TryRunPendingReconnectSweep(playerId, absoluteDay);
            }
            foreach (var pair in bindings)
            {
                if (!collectionTargetIds.Contains(pair.Key)) continue;
                var binding = pair.Value;
                var current = binding.Wallet.Balance;
                if (current <= binding.Baseline)
                {
                    binding.Baseline = current;
                    continue;
                }

                var positiveDelta = current - binding.Baseline;
                applyingBankWalletWrite = true;
                try
                {
                    var result = bank.CollectIncomeGarnishment(pair.Key, binding.Wallet, positiveDelta, absoluteDay,
                        "collection-income-" + pair.Key + "-" + tickSequence, bank.State.Revision);
                    changed |= HandleResult("income-garnishment", pair.Key, result);
                    binding.Baseline = binding.Wallet.Balance;
                }
                finally { applyingBankWalletWrite = false; }
            }

            if (changed)
            {
                RefreshCollectionTargets();
                multiplayer.PublishStateChanged();
                multiplayer.BroadcastSnapshot();
            }
        }

        private bool TryRunPendingReconnectSweep(long playerId, int absoluteDay)
        {
            if (!collectionTargetIds.Contains(playerId))
            {
                pendingReconnectSweepIds.Remove(playerId);
                return false;
            }
            WalletBinding binding;
            if (!bindings.TryGetValue(playerId, out binding)) return false;
            var loan = bank.GetAccount(playerId).Loan;
            if (loan == null || (loan.LastWalletSweepDay.HasValue && loan.LastWalletSweepDay.Value >= absoluteDay))
            {
                pendingReconnectSweepIds.Remove(playerId);
                return false;
            }
            applyingBankWalletWrite = true;
            try
            {
                var result = bank.CollectScheduledPayment(playerId, binding.Wallet, absoluteDay, false, true,
                    "collection-wallet-" + loan.Id + "-" + absoluteDay, bank.State.Revision);
                binding.Baseline = binding.Wallet.Balance;
                if (result.Status == TransactionStatus.Applied || result.Status == TransactionStatus.Duplicate)
                    pendingReconnectSweepIds.Remove(playerId);
                var changed = HandleResult("reconnect-wallet-sweep", playerId, result);
                RefreshCollectionTargets();
                return changed;
            }
            finally { applyingBankWalletWrite = false; }
        }

        public void RefreshWalletBaselines()
        {
            RefreshCollectionTargets();
            foreach (var binding in bindings.Values)
                binding.Baseline = binding.Wallet.Balance;
        }

        private void OnBankStateChanged()
        {
            if (bank != null && Context.IsMainPlayer) RefreshCollectionTargets();
        }

        private bool CanCollect()
        {
            return bank != null && Context.IsWorldReady && Context.IsMainPlayer && !bank.State.DataInvalid && !bank.State.ModeMismatch;
        }

        private void RefreshCollectionTargets()
        {
            collectionTargetIds.Clear();
            if (bank.State.WalletMode == WalletMode.Shared)
            {
                if (bank.State.SharedAccount != null && IsCollectionLoan(bank.State.SharedAccount.Loan))
                {
                    var owner = Game1.MasterPlayer == null ? (Game1.player == null ? 0 : Game1.player.UniqueMultiplayerID) : Game1.MasterPlayer.UniqueMultiplayerID;
                    if (owner != 0) collectionTargetIds.Add(owner);
                    else WarnInvalidBinding("zero-shared-owner", "Skipped shared-wallet collection because the authoritative host ID is zero.");
                }
                return;
            }
            foreach (var pair in bank.State.PersonalAccounts)
            {
                if (pair.Value == null || !IsCollectionLoan(pair.Value.Loan)) continue;
                if (pair.Key == 0) WarnInvalidBinding("zero-personal-owner", "Skipped personal-wallet collection because an account uses player ID zero.");
                else collectionTargetIds.Add(pair.Key);
            }
        }

        private static bool IsCollectionLoan(LoanContract loan)
        {
            return loan != null && loan.LockedCollectionEnabled
                && (loan.Status == LoanStatus.Collection || loan.Status == LoanStatus.Defaulted);
        }

        private void ReconcileBindings()
        {
            var online = new HashSet<long>();
            foreach (var farmer in Game1.getOnlineFarmers())
            {
                if (farmer == null || farmer.team == null) continue;
                if (farmer.UniqueMultiplayerID == 0)
                {
                    WarnInvalidBinding("online-zero", "Skipped an online wallet binding because its player ID is zero.");
                    continue;
                }
                if (!online.Add(farmer.UniqueMultiplayerID))
                {
                    WarnInvalidBinding("duplicate-" + farmer.UniqueMultiplayerID, "Skipped a duplicate online wallet binding for player ID " + farmer.UniqueMultiplayerID + ".");
                    continue;
                }
                if (!collectionTargetIds.Contains(farmer.UniqueMultiplayerID)) continue;
                BindPlayer(farmer.UniqueMultiplayerID, farmer);
            }
            var stale = new List<long>();
            foreach (var playerId in bindings.Keys)
                if (!online.Contains(playerId)) stale.Add(playerId);
            foreach (var playerId in stale) bindings.Remove(playerId);
        }

        private void BindPlayer(long playerId)
        {
            if (playerId == 0 || !collectionTargetIds.Contains(playerId)) return;
            foreach (var farmer in Game1.getOnlineFarmers())
                if (farmer != null && farmer.UniqueMultiplayerID == playerId)
                {
                    BindPlayer(playerId, farmer);
                    return;
                }
        }

        private void BindPlayer(long playerId, Farmer farmer)
        {
            if (playerId == 0 || farmer == null || farmer.UniqueMultiplayerID != playerId || !collectionTargetIds.Contains(playerId))
            {
                WarnInvalidBinding("mismatch-" + playerId, "Skipped a wallet binding whose Farmer identity does not match the collection account.");
                return;
            }
            WalletBinding existing;
            if (bindings.TryGetValue(playerId, out existing) && ReferenceEquals(existing.Farmer, farmer)) return;
            var wallet = new GameWalletAdapter(farmer, bank.State.WalletMode, diagnostics);
            bindings[playerId] = new WalletBinding { Farmer = farmer, Wallet = wallet, Baseline = wallet.Balance };
            if (diagnostics.Enabled)
                diagnostics.Log("collection-binding: playerId=" + playerId + ", mode=" + bank.State.WalletMode + ", baseline=" + wallet.Balance);
        }

        private bool HandleResult(string operation, long playerId, TransactionResult result)
        {
            if (result == null) return false;
            if (result.Message == "wallet-rollback-failed")
                monitor.Log("Overdue collection could not restore a wallet after rollback; operation=" + operation + ", playerId=" + playerId + ".", LogLevel.Error);
            if (diagnostics.Enabled && result.Status != TransactionStatus.Duplicate)
                diagnostics.Log("collection-result: operation=" + operation + ", playerId=" + playerId + ", status=" + result.Status
                    + ", message=" + result.Message + ", active=" + result.ActiveSetoffAmount + ", wallet=" + result.WalletCollectionAmount
                    + ", interest=" + result.InterestPaid + ", principal=" + result.PrincipalPaid + ", remaining=" + result.RemainingDebt
                    + ", revision=" + result.NewRevision);
            if (result.Status == TransactionStatus.Applied && (result.ActiveSetoffAmount > 0 || result.WalletCollectionAmount > 0))
                multiplayer.PublishCollectionNotice(playerId, operation, result);
            return result.Status == TransactionStatus.Applied && result.StateChanged;
        }

        private void WarnInvalidBinding(string key, string message)
        {
            if (warnedInvalidBindings.Add(key)) monitor.Log(message, LogLevel.Warn);
        }

        private sealed class WalletBinding
        {
            public Farmer Farmer;
            public GameWalletAdapter Wallet;
            public long Baseline;
        }
    }
}
