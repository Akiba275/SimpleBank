using System;
using System.Collections.Generic;
using System.Linq;

namespace SimpleBank.Core
{
    public sealed class BankService
    {
        private BankConfig config;
        private long configRevision;
        private readonly object sync = new object();
        private readonly Func<bool> diagnosticsEnabled;
        private readonly Action<string> diagnosticLog;
        public BankState State { get; private set; }
        public BankConfig Config { get { lock (sync) return config.Clone(); } }
        public bool IsAtmEnabled { get { lock (sync) return config.EnableAtm; } }
        public long ConfigRevision { get { lock (sync) return configRevision; } }

        public BankService(BankConfig config, BankState state = null, Func<bool> diagnosticsEnabled = null, Action<string> diagnosticLog = null)
        {
            if (config == null) throw new ArgumentNullException("config");
            this.config = config.Clone();
            var validation = this.config.ValidateAndNormalize();
            if (!validation.IsValid) throw new ArgumentException(string.Join("; ", validation.Errors));
            State = state ?? new BankState();
            this.diagnosticsEnabled = diagnosticsEnabled;
            this.diagnosticLog = diagnosticLog;
            var stateErrors = BankStateValidator.Validate(State);
            if (stateErrors.Count > 0) throw new ArgumentException("Invalid bank state: " + string.Join("; ", stateErrors));
            NormalizeState(State);
            if (DiagnosticsEnabled)
                Trace("core-init: mode=" + State.WalletMode + ", revision=" + State.Revision + ", dataInvalid=" + State.DataInvalid);
        }

        public void UpdateConfig(BankConfig updated)
        {
            if (updated == null) throw new ArgumentNullException("updated");
            var clone = updated.Clone();
            var validation = clone.ValidateAndNormalize();
            if (!validation.IsValid) throw new ArgumentException(string.Join("; ", validation.Errors));
            lock (sync)
            {
                if (ConfigsEquivalent(config, clone))
                    return;
                config = clone;
                configRevision = checked(configRevision + 1);
                if (DiagnosticsEnabled)
                    Trace("core-config: updated, configRevision=" + configRevision + ", atmEnabled=" + config.EnableAtm + ", debugLogging=" + config.DebugLogging);
            }
        }

        public Account GetAccount(long playerId)
        {
            lock (sync)
            {
                if (State.WalletMode == WalletMode.Shared) return State.SharedAccount;
                Account account;
                return State.PersonalAccounts.TryGetValue(playerId, out account) ? account : new Account { Id = "player-" + playerId };
            }
        }

        public TransactionResult Deposit(long playerId, IWalletAdapter wallet, long amount, string transactionId, long expectedRevision)
        {
            return Mutate("deposit", expectedRevision, transactionId, playerId, wallet, delegate(Account account) {
                ValidateAmount(amount);
                if (!wallet.TryRemove(amount)) throw new BankOperationException("wallet-insufficient");
                if (IsCollectionPhase(account.Loan))
                {
                    var payment = Math.Min(amount, account.Loan.TotalOwed);
                    ApplyLoanPayment(account.Loan, payment, account.Loan.LastSettledDay);
                    account.ActiveBalance = checked(account.ActiveBalance + amount - payment);
                    return "deposit-applied-to-overdue-loan";
                }
                account.ActiveBalance = checked(account.ActiveBalance + amount);
                return "deposit-applied";
            });
        }

        public TransactionResult Withdraw(long playerId, IWalletAdapter wallet, long amount, string transactionId, long expectedRevision)
        {
            return Mutate("withdraw", expectedRevision, transactionId, playerId, wallet, delegate(Account account) {
                ValidateAmount(amount);
                if (IsCollectionPhase(account.Loan)) throw new BankOperationException("collection-withdrawal-blocked");
                if (amount > account.ActiveBalance) throw new BankOperationException("active-insufficient");
                if (!wallet.TryAdd(amount)) throw new BankOperationException("wallet-overflow");
                account.ActiveBalance -= amount;
                return "withdraw-applied";
            });
        }

        public TransactionResult CreateTermDeposit(long playerId, IWalletAdapter wallet, int productIndex, long amount, int absoluteDay, string transactionId, long expectedRevision)
        {
            return Mutate("term", expectedRevision, transactionId, playerId, wallet, delegate(Account account) {
                var deposit = PrepareTermDeposit(account, productIndex, amount, absoluteDay);
                if (!wallet.TryRemove(amount)) throw new BankOperationException("wallet-insufficient");
                account.TermDeposits.Add(deposit);
                return "term-created:" + deposit.Id;
            });
        }

        public TransactionResult CreateTermDepositFromActive(long playerId, int productIndex, long amount, int absoluteDay, string transactionId, long expectedRevision)
        {
            if (DiagnosticsEnabled)
                Trace("core-transaction: begin, operation=term-active, playerId=" + playerId + ", amount=" + amount + ", transactionId=" + transactionId + ", expectedRevision=" + expectedRevision);
            var result = MutateAccountState("term-active", playerId, transactionId, expectedRevision, delegate(Account account) {
                var deposit = PrepareTermDeposit(account, productIndex, amount, absoluteDay);
                if (amount > account.ActiveBalance) throw new BankOperationException("active-insufficient");
                account.ActiveBalance -= amount;
                // Fractional active interest belongs to this account, even when its whole-gold balance becomes zero.
                account.TermDeposits.Add(deposit);
                return "term-created:" + deposit.Id;
            }, "active-insufficient");
            if (DiagnosticsEnabled)
                Trace("core-transaction: result, operation=term-active, playerId=" + playerId + ", amount=" + amount + ", transactionId=" + transactionId + ", status=" + result.Status + ", message=" + result.Message + ", revision=" + result.NewRevision);
            return result;
        }

        private TermDeposit PrepareTermDeposit(Account account, int productIndex, long amount, int absoluteDay)
        {
            ValidateAmount(amount);
            if (IsCollectionPhase(account.Loan)) throw new BankOperationException("collection-new-term-blocked");
            var product = GetTermProduct(productIndex);
            if (account.TermDeposits.Count >= BankLimits.MaximumTermDepositsPerAccount) throw new BankOperationException("term-limit-reached");
            if (TotalTermBalance(account.TermDeposits) + amount > long.MaxValue) throw new OverflowException("aggregate term balance");
            return new TermDeposit
            {
                Id = "TD-" + Guid.NewGuid().ToString("N"), Principal = amount, Balance = amount, TermDays = product.Days,
                LockedTermRatePercent = product.DailyRatePercent, LockedActivityRatePercent = config.ActivityDailyRatePercent,
                OpenDay = absoluteDay, MaturityDay = checked(absoluteDay + product.Days), DaysSettled = 0
            };
        }

        public TransactionResult WithdrawTermEarly(long playerId, IWalletAdapter wallet, string depositId, int absoluteDay, string transactionId, long expectedRevision)
        {
            return Mutate("term-early", expectedRevision, transactionId, playerId, wallet, delegate(Account account) {
                var deposit = account.TermDeposits.FirstOrDefault(x => x.Id == depositId);
                if (deposit == null) throw new BankOperationException("term-not-found");
                var completedDays = Math.Max(0, Math.Min(deposit.DaysSettled, absoluteDay - deposit.OpenDay));
                var returnAmount = BankMath.Compound(deposit.Principal, deposit.LockedActivityRatePercent, completedDays);
                account.TermDeposits.Remove(deposit);
                if (IsCollectionPhase(account.Loan))
                {
                    var payment = Math.Min(returnAmount, account.Loan.TotalOwed);
                    ApplyLoanPayment(account.Loan, payment, absoluteDay);
                    account.ActiveBalance = checked(account.ActiveBalance + returnAmount - payment);
                    return "term-closed-early-to-overdue-loan";
                }
                account.ActiveBalance = checked(account.ActiveBalance + returnAmount);
                return "term-closed-early";
            });
        }

        public TransactionResult RequestLoan(long playerId, IWalletAdapter wallet, int productIndex, long amount, int absoluteDay, string transactionId, long expectedRevision)
        {
            return Mutate("loan", expectedRevision, transactionId, playerId, wallet, delegate(Account account) {
                ValidateAmount(amount);
                if (IsOpenLoan(account.Loan)) throw new BankOperationException("loan-already-open");
                if (account.CreditRestriction != null && account.CreditRestriction.IsRestricted(absoluteDay)) throw new BankOperationException("loan-credit-restricted");
                if (amount % 1000 != 0) throw new BankOperationException("loan-must-be-1000-multiple");
                var product = GetLoanProduct(productIndex);
                var limit = BankMath.CreditLimit(config, wallet.HistoricalTotalEarnings);
                if (amount > limit) throw new BankOperationException("loan-over-limit");
                if (!wallet.TryAdd(amount)) throw new BankOperationException("wallet-overflow");
                account.Loan = new LoanContract
                {
                    Id = "LN-" + Guid.NewGuid().ToString("N"), OriginalPrincipal = amount, Principal = amount, TermDays = product.Days,
                    LockedRatePercent = product.DailyRatePercent, OverdueMultiplier = config.OverdueRateMultiplier,
                    LockedCollectionEnabled = config.EnableOverdueCollection,
                    LockedGraceDays = config.OverdueGraceDays,
                    LockedActiveBalanceSetoff = config.EnableActiveBalanceSetoff,
                    LockedMinimumActiveBalanceReserve = config.MinimumActiveBalanceReserve,
                    LockedMinimumWalletReserve = config.MinimumWalletReserve,
                    LockedIncomeGarnishmentPercent = config.IncomeGarnishmentPercent,
                    LockedMaximumOverdueInterestPercent = config.MaximumOverdueInterestPercent,
                    LockedDefaultAfterDays = config.DefaultAfterDays,
                    OpenDay = absoluteDay,
                    MaturityDay = checked(absoluteDay + product.Days),
                    CollectionStartDay = checked(absoluteDay + product.Days + config.OverdueGraceDays + 1),
                    DefaultDay = checked(absoluteDay + product.Days + config.OverdueGraceDays + 1 + config.DefaultAfterDays),
                    LastSettledDay = absoluteDay,
                    Status = LoanStatus.Current
                };
                return "loan-created";
            });
        }

        public TransactionResult RepayLoan(long playerId, IWalletAdapter wallet, long amount, string transactionId, long expectedRevision)
        {
            return RepayLoan(playerId, wallet, amount, -1, transactionId, expectedRevision);
        }

        public TransactionResult RepayLoan(long playerId, IWalletAdapter wallet, long amount, int absoluteDay, string transactionId, long expectedRevision)
        {
            return Mutate("repay", expectedRevision, transactionId, playerId, wallet, delegate(Account account) {
                ValidateAmount(amount);
                if (!IsOpenLoan(account.Loan)) throw new BankOperationException("loan-not-found");
                var payment = Math.Min(amount, account.Loan.TotalOwed);
                if (!wallet.TryRemove(payment)) throw new BankOperationException("wallet-insufficient");
                ApplyLoanPayment(account.Loan, payment, absoluteDay < 0 ? account.Loan.LastSettledDay : absoluteDay);
                return "loan-repayment-applied";
            });
        }

        public bool HasCollectionLoan(long playerId)
        {
            lock (sync)
            {
                Account account;
                return TryGetExistingAccount(playerId, out account) && IsCollectionPhase(account.Loan);
            }
        }

        public TransactionResult CollectScheduledPayment(long playerId, IWalletAdapter wallet, int absoluteDay, bool checkActiveBalance, bool checkWallet, string transactionId, long expectedRevision)
        {
            return MutateCollection("scheduled-collection", playerId, wallet, transactionId, expectedRevision, delegate(Account account) {
                var loan = account.Loan;
                if (!IsCollectionEligible(loan, absoluteDay)) throw new BankOperationException("loan-not-in-collection");
                var result = new CollectionMutation { Message = "scheduled-collection-applied" };

                if (checkActiveBalance && (!loan.LastActiveSetoffDay.HasValue || loan.LastActiveSetoffDay.Value < absoluteDay))
                {
                    result.Changed = true;
                    var available = Math.Max(0L, account.ActiveBalance - loan.LockedMinimumActiveBalanceReserve);
                    var payment = Math.Min(available, loan.TotalOwed);
                    if (loan.LockedActiveBalanceSetoff && payment > 0)
                    {
                        account.ActiveBalance -= payment;
                        var paid = ApplyLoanPayment(loan, payment, absoluteDay);
                        result.ActiveSetoffAmount = payment;
                        result.InterestPaid += paid.InterestPaid;
                        result.PrincipalPaid += paid.PrincipalPaid;
                    }
                    loan.LastActiveSetoffDay = absoluteDay;
                }

                if (checkWallet && IsOpenLoan(loan) && (!loan.LastWalletSweepDay.HasValue || loan.LastWalletSweepDay.Value < absoluteDay))
                {
                    if (wallet == null) throw new BankOperationException("wallet-unavailable");
                    var walletBalance = wallet.Balance;
                    result.Changed = true;
                    var available = Math.Max(0L, walletBalance - loan.LockedMinimumWalletReserve);
                    var payment = Math.Min(available, loan.TotalOwed);
                    if (payment > 0)
                    {
                        if (!wallet.TryRemove(payment)) throw new BankOperationException("wallet-insufficient");
                        var paid = ApplyLoanPayment(loan, payment, absoluteDay);
                        result.WalletCollectionAmount = payment;
                        result.InterestPaid += paid.InterestPaid;
                        result.PrincipalPaid += paid.PrincipalPaid;
                    }
                    loan.LastWalletSweepDay = absoluteDay;
                }

                result.RemainingDebt = IsOpenLoan(loan) ? loan.TotalOwed : 0;
                return result;
            });
        }

        public TransactionResult CollectIncomeGarnishment(long playerId, IWalletAdapter wallet, long positiveIncomeDelta, int absoluteDay, string transactionId, long expectedRevision)
        {
            return MutateCollection("income-garnishment", playerId, wallet, transactionId, expectedRevision, delegate(Account account) {
                if (wallet == null) throw new BankOperationException("wallet-unavailable");
                if (positiveIncomeDelta <= 0) throw new BankOperationException("income-delta-not-positive");
                var loan = account.Loan;
                if (!IsCollectionEligible(loan, absoluteDay)) throw new BankOperationException("loan-not-in-collection");
                var exact = checked(positiveIncomeDelta * loan.LockedIncomeGarnishmentPercent / 100m + loan.GarnishmentRemainder);
                decimal nextRemainder;
                var quota = BankMath.ExtractWholeCoins(exact, out nextRemainder);
                var available = Math.Max(0L, wallet.Balance - loan.LockedMinimumWalletReserve);
                var payment = Math.Min(Math.Min(quota, available), loan.TotalOwed);
                var result = new CollectionMutation { Changed = nextRemainder != loan.GarnishmentRemainder, Message = "income-garnishment-applied" };
                loan.GarnishmentRemainder = nextRemainder;
                if (payment > 0)
                {
                    if (!wallet.TryRemove(payment)) throw new BankOperationException("wallet-insufficient");
                    var paid = ApplyLoanPayment(loan, payment, absoluteDay);
                    result.Changed = true;
                    result.WalletCollectionAmount = payment;
                    result.InterestPaid = paid.InterestPaid;
                    result.PrincipalPaid = paid.PrincipalPaid;
                }
                result.RemainingDebt = IsOpenLoan(loan) ? loan.TotalOwed : 0;
                return result;
            });
        }

        public TransactionResult CancelDebt(long playerId, int absoluteDay, string transactionId, long expectedRevision)
        {
            return MutateAccountState("cancel-debt", playerId, transactionId, expectedRevision, delegate(Account account) {
                if (!IsOpenLoan(account.Loan)) throw new BankOperationException("loan-not-found");
                account.Loan.Principal = 0;
                account.Loan.AccruedInterest = 0;
                account.Loan.InterestRemainder = 0m;
                account.Loan.GarnishmentRemainder = 0m;
                account.Loan.Status = LoanStatus.Cancelled;
                account.Loan.ResolvedDay = absoluteDay;
                account.Loan.Resolution = LoanResolutionKind.AdministrativeCancellation;
                return "loan-cancelled";
            });
        }

        public TransactionResult SetCreditRestriction(long playerId, CreditRestrictionMode mode, int? untilDay, string transactionId, long expectedRevision)
        {
            return MutateAccountState("set-credit-restriction", playerId, transactionId, expectedRevision, delegate(Account account) {
                if (mode == CreditRestrictionMode.None) throw new BankOperationException("credit-restriction-invalid");
                if (mode == CreditRestrictionMode.UntilDay && (!untilDay.HasValue || untilDay.Value < 0)) throw new BankOperationException("credit-restriction-date-invalid");
                if (mode == CreditRestrictionMode.Permanent && untilDay.HasValue) throw new BankOperationException("credit-restriction-date-invalid");
                account.CreditRestriction = new CreditRestriction { Mode = mode, UntilDay = mode == CreditRestrictionMode.UntilDay ? untilDay : null };
                return "credit-restriction-set";
            });
        }

        public TransactionResult ClearCreditRestriction(long playerId, string transactionId, long expectedRevision)
        {
            return MutateAccountState("clear-credit-restriction", playerId, transactionId, expectedRevision, delegate(Account account) {
                account.CreditRestriction = new CreditRestriction();
                return "credit-restriction-cleared";
            });
        }

        public TransactionResult SettleToDay(int absoluteDay, string transactionId, long expectedRevision)
        {
            lock (sync)
            {
                if (DiagnosticsEnabled)
                    Trace("core-settlement: begin, targetDay=" + absoluteDay + ", expectedRevision=" + expectedRevision + ", revision=" + State.Revision + ", lastDay=" + (State.LastSettlementDay.HasValue ? State.LastSettlementDay.Value.ToString() : "none"));
                if (State.DataInvalid) return TraceSettlementResult(TransactionResult.Rejected("save-data-invalid", State.Revision), absoluteDay);
                if (State.ModeMismatch) return TraceSettlementResult(new TransactionResult { Status = TransactionStatus.Rejected, Message = "wallet-mode-mismatch", NewRevision = State.Revision }, absoluteDay);
                if (expectedRevision != State.Revision) return TraceSettlementResult(new TransactionResult { Status = TransactionStatus.StaleRevision, Message = "stale-revision", NewRevision = State.Revision }, absoluteDay);
                if (string.IsNullOrWhiteSpace(transactionId) || transactionId.Length > 128) return TraceSettlementResult(TransactionResult.Rejected("transaction-id-required", State.Revision), absoluteDay);
                if (absoluteDay < 0) return TraceSettlementResult(TransactionResult.Rejected("date-before-last-settlement", State.Revision), absoluteDay);
                if (State.ProcessedTransactionIds.Contains(transactionId)) return TraceSettlementResult(new TransactionResult { Status = TransactionStatus.Duplicate, Message = "duplicate-transaction", NewRevision = State.Revision }, absoluteDay);
                var before = State.Clone();
                try
                {
                    var from = State.LastSettlementDay.HasValue ? State.LastSettlementDay.Value + 1 : absoluteDay;
                    if (State.LastSettlementDay.HasValue && absoluteDay < State.LastSettlementDay.Value) throw new BankOperationException("date-before-last-settlement");
                    for (var day = from; day <= absoluteDay; day++) SettleOneDay(day);
                    State.LastSettlementDay = absoluteDay;
                    MarkProcessed(transactionId);
                    State.Revision = checked(State.Revision + 1);
                    var applied = TransactionResult.Applied("settlement-applied", State.Revision);
                    if (DiagnosticsEnabled)
                        Trace("core-settlement: applied, targetDay=" + absoluteDay + ", daysProcessed=" + (absoluteDay - from + 1) + ", revision=" + State.Revision + ", before=" + DescribeState(before) + ", after=" + DescribeState(State));
                    return applied;
                }
                catch (Exception ex)
                {
                    State = before;
                    var message = ex is BankOperationException ? ex.Message : ex is OverflowException ? "settlement-overflow" : "settlement-failed";
                    if (DiagnosticsEnabled)
                        Trace("core-settlement: rolled-back, targetDay=" + absoluteDay + ", message=" + message + ", revision=" + State.Revision);
                    return TransactionResult.Rejected(message, State.Revision);
                }
            }
        }

        public BankSnapshot Snapshot(long playerId, IWalletAdapter wallet)
        {
            if (wallet == null) throw new ArgumentNullException("wallet");
            lock (sync)
            {
                var account = State.WalletMode == WalletMode.Shared
                    ? State.SharedAccount
                    : State.PersonalAccounts.ContainsKey(playerId) ? State.PersonalAccounts[playerId] : new Account { Id = "player-" + playerId };
                var snapshot = new BankSnapshot
                {
                    WalletBalance = wallet.Balance, ActiveBalance = account.ActiveBalance,
                    TermBalance = SaturatingTermBalance(account.TermDeposits),
                    LoanPrincipal = account.Loan == null ? 0 : account.Loan.Principal,
                    LoanInterest = account.Loan == null ? 0 : account.Loan.PayableInterest,
                    CreditLimit = BankMath.CreditLimit(config, wallet.HistoricalTotalEarnings), Revision = State.Revision
                };
                if (DiagnosticsEnabled)
                    Trace("core-snapshot: playerId=" + playerId + ", revision=" + snapshot.Revision + ", wallet=" + snapshot.WalletBalance + ", active=" + snapshot.ActiveBalance + ", term=" + snapshot.TermBalance + ", principal=" + snapshot.LoanPrincipal + ", interest=" + snapshot.LoanInterest + ", credit=" + snapshot.CreditLimit);
                return snapshot;
            }
        }

        public void ReplaceState(BankState state)
        {
            if (state == null) throw new ArgumentNullException("state");
            var errors = BankStateValidator.Validate(state);
            if (errors.Count > 0) throw new ArgumentException("Invalid bank state: " + string.Join("; ", errors));
            lock (sync)
            {
                State = state.Clone();
                NormalizeState(State);
                if (DiagnosticsEnabled)
                    Trace("core-state: replaced, mode=" + State.WalletMode + ", revision=" + State.Revision + ", personalAccounts=" + State.PersonalAccounts.Count);
            }
        }

        /// <summary>
        /// Preflight a wallet-mode switch without changing any bank state.
        /// A switch is only allowed when no account currently holds an
        /// unsettled term deposit or loan; only active balances are migrated,
        /// so an open contract would otherwise be dropped silently.
        /// </summary>
        public TransactionResult CheckWalletModeMigration(WalletMode targetMode)
        {
            lock (sync)
            {
                if (!Enum.IsDefined(typeof(WalletMode), targetMode))
                    return MigrationRejected("wallet-mode-invalid", "The requested wallet mode is not supported.", targetMode);
                if (State.DataInvalid)
                    return MigrationRejected("save-data-invalid", "The save is already marked invalid; no bank state was changed.", targetMode);

                var currentErrors = BankStateValidator.Validate(State);
                if (currentErrors.Count > 0)
                    return MigrationRejected("wallet-mode-migration-invalid-state", "The current bank state failed validation; no assets or debts were changed.", targetMode);

                if (HasUnsettledProducts(State))
                    return MigrationRejected("wallet-mode-migration-open-products", "The bank account still has an open term deposit or unsettled loan, so the economy-mode switch cannot be completed.", targetMode);

                if (State.WalletMode == WalletMode.Shared && targetMode == WalletMode.Personal)
                {
                    try
                    {
                        EnsureNoInactivePersonalAssets(State);
                    }
                    catch (BankOperationException ex)
                    {
                        return MigrationRejected(ex.Message, ex.Detail ?? "The wallet-mode change was rejected before bank migration.", targetMode);
                    }
                }
                else if (State.WalletMode == WalletMode.Personal && targetMode == WalletMode.Shared)
                {
                    try
                    {
                        if (HasBankAssets(State.SharedAccount))
                            throw new BankOperationException("wallet-mode-migration-conflict", "The shared account contains inactive bank assets; no personal account can be merged into it safely.");
                    }
                    catch (BankOperationException ex)
                    {
                        return MigrationRejected(ex.Message, ex.Detail ?? "The wallet-mode change was rejected before bank migration.", targetMode);
                    }
                }

                return TransactionResult.Applied("wallet-mode-migration-allowed", State.Revision);
            }
        }

        /// <summary>
        /// Clear the transient mismatch marker after the game-level wallet
        /// request has been rolled back to the bank's current topology. This
        /// does not move assets or advance the revision.
        /// </summary>
        public void ClearModeMismatch()
        {
            lock (sync)
            {
                State.ModeMismatch = false;
            }
        }

        /// <summary>
        /// Migrate the bank state after the host changes the game's wallet mode.
        /// The request is revision-checked, and the owner ID is the host's stable
        /// multiplayer ID used as the personal-account destination.
        /// </summary>
        public TransactionResult MigrateWalletMode(WalletMode targetMode, long ownerPlayerId, long expectedRevision)
        {
            lock (sync)
            {
                return MigrateWalletModeLocked(targetMode, ownerPlayerId, expectedRevision);
            }
        }

        private TransactionResult MigrateWalletModeLocked(WalletMode targetMode, long ownerPlayerId, long expectedRevision)
        {
            if (!Enum.IsDefined(typeof(WalletMode), targetMode)) return MigrationRejected("wallet-mode-invalid", "The requested wallet mode is not supported.", targetMode);
            if (State.DataInvalid) return MigrationRejected("save-data-invalid", "The save is already marked invalid; no bank state was changed.", targetMode);
            if (expectedRevision != State.Revision)
                return MigrationResult(TransactionStatus.StaleRevision, "stale-revision", "The migration request used an old bank-state revision.", State.WalletMode, targetMode, State.Revision, false, 0, 0, 0, 0);

            var currentErrors = BankStateValidator.Validate(State);
            if (currentErrors.Count > 0) return MigrationRejected("wallet-mode-migration-invalid-state", "The current bank state failed validation; no assets or debts were changed.", targetMode);

            if (targetMode == State.WalletMode)
            {
                if (targetMode == WalletMode.Shared)
                {
                    try
                    {
                        EnsureNoInactivePersonalAssets(State);
                    }
                    catch (BankOperationException ex)
                    {
                        return MigrationRejected(ex.Message, ex.Detail ?? "The shared wallet topology contains stranded personal assets.", targetMode);
                    }
                }

                // The bank is already using the requested topology.  The
                // transient ModeMismatch marker can be stale after a rejected
                // vanilla request was rolled back; clearing it must not move
                // assets or advance the revision, otherwise ordinary host
                // transactions would shift revisions for no business reason.
                if (State.ModeMismatch)
                    State.ModeMismatch = false;
                return MigrationResult(TransactionStatus.Applied, "wallet-mode-already-set", "The bank is already using the requested wallet mode.", State.WalletMode, targetMode, State.Revision, false, 0, 0, 0, 0);
            }

            try
            {
                // Re-check at the moment the vanilla topology actually changes.
                // The earlier request may have been approved, but the player
                // could have opened a new term deposit or loan before sleeping.
                if (HasUnsettledProducts(State))
                    throw new BankOperationException("wallet-mode-migration-open-products", "The bank account still has an open term deposit or unsettled loan, so the economy-mode switch cannot be completed.");

                var migrated = State.Clone();
                var sourceMode = State.WalletMode;
                var summary = new ModeMigrationSummary();
                if (State.WalletMode == WalletMode.Shared && targetMode == WalletMode.Personal)
                    summary = MoveSharedActiveBalanceToPersonal(migrated, ownerPlayerId);
                else if (State.WalletMode == WalletMode.Personal && targetMode == WalletMode.Shared)
                    summary = MergePersonalAccountsToShared(migrated);
                else
                    throw new BankOperationException("wallet-mode-invalid");

                migrated.WalletMode = targetMode;
                migrated.ModeMismatch = false;
                migrated.Revision = checked(State.Revision + 1);
                var migratedErrors = BankStateValidator.Validate(migrated);
                if (migratedErrors.Count > 0) throw new BankOperationException("wallet-mode-migration-invalid-result");

                State = migrated;
                var diagnostic = targetMode == WalletMode.Shared
                    ? "Active balances were merged exactly once; term deposits and loans were not transferred, and the personal source containers were cleared."
                    : "Active savings were moved to the host personal account; term deposits and loans were not transferred.";
                return MigrationResult(TransactionStatus.Applied, "wallet-mode-migrated", diagnostic, sourceMode, targetMode, State.Revision, true, summary.ActiveBalanceMoved, summary.SourceAccountCount, summary.TermDepositsMoved, summary.LoanContractsMoved);
            }
            catch (BankOperationException ex)
            {
                return MigrationRejected(ex.Message, ex.Detail ?? "The migration was rejected atomically; the original bank state was preserved.", targetMode);
            }
            catch (OverflowException)
            {
                return MigrationRejected("wallet-mode-migration-overflow", "A checked balance or revision calculation overflowed; the original bank state was preserved.", targetMode);
            }
        }

        private static ModeMigrationSummary MoveSharedActiveBalanceToPersonal(BankState state, long ownerPlayerId)
        {
            var shared = state.SharedAccount;
            if (shared == null) throw new BankOperationException("wallet-mode-migration-invalid-state", "SharedAccount is missing.");
            if (state.PersonalAccounts == null) throw new BankOperationException("wallet-mode-migration-invalid-state", "PersonalAccounts is missing.");

            EnsureNoInactivePersonalAssets(state);

            Account destination;
            if (state.PersonalAccounts.TryGetValue(ownerPlayerId, out destination))
            {
                if (destination == null)
                    throw new BankOperationException("wallet-mode-migration-invalid-state", "The requested host destination account is missing.");
            }
            else
            {
                if (state.PersonalAccounts.Count >= BankLimits.MaximumPersonalAccounts)
                    throw new BankOperationException("account-limit-reached", "The personal-account capacity is full, so no unique host destination can be created.");
                destination = new Account { Id = "player-" + ownerPlayerId };
                state.PersonalAccounts[ownerPlayerId] = destination;
            }

            var moved = shared.ActiveBalance;
            destination.Id = "player-" + ownerPlayerId;
            MergeActiveSavings(destination, shared.ActiveBalance, shared.ActiveInterestRemainder);
            destination.CreditRestriction = MergeCreditRestrictions(destination.CreditRestriction, shared.CreditRestriction);
            state.SharedAccount = new Account { Id = "shared" };
            return new ModeMigrationSummary
            {
                ActiveBalanceMoved = moved,
                SourceAccountCount = 1,
                TermDepositsMoved = 0,
                LoanContractsMoved = 0
            };
        }

        private static void EnsureNoInactivePersonalAssets(BankState state)
        {
            if (state == null || state.PersonalAccounts == null)
                throw new BankOperationException("wallet-mode-migration-invalid-state", "PersonalAccounts is missing.");

            foreach (var pair in state.PersonalAccounts.OrderBy(x => x.Key))
            {
                if (pair.Value == null)
                    throw new BankOperationException("wallet-mode-migration-invalid-state", "A personal account is missing.");
                if (HasBankAssets(pair.Value))
                    throw new BankOperationException("wallet-mode-migration-conflict", "PersonalAccounts already contains bank assets; moving the shared account would make ownership ambiguous.");
            }
        }

        private static ModeMigrationSummary MergePersonalAccountsToShared(BankState state)
        {
            if (HasBankAssets(state.SharedAccount))
                throw new BankOperationException("wallet-mode-migration-conflict", "SharedAccount contains inactive bank assets; merging PersonalAccounts would make ownership ambiguous.");

            if (HasUnsettledProducts(state))
                throw new BankOperationException("wallet-mode-migration-open-products", "The bank account still has an open term deposit or unsettled loan, so the economy-mode switch cannot be completed.");

            var merged = new Account
            {
                Id = "shared",
                CreditRestriction = state.SharedAccount.CreditRestriction == null
                    ? new CreditRestriction()
                    : state.SharedAccount.CreditRestriction.Clone()
            };
            var sourceAccountCount = state.PersonalAccounts.Count;

            foreach (var pair in state.PersonalAccounts.OrderBy(x => x.Key))
            {
                var account = pair.Value;
                if (account == null) throw new BankOperationException("wallet-mode-migration-invalid-state", "A personal account is missing.");
                MergeActiveSavings(merged, account.ActiveBalance, account.ActiveInterestRemainder);
                merged.CreditRestriction = MergeCreditRestrictions(merged.CreditRestriction, account.CreditRestriction);
            }

            state.SharedAccount = merged;
            state.PersonalAccounts.Clear();
            return new ModeMigrationSummary
            {
                ActiveBalanceMoved = merged.ActiveBalance,
                SourceAccountCount = sourceAccountCount,
                TermDepositsMoved = 0,
                LoanContractsMoved = 0
            };
        }

        private static TransactionResult MigrationResult(TransactionStatus status, string message, string diagnostic, WalletMode sourceMode, WalletMode targetMode, long revision, bool changed, long activeBalanceMoved, int sourceAccountCount, int termDepositsMoved, int loanContractsMoved)
        {
            return new TransactionResult
            {
                Status = status,
                Message = message,
                Diagnostic = diagnostic,
                SourceMode = sourceMode,
                TargetMode = targetMode,
                NewRevision = revision,
                StateChanged = changed,
                ActiveBalanceMoved = activeBalanceMoved,
                SourceAccountCount = sourceAccountCount,
                TermDepositsMoved = termDepositsMoved,
                LoanContractsMoved = loanContractsMoved
            };
        }

        private TransactionResult MigrationRejected(string message, string diagnostic, WalletMode targetMode)
        {
            return MigrationResult(TransactionStatus.Rejected, message, diagnostic, State.WalletMode, targetMode, State.Revision, false, 0, 0, 0, 0);
        }

        private sealed class ModeMigrationSummary
        {
            public long ActiveBalanceMoved;
            public int SourceAccountCount;
            public int TermDepositsMoved;
            public int LoanContractsMoved;
        }

        private static bool HasBankAssets(Account account)
        {
            return account != null && (account.ActiveBalance != 0 || account.ActiveInterestRemainder != 0m
                || (account.TermDeposits != null && account.TermDeposits.Count > 0) || IsOpenLoan(account.Loan));
        }

        private static void MergeActiveSavings(Account destination, long balance, decimal interestRemainder)
        {
            decimal mergedRemainder;
            var carriedCoins = BankMath.ExtractWholeCoins(checked(destination.ActiveInterestRemainder + interestRemainder), out mergedRemainder);
            destination.ActiveBalance = checked(destination.ActiveBalance + balance);
            destination.ActiveBalance = checked(destination.ActiveBalance + carriedCoins);
            destination.ActiveInterestRemainder = mergedRemainder;
        }

        private static bool HasUnsettledProducts(BankState state)
        {
            if (state == null) return false;
            if (state.WalletMode == WalletMode.Shared)
                return HasUnsettledProducts(state.SharedAccount);
            if (state.PersonalAccounts == null) return false;
            foreach (var pair in state.PersonalAccounts)
                if (HasUnsettledProducts(pair.Value))
                    return true;
            return false;
        }

        private static bool HasUnsettledProducts(Account account)
        {
            if (account == null) return false;
            if (account.TermDeposits != null && account.TermDeposits.Count > 0) return true;
            return IsOpenLoan(account.Loan);
        }

        private static void NormalizeState(BankState state)
        {
            if (state.SharedAccount == null) state.SharedAccount = new Account { Id = "shared" };
            NormalizeAccount(state.SharedAccount, "shared");
            if (state.PersonalAccounts == null) state.PersonalAccounts = new Dictionary<long, Account>();
            foreach (var playerId in state.PersonalAccounts.Keys.ToList())
            {
                if (state.PersonalAccounts[playerId] == null) state.PersonalAccounts[playerId] = new Account { Id = "player-" + playerId };
                NormalizeAccount(state.PersonalAccounts[playerId], "player-" + playerId);
            }
            if (state.ProcessedTransactionIds == null) state.ProcessedTransactionIds = new List<string>();
            state.ProcessedTransactionIds.RemoveAll(string.IsNullOrWhiteSpace);
            if (state.ProcessedTransactionIds.Count > 256)
                state.ProcessedTransactionIds.RemoveRange(0, state.ProcessedTransactionIds.Count - 256);
        }

        private static void NormalizeAccount(Account account, string fallbackId)
        {
            if (string.IsNullOrWhiteSpace(account.Id)) account.Id = fallbackId;
            if (account.TermDeposits == null) account.TermDeposits = new List<TermDeposit>();
            account.TermDeposits.RemoveAll(x => x == null || string.IsNullOrWhiteSpace(x.Id));
            if (account.CreditRestriction == null) account.CreditRestriction = new CreditRestriction();
        }

        private TransactionResult Mutate(string operation, long expectedRevision, string transactionId, long playerId, IWalletAdapter wallet, Func<Account, string> action)
        {
            lock (sync)
            {
                if (wallet == null) throw new ArgumentNullException("wallet");
                if (DiagnosticsEnabled)
                    Trace("core-transaction: begin, operation=" + operation + ", playerId=" + playerId + ", expectedRevision=" + expectedRevision + ", revision=" + State.Revision + ", wallet=" + wallet.Balance);
                if (State.DataInvalid) return TraceTransactionResult(operation, playerId, TransactionResult.Rejected("save-data-invalid", State.Revision));
                if (State.ModeMismatch) return TraceTransactionResult(operation, playerId, new TransactionResult { Status = TransactionStatus.Rejected, Message = "wallet-mode-mismatch", NewRevision = State.Revision });
                if (expectedRevision != State.Revision) return TraceTransactionResult(operation, playerId, new TransactionResult { Status = TransactionStatus.StaleRevision, Message = "stale-revision", NewRevision = State.Revision });
                if (string.IsNullOrWhiteSpace(transactionId) || transactionId.Length > 128) return TraceTransactionResult(operation, playerId, TransactionResult.Rejected("transaction-id-required", State.Revision));
                if (State.ProcessedTransactionIds.Contains(transactionId)) return TraceTransactionResult(operation, playerId, new TransactionResult { Status = TransactionStatus.Duplicate, Message = "duplicate-transaction", NewRevision = State.Revision });
                var walletBalance = wallet.Balance;
                var revisionBefore = State.Revision;
                var processedBefore = new List<string>(State.ProcessedTransactionIds);
                Account account = null;
                Account accountBefore = null;
                var createdPersonalAccount = false;
                try
                {
                    if (State.WalletMode == WalletMode.Shared) account = State.SharedAccount;
                    else if (!State.PersonalAccounts.TryGetValue(playerId, out account))
                    {
                        if (State.PersonalAccounts.Count >= BankLimits.MaximumPersonalAccounts) throw new BankOperationException("account-limit-reached");
                        account = new Account { Id = "player-" + playerId };
                        State.PersonalAccounts[playerId] = account;
                        createdPersonalAccount = true;
                    }
                    accountBefore = account.Clone();
                    var message = action(account);
                    MarkProcessed(transactionId);
                    State.Revision = checked(State.Revision + 1);
                    if (DiagnosticsEnabled)
                        Trace("core-transaction: applied, operation=" + operation + ", playerId=" + playerId + ", message=" + message + ", revision=" + State.Revision + ", wallet=" + wallet.Balance + ", active=" + account.ActiveBalance + ", termCount=" + account.TermDeposits.Count + ", termTotal=" + SaturatingTermBalance(account.TermDeposits) + ", loanPrincipal=" + (account.Loan == null ? 0 : account.Loan.Principal) + ", loanInterest=" + (account.Loan == null ? 0 : account.Loan.PayableInterest) + ", loanStatus=" + (account.Loan == null ? "none" : account.Loan.Status.ToString()));
                    return TransactionResult.Applied(message, State.Revision);
                }
                catch (Exception ex)
                {
                    if (State.WalletMode == WalletMode.Shared && accountBefore != null) State.SharedAccount = accountBefore;
                    else if (createdPersonalAccount) State.PersonalAccounts.Remove(playerId);
                    else if (accountBefore != null) State.PersonalAccounts[playerId] = accountBefore;
                    State.ProcessedTransactionIds = processedBefore;
                    State.Revision = revisionBefore;
                    try { wallet.RestoreBalance(walletBalance); }
                    catch
                    {
                        return TransactionResult.Rejected("wallet-rollback-failed", State.Revision);
                    }
                    if (wallet.Balance != walletBalance)
                        return TransactionResult.Rejected("wallet-rollback-failed", State.Revision);
                    var message = ex is BankOperationException ? ex.Message : ex is OverflowException ? "amount-overflow" : "transaction-failed";
                    if (DiagnosticsEnabled)
                        Trace("core-transaction: rolled-back, operation=" + operation + ", playerId=" + playerId + ", message=" + message + ", revision=" + State.Revision + ", wallet=" + wallet.Balance);
                    return TransactionResult.Rejected(message, State.Revision);
                }
            }
        }

        private TransactionResult MutateCollection(string operation, long playerId, IWalletAdapter wallet, string transactionId, long expectedRevision, Func<Account, CollectionMutation> action)
        {
            lock (sync)
            {
                if (State.DataInvalid) return TransactionResult.Rejected("save-data-invalid", State.Revision);
                if (State.ModeMismatch) return TransactionResult.Rejected("wallet-mode-mismatch", State.Revision);
                if (expectedRevision != State.Revision) return new TransactionResult { Status = TransactionStatus.StaleRevision, Message = "stale-revision", NewRevision = State.Revision };
                if (string.IsNullOrWhiteSpace(transactionId) || transactionId.Length > 128) return TransactionResult.Rejected("transaction-id-required", State.Revision);
                if (State.ProcessedTransactionIds.Contains(transactionId)) return new TransactionResult { Status = TransactionStatus.Duplicate, Message = "duplicate-transaction", NewRevision = State.Revision };
                Account account;
                if (!TryGetExistingAccount(playerId, out account)) return TransactionResult.Rejected("account-not-found", State.Revision);
                var before = State.Clone();
                var walletBalance = wallet == null ? 0 : wallet.Balance;
                try
                {
                    var mutation = action(account);
                    if (mutation == null || !mutation.Changed)
                        return new TransactionResult { Status = TransactionStatus.Duplicate, Message = mutation == null ? "collection-no-change" : mutation.Message, NewRevision = State.Revision };
                    MarkProcessed(transactionId);
                    State.Revision = checked(State.Revision + 1);
                    return new TransactionResult
                    {
                        Status = TransactionStatus.Applied,
                        Message = mutation.Message,
                        NewRevision = State.Revision,
                        StateChanged = true,
                        ActiveSetoffAmount = mutation.ActiveSetoffAmount,
                        WalletCollectionAmount = mutation.WalletCollectionAmount,
                        InterestPaid = mutation.InterestPaid,
                        PrincipalPaid = mutation.PrincipalPaid,
                        RemainingDebt = mutation.RemainingDebt
                    };
                }
                catch (Exception ex)
                {
                    State = before;
                    if (wallet != null)
                    {
                        try { wallet.RestoreBalance(walletBalance); }
                        catch { return TransactionResult.Rejected("wallet-rollback-failed", State.Revision); }
                        if (wallet.Balance != walletBalance) return TransactionResult.Rejected("wallet-rollback-failed", State.Revision);
                    }
                    var message = ex is BankOperationException ? ex.Message : ex is OverflowException ? "amount-overflow" : "transaction-failed";
                    return TransactionResult.Rejected(message, State.Revision);
                }
            }
        }

        private TransactionResult MutateAccountState(string operation, long playerId, string transactionId, long expectedRevision, Func<Account, string> action, string missingAccountMessage = "account-not-found")
        {
            lock (sync)
            {
                if (State.DataInvalid) return TransactionResult.Rejected("save-data-invalid", State.Revision);
                if (State.ModeMismatch) return TransactionResult.Rejected("wallet-mode-mismatch", State.Revision);
                if (expectedRevision != State.Revision) return new TransactionResult { Status = TransactionStatus.StaleRevision, Message = "stale-revision", NewRevision = State.Revision };
                if (string.IsNullOrWhiteSpace(transactionId) || transactionId.Length > 128) return TransactionResult.Rejected("transaction-id-required", State.Revision);
                if (State.ProcessedTransactionIds.Contains(transactionId)) return new TransactionResult { Status = TransactionStatus.Duplicate, Message = "duplicate-transaction", NewRevision = State.Revision };
                Account account;
                if (!TryGetExistingAccount(playerId, out account)) return TransactionResult.Rejected(missingAccountMessage, State.Revision);
                var before = State.Clone();
                try
                {
                    var message = action(account);
                    MarkProcessed(transactionId);
                    State.Revision = checked(State.Revision + 1);
                    return TransactionResult.Applied(message, State.Revision);
                }
                catch (Exception ex)
                {
                    State = before;
                    var message = ex is BankOperationException ? ex.Message : ex is OverflowException ? "amount-overflow" : "transaction-failed";
                    return TransactionResult.Rejected(message, State.Revision);
                }
            }
        }

        private bool TryGetExistingAccount(long playerId, out Account account)
        {
            if (State.WalletMode == WalletMode.Shared)
            {
                account = State.SharedAccount;
                return account != null;
            }
            return State.PersonalAccounts.TryGetValue(playerId, out account) && account != null;
        }

        private static PaymentBreakdown ApplyLoanPayment(LoanContract loan, long requestedPayment, int absoluteDay)
        {
            if (!IsOpenLoan(loan) || requestedPayment < 0) throw new BankOperationException("loan-not-found");
            var totalBefore = loan.TotalOwed;
            var payment = Math.Min(requestedPayment, totalBefore);
            var interestPaid = Math.Min(payment, loan.PayableInterest);
            var accruedPaid = Math.Min(interestPaid, loan.AccruedInterest);
            loan.AccruedInterest -= accruedPaid;
            if (interestPaid > accruedPaid) loan.InterestRemainder = 0m;
            var principalPaid = payment - interestPaid;
            loan.Principal -= principalPaid;
            if (payment == totalBefore)
            {
                loan.Principal = 0;
                loan.AccruedInterest = 0;
                loan.InterestRemainder = 0m;
                loan.GarnishmentRemainder = 0m;
                loan.Status = LoanStatus.Settled;
                loan.ResolvedDay = Math.Max(loan.OpenDay, absoluteDay);
                loan.Resolution = LoanResolutionKind.Repaid;
            }
            return new PaymentBreakdown { InterestPaid = interestPaid, PrincipalPaid = principalPaid };
        }

        private static void AccrueCappedOverdueInterest(LoanContract loan)
        {
            var exact = checked(loan.Principal * BankMath.PercentToRate(loan.LockedRatePercent * loan.OverdueMultiplier));
            if (loan.LockedMaximumOverdueInterestPercent > 0)
            {
                var cap = checked(loan.OriginalPrincipal * BankMath.PercentToRate(loan.LockedMaximumOverdueInterestPercent));
                var remaining = Math.Max(0m, cap - loan.OverdueInterestCharged);
                exact = Math.Min(exact, remaining);
            }
            if (exact <= 0m) return;
            decimal nextRemainder;
            var whole = BankMath.ExtractWholeCoins(checked(loan.InterestRemainder + exact), out nextRemainder);
            loan.AccruedInterest = checked(loan.AccruedInterest + whole);
            loan.InterestRemainder = nextRemainder;
            loan.OverdueInterestCharged = checked(loan.OverdueInterestCharged + exact);
        }

        private static LoanStatus LoanStatusForDay(LoanContract loan, int day)
        {
            if (day <= loan.MaturityDay) return LoanStatus.Current;
            if (!loan.LockedCollectionEnabled || day < loan.CollectionStartDay) return LoanStatus.Overdue;
            return day >= loan.DefaultDay ? LoanStatus.Defaulted : LoanStatus.Collection;
        }

        private static bool IsOpenLoan(LoanContract loan)
        {
            return loan != null && loan.Status != LoanStatus.Settled && loan.Status != LoanStatus.Cancelled;
        }

        private static bool IsCollectionPhase(LoanContract loan)
        {
            return IsOpenLoan(loan) && (loan.Status == LoanStatus.Collection || loan.Status == LoanStatus.Defaulted);
        }

        private static bool IsCollectionEligible(LoanContract loan, int absoluteDay)
        {
            return IsCollectionPhase(loan) && loan.LockedCollectionEnabled && absoluteDay >= loan.CollectionStartDay;
        }

        private static CreditRestriction MergeCreditRestrictions(CreditRestriction left, CreditRestriction right)
        {
            left = left ?? new CreditRestriction();
            right = right ?? new CreditRestriction();
            if (left.Mode == CreditRestrictionMode.Permanent || right.Mode == CreditRestrictionMode.Permanent)
                return new CreditRestriction { Mode = CreditRestrictionMode.Permanent };
            if (left.Mode == CreditRestrictionMode.UntilDay || right.Mode == CreditRestrictionMode.UntilDay)
                return new CreditRestriction
                {
                    Mode = CreditRestrictionMode.UntilDay,
                    UntilDay = Math.Max(left.UntilDay ?? 0, right.UntilDay ?? 0)
                };
            return new CreditRestriction();
        }

        private sealed class CollectionMutation
        {
            public bool Changed;
            public string Message;
            public long ActiveSetoffAmount;
            public long WalletCollectionAmount;
            public long InterestPaid;
            public long PrincipalPaid;
            public long RemainingDebt;
        }

        private sealed class PaymentBreakdown
        {
            public long InterestPaid;
            public long PrincipalPaid;
        }

        private void SettleOneDay(int day)
        {
            foreach (var account in AllAccounts())
            {
                decimal activeRemainder;
                var activeInterest = BankMath.AccrueDailyInterest(account.ActiveBalance, config.ActivityDailyRatePercent, account.ActiveInterestRemainder, out activeRemainder);
                account.ActiveBalance = checked(account.ActiveBalance + activeInterest);
                account.ActiveInterestRemainder = activeRemainder;
                foreach (var deposit in account.TermDeposits.ToList())
                {
                    if (day > deposit.OpenDay)
                    {
                        decimal termRemainder;
                        var termInterest = BankMath.AccrueDailyInterest(deposit.Balance, deposit.LockedTermRatePercent, deposit.InterestRemainder, out termRemainder);
                        deposit.Balance = checked(deposit.Balance + termInterest);
                        deposit.InterestRemainder = termRemainder;
                        deposit.DaysSettled++;
                    }
                    if (day >= deposit.MaturityDay && deposit.DaysSettled >= deposit.TermDays)
                    {
                        deposit.Balance = deposit.PayableBalance;
                        deposit.InterestRemainder = 0m;
                        account.ActiveBalance = checked(account.ActiveBalance + deposit.Balance);
                        account.TermDeposits.Remove(deposit);
                    }
                }
                if (TotalTermBalance(account.TermDeposits) > long.MaxValue) throw new OverflowException("aggregate term balance");
                if (IsOpenLoan(account.Loan))
                {
                    if (day > account.Loan.OpenDay)
                    {
                        if (day > account.Loan.MaturityDay)
                            AccrueCappedOverdueInterest(account.Loan);
                        else
                        {
                            decimal loanRemainder;
                            var loanInterest = BankMath.AccrueDailyInterest(account.Loan.Principal, account.Loan.LockedRatePercent, account.Loan.InterestRemainder, out loanRemainder);
                            account.Loan.AccruedInterest = checked(account.Loan.AccruedInterest + loanInterest);
                            account.Loan.InterestRemainder = loanRemainder;
                        }
                        account.Loan.LastSettledDay = day;
                        account.Loan.Status = LoanStatusForDay(account.Loan, day);
                    }
                }
            }
        }

        private IEnumerable<Account> AllAccounts()
        {
            if (State.WalletMode == WalletMode.Shared)
            {
                yield return State.SharedAccount;
            }
            else
            {
                foreach (var account in State.PersonalAccounts.Values) yield return account;
            }
        }
        private TermProductConfig GetTermProduct(int index)
        {
            if (index < 0 || index >= 10 || index >= config.TermProducts.Count || !config.TermProducts[index].Enabled) throw new BankOperationException("term-product-invalid");
            return config.TermProducts[index];
        }
        private LoanProductConfig GetLoanProduct(int index)
        {
            if (index < 0 || index >= 10 || index >= config.LoanProducts.Count || !config.LoanProducts[index].Enabled) throw new BankOperationException("loan-product-invalid");
            return config.LoanProducts[index];
        }
        private static void ValidateAmount(long amount)
        {
            if (amount <= 0) throw new BankOperationException("amount-must-be-positive");
        }

        private static decimal TotalTermBalance(IEnumerable<TermDeposit> deposits)
        {
            decimal total = 0;
            foreach (var deposit in deposits) total += deposit.PayableBalance;
            return total;
        }

        private static long SaturatingTermBalance(IEnumerable<TermDeposit> deposits)
        {
            var total = TotalTermBalance(deposits);
            return total >= long.MaxValue ? long.MaxValue : (long)total;
        }
        private void MarkProcessed(string transactionId)
        {
            State.ProcessedTransactionIds.Add(transactionId);
            if (State.ProcessedTransactionIds.Count > 256) State.ProcessedTransactionIds.RemoveRange(0, State.ProcessedTransactionIds.Count - 256);
        }

        private bool DiagnosticsEnabled
        {
            get
            {
                try { return diagnosticsEnabled != null && diagnosticsEnabled(); }
                catch { return false; }
            }
        }

        private TransactionResult TraceTransactionResult(string operation, long playerId, TransactionResult result)
        {
            if (DiagnosticsEnabled)
                Trace("core-transaction: rejected-before-mutation, operation=" + operation + ", playerId=" + playerId + ", status=" + result.Status + ", message=" + result.Message + ", revision=" + result.NewRevision);
            return result;
        }

        private TransactionResult TraceSettlementResult(TransactionResult result, int targetDay)
        {
            if (DiagnosticsEnabled)
                Trace("core-settlement: rejected-before-mutation, targetDay=" + targetDay + ", status=" + result.Status + ", message=" + result.Message + ", revision=" + result.NewRevision);
            return result;
        }

        private static string DescribeState(BankState state)
        {
            decimal active = 0;
            decimal term = 0;
            var termCount = 0;
            decimal loanPrincipal = 0;
            decimal loanInterest = 0;
            var accounts = state.WalletMode == WalletMode.Shared
                ? new[] { state.SharedAccount }
                : state.PersonalAccounts.Values.ToArray();
            foreach (var account in accounts)
            {
                active += account.ActiveBalance;
                term += TotalTermBalance(account.TermDeposits);
                termCount += account.TermDeposits.Count;
                if (IsOpenLoan(account.Loan))
                {
                    loanPrincipal += account.Loan.Principal;
                    loanInterest += account.Loan.PayableInterest;
                }
            }
            return "accounts=" + accounts.Length + ",active=" + active + ",termCount=" + termCount + ",termTotal=" + term + ",loanPrincipal=" + loanPrincipal + ",loanInterest=" + loanInterest;
        }

        private static bool ConfigsEquivalent(BankConfig left, BankConfig right)
        {
            if (left == null || right == null
                || left.EnableAtm != right.EnableAtm
                || left.DebugLogging != right.DebugLogging
                || left.ActivityDailyRatePercent != right.ActivityDailyRatePercent
                || left.BaseCredit != right.BaseCredit
                || left.EarningsRatio != right.EarningsRatio
                || left.AbsoluteCreditCap != right.AbsoluteCreditCap
                || left.OverdueRateMultiplier != right.OverdueRateMultiplier
                || left.EnableOverdueCollection != right.EnableOverdueCollection
                || left.OverdueGraceDays != right.OverdueGraceDays
                || left.EnableActiveBalanceSetoff != right.EnableActiveBalanceSetoff
                || left.MinimumActiveBalanceReserve != right.MinimumActiveBalanceReserve
                || left.MinimumWalletReserve != right.MinimumWalletReserve
                || left.IncomeGarnishmentPercent != right.IncomeGarnishmentPercent
                || left.MaximumOverdueInterestPercent != right.MaximumOverdueInterestPercent
                || left.DefaultAfterDays != right.DefaultAfterDays
                || left.TermProducts.Count != right.TermProducts.Count
                || left.LoanProducts.Count != right.LoanProducts.Count)
                return false;

            for (var index = 0; index < left.TermProducts.Count; index++)
                if (!ProductsEquivalent(left.TermProducts[index], right.TermProducts[index]))
                    return false;
            for (var index = 0; index < left.LoanProducts.Count; index++)
                if (!ProductsEquivalent(left.LoanProducts[index], right.LoanProducts[index]))
                    return false;
            return true;
        }

        private static bool ProductsEquivalent(ProductConfig left, ProductConfig right)
        {
            return left != null && right != null
                && left.Enabled == right.Enabled
                && left.Days == right.Days
                && left.DailyRatePercent == right.DailyRatePercent;
        }

        private void Trace(string message)
        {
            if (DiagnosticsEnabled && diagnosticLog != null)
                diagnosticLog(message);
        }
    }

    public sealed class BankOperationException : Exception
    {
        public string Detail { get; private set; }
        public BankOperationException(string message) : this(message, null) { }
        public BankOperationException(string message, string detail) : base(message) { Detail = detail; }
    }
}
