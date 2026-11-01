using System;
using System.Collections.Generic;

namespace SimpleBank.Core
{
    public enum WalletMode { Shared, Personal }
    // Keep the schema-2 numeric values for Matured/Settled. Overdue is the
    // clearer schema-3 name for the former Matured state.
    public enum LoanStatus { Current = 0, Overdue = 1, Matured = Overdue, Settled = 2, Collection = 3, Defaulted = 4, Cancelled = 5 }
    public enum LoanResolutionKind { None, Repaid, AdministrativeCancellation }
    public enum CreditRestrictionMode { None, UntilDay, Permanent }
    public enum TransactionStatus { Applied, Duplicate, Rejected, StaleRevision }

    public sealed class BankState
    {
        public const int CurrentSchemaVersion = 3;
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public WalletMode WalletMode { get; set; } = WalletMode.Shared;
        public bool ModeMismatch { get; set; }
        public bool DataInvalid { get; set; }
        public Account SharedAccount { get; set; } = new Account { Id = "shared" };
        public Dictionary<long, Account> PersonalAccounts { get; set; } = new Dictionary<long, Account>();
        public int? LastSettlementDay { get; set; }
        public long Revision { get; set; }
        public List<string> ProcessedTransactionIds { get; set; } = new List<string>();

        public BankState Clone()
        {
            var shared = SharedAccount ?? new Account { Id = "shared" };
            var clone = new BankState
            {
                SchemaVersion = SchemaVersion, WalletMode = WalletMode, ModeMismatch = ModeMismatch, DataInvalid = DataInvalid, SharedAccount = shared.Clone(),
                LastSettlementDay = LastSettlementDay, Revision = Revision,
                ProcessedTransactionIds = ProcessedTransactionIds == null ? new List<string>() : new List<string>(ProcessedTransactionIds)
            };
            if (PersonalAccounts != null)
            {
                foreach (var pair in PersonalAccounts)
                    // Preserve a null account entry so validation can still
                    // reject the same malformed shape after a rollback or a
                    // diagnostic clone; never turn corrupted data into an
                    // apparently valid empty account here.
                    clone.PersonalAccounts[pair.Key] = pair.Value == null ? null : pair.Value.Clone();
            }
            return clone;
        }
    }

    public sealed class Account
    {
        public string Id { get; set; }
        public long ActiveBalance { get; set; }
        public decimal ActiveInterestRemainder { get; set; }
        public List<TermDeposit> TermDeposits { get; set; } = new List<TermDeposit>();
        public LoanContract Loan { get; set; }
        public CreditRestriction CreditRestriction { get; set; } = new CreditRestriction();
        public Account Clone()
        {
            var clone = new Account
            {
                Id = Id,
                ActiveBalance = ActiveBalance,
                ActiveInterestRemainder = ActiveInterestRemainder,
                Loan = Loan == null ? null : Loan.Clone(),
                CreditRestriction = CreditRestriction == null ? null : CreditRestriction.Clone(),
                TermDeposits = TermDeposits == null ? null : new List<TermDeposit>()
            };
            if (TermDeposits != null)
            {
                foreach (var deposit in TermDeposits)
                    // A null contract is invalid, but retaining the slot is
                    // important: cloning must not silently discard corrupted
                    // data before the validator reports it.
                    clone.TermDeposits.Add(deposit == null ? null : deposit.Clone());
            }
            return clone;
        }
    }

    public sealed class CreditRestriction
    {
        public CreditRestrictionMode Mode { get; set; }
        public int? UntilDay { get; set; }
        public CreditRestriction Clone() { return (CreditRestriction)MemberwiseClone(); }
        public bool IsRestricted(int absoluteDay)
        {
            return Mode == CreditRestrictionMode.Permanent
                || (Mode == CreditRestrictionMode.UntilDay && UntilDay.HasValue && absoluteDay <= UntilDay.Value);
        }
    }

    public sealed class TermDeposit
    {
        public string Id { get; set; }
        public long Principal { get; set; }
        public long Balance { get; set; }
        public decimal InterestRemainder { get; set; }
        public int TermDays { get; set; }
        public decimal LockedTermRatePercent { get; set; }
        public decimal LockedActivityRatePercent { get; set; }
        public int OpenDay { get; set; }
        public int MaturityDay { get; set; }
        public int DaysSettled { get; set; }
        public long PayableBalance { get { return checked(Balance + BankMath.RoundInterestRemainder(InterestRemainder)); } }
        public TermDeposit Clone()
        {
            return (TermDeposit)MemberwiseClone();
        }
    }

    public sealed class LoanContract
    {
        public string Id { get; set; }
        public long OriginalPrincipal { get; set; }
        public long Principal { get; set; }
        public long AccruedInterest { get; set; }
        public decimal InterestRemainder { get; set; }
        public decimal OverdueInterestCharged { get; set; }
        public int TermDays { get; set; }
        public decimal LockedRatePercent { get; set; }
        public decimal OverdueMultiplier { get; set; }
        public bool LockedCollectionEnabled { get; set; }
        public int LockedGraceDays { get; set; }
        public bool LockedActiveBalanceSetoff { get; set; }
        public long LockedMinimumActiveBalanceReserve { get; set; }
        public long LockedMinimumWalletReserve { get; set; }
        public decimal LockedIncomeGarnishmentPercent { get; set; }
        public decimal LockedMaximumOverdueInterestPercent { get; set; }
        public int LockedDefaultAfterDays { get; set; }
        public decimal GarnishmentRemainder { get; set; }
        public int OpenDay { get; set; }
        public int MaturityDay { get; set; }
        public int CollectionStartDay { get; set; }
        public int DefaultDay { get; set; }
        public int LastSettledDay { get; set; }
        public int? LastActiveSetoffDay { get; set; }
        public int? LastWalletSweepDay { get; set; }
        public LoanStatus Status { get; set; } = LoanStatus.Current;
        public int? ResolvedDay { get; set; }
        public LoanResolutionKind Resolution { get; set; }
        public long PayableInterest { get { return checked(AccruedInterest + BankMath.RoundInterestRemainder(InterestRemainder)); } }
        public long TotalOwed { get { return checked(Principal + PayableInterest); } }
        public LoanContract Clone() { return (LoanContract)MemberwiseClone(); }

        // Callers handle settled/cancelled loans before deriving an open loan's phase.
        internal LoanStatus GetOpenStatusForDay(int day)
        {
            if (day <= MaturityDay) return LoanStatus.Current;
            if (!LockedCollectionEnabled || day < CollectionStartDay) return LoanStatus.Overdue;
            return day >= DefaultDay ? LoanStatus.Defaulted : LoanStatus.Collection;
        }
    }

    // A successful settlement receipt, never part of persisted BankState.
    public sealed class TermMaturity
    {
        // Null identifies a shared-account deposit; otherwise this is the stable owner ID.
        public long? PlayerId { get; set; }
        public string DepositId { get; set; }
        public int Day { get; set; }
        public long Amount { get; set; }
    }

    public sealed class TransactionResult
    {
        public TransactionStatus Status { get; set; }
        public string Message { get; set; }
        public long NewRevision { get; set; }
        // Optional diagnostics populated by host-only wallet-mode migrations.
        // Existing transaction callers can continue to use Status/Message/
        // NewRevision without needing to know about these fields.
        public string Diagnostic { get; set; }
        public WalletMode? SourceMode { get; set; }
        public WalletMode? TargetMode { get; set; }
        public bool StateChanged { get; set; }
        public long ActiveBalanceMoved { get; set; }
        public int SourceAccountCount { get; set; }
        public int TermDepositsMoved { get; set; }
        public int LoanContractsMoved { get; set; }
        public long ActiveSetoffAmount { get; set; }
        public long WalletCollectionAmount { get; set; }
        public long InterestPaid { get; set; }
        public long PrincipalPaid { get; set; }
        public long RemainingDebt { get; set; }
        public List<TermMaturity> TermMaturities { get; set; } = new List<TermMaturity>();
        public static TransactionResult Applied(string message, long revision) { return new TransactionResult { Status = TransactionStatus.Applied, Message = message, NewRevision = revision }; }
        public static TransactionResult Rejected(string message, long revision) { return new TransactionResult { Status = TransactionStatus.Rejected, Message = message, NewRevision = revision }; }
    }

    public sealed class BankSnapshot
    {
        public long WalletBalance { get; set; }
        public long ActiveBalance { get; set; }
        public long TermBalance { get; set; }
        public long LoanPrincipal { get; set; }
        public long LoanInterest { get; set; }
        public long CreditLimit { get; set; }
        public long Revision { get; set; }
    }

    public static class BankStateValidator
    {
        public static IReadOnlyList<string> Validate(BankState state)
        {
            var errors = new List<string>();
            if (state == null) { errors.Add("state is null"); return errors; }
            if (state.SchemaVersion != BankState.CurrentSchemaVersion) errors.Add("unsupported schema version");
            if (!Enum.IsDefined(typeof(WalletMode), state.WalletMode)) errors.Add("invalid wallet mode");
            if (state.Revision < 0) errors.Add("negative revision");
            if (state.LastSettlementDay.HasValue && state.LastSettlementDay.Value < 0) errors.Add("negative settlement day");
            ValidateAccount(errors, state.SharedAccount, "shared account");
            if (state.PersonalAccounts == null) errors.Add("personal account map is null");
            else
            {
                if (state.PersonalAccounts.Count > BankLimits.MaximumPersonalAccounts) errors.Add("personal account map exceeds " + BankLimits.MaximumPersonalAccounts + " entries");
                foreach (var pair in state.PersonalAccounts) ValidateAccount(errors, pair.Value, "personal account " + pair.Key);
            }
            if (state.ProcessedTransactionIds == null) errors.Add("processed transaction list is null");
            else
            {
                if (state.ProcessedTransactionIds.Count > 256) errors.Add("processed transaction list exceeds 256 entries");
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var id in state.ProcessedTransactionIds)
                    if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || !ids.Add(id)) errors.Add("invalid processed transaction ID");
            }
            return errors;
        }

        private static void ValidateAccount(List<string> errors, Account account, string label)
        {
            if (account == null) { errors.Add(label + " is null"); return; }
            if (string.IsNullOrWhiteSpace(account.Id) || account.Id.Length > 128) errors.Add(label + " has an invalid ID");
            if (account.ActiveBalance < 0) errors.Add(label + " has a negative active balance");
            if (!BankMath.IsValidInterestRemainder(account.ActiveInterestRemainder)) errors.Add(label + " has an invalid active-interest remainder");
            ValidateCreditRestriction(errors, account.CreditRestriction, label);
            if (account.TermDeposits == null) errors.Add(label + " has a null term list");
            else
            {
                if (account.TermDeposits.Count > BankLimits.MaximumTermDepositsPerAccount) errors.Add(label + " exceeds the open term-deposit limit");
                var ids = new HashSet<string>(StringComparer.Ordinal);
                decimal totalTermBalance = 0;
                foreach (var deposit in account.TermDeposits)
                {
                    if (deposit == null) { errors.Add(label + " has a null term deposit"); continue; }
                    if (string.IsNullOrWhiteSpace(deposit.Id) || deposit.Id.Length > 128 || !ids.Add(deposit.Id)) errors.Add(label + " has an invalid term ID");
                    if (deposit.Principal <= 0 || deposit.Balance < deposit.Principal) errors.Add(label + " has an invalid term balance");
                    if (deposit.TermDays <= 0 || deposit.TermDays > 1120 || deposit.DaysSettled < 0 || deposit.DaysSettled > deposit.TermDays) errors.Add(label + " has invalid term days");
                    if (deposit.OpenDay < 0 || (long)deposit.OpenDay + deposit.TermDays != deposit.MaturityDay) errors.Add(label + " has invalid term dates");
                    if (deposit.LockedTermRatePercent < 0 || deposit.LockedTermRatePercent > BankLimits.MaximumDepositDailyRatePercent || deposit.LockedActivityRatePercent < 0 || deposit.LockedActivityRatePercent > BankLimits.MaximumDepositDailyRatePercent) errors.Add(label + " has invalid term rates");
                    if (!BankMath.IsValidInterestRemainder(deposit.InterestRemainder)) errors.Add(label + " has an invalid term-interest remainder");
                    if (deposit.Balance >= 0 && BankMath.IsValidInterestRemainder(deposit.InterestRemainder))
                        totalTermBalance += (decimal)deposit.Balance + decimal.Round(deposit.InterestRemainder, 0, MidpointRounding.AwayFromZero);
                }
                if (totalTermBalance > long.MaxValue) errors.Add(label + " has an unsupported aggregate term balance");
            }
            if (account.Loan != null)
            {
                ValidateLoan(errors, account.Loan, label);
            }
        }

        private static void ValidateCreditRestriction(List<string> errors, CreditRestriction restriction, string label)
        {
            if (restriction == null) { errors.Add(label + " has a null credit restriction"); return; }
            if (!Enum.IsDefined(typeof(CreditRestrictionMode), restriction.Mode))
            {
                errors.Add(label + " has an invalid credit restriction");
                return;
            }
            if (restriction.Mode == CreditRestrictionMode.UntilDay)
            {
                if (!restriction.UntilDay.HasValue || restriction.UntilDay.Value < 0)
                    errors.Add(label + " has an invalid credit-restriction date");
            }
            else if (restriction.UntilDay.HasValue)
            {
                errors.Add(label + " has an unexpected credit-restriction date");
            }
        }

        private static void ValidateLoan(List<string> errors, LoanContract loan, string label)
        {
            if (string.IsNullOrWhiteSpace(loan.Id) || loan.Id.Length > 128) errors.Add(label + " has an invalid loan ID");
            var validLoanRemainder = BankMath.IsValidInterestRemainder(loan.InterestRemainder);
            if (!validLoanRemainder) errors.Add(label + " has an invalid loan-interest remainder");
            if (!BankMath.IsValidInterestRemainder(loan.GarnishmentRemainder)) errors.Add(label + " has an invalid garnishment remainder");
            if (loan.OriginalPrincipal < 0 || loan.Principal < 0 || loan.Principal > loan.OriginalPrincipal || loan.AccruedInterest < 0
                || (validLoanRemainder && (decimal)loan.Principal + loan.AccruedInterest + decimal.Round(loan.InterestRemainder, 0, MidpointRounding.AwayFromZero) > long.MaxValue))
                errors.Add(label + " has an invalid loan balance");
            if (loan.TermDays <= 0 || loan.TermDays > BankLimits.MaximumCollectionDays) errors.Add(label + " has invalid loan days");
            var expectedMaturity = (long)loan.OpenDay + loan.TermDays;
            var expectedCollection = expectedMaturity + loan.LockedGraceDays + 1L;
            var expectedDefault = expectedCollection + loan.LockedDefaultAfterDays;
            if (loan.OpenDay < 0 || expectedMaturity != loan.MaturityDay || expectedCollection != loan.CollectionStartDay
                || expectedDefault != loan.DefaultDay || loan.LastSettledDay < loan.OpenDay)
                errors.Add(label + " has invalid loan dates");
            if (loan.LockedGraceDays < 0 || loan.LockedGraceDays > BankLimits.MaximumCollectionDays
                || loan.LockedDefaultAfterDays < 1 || loan.LockedDefaultAfterDays > BankLimits.MaximumCollectionDays)
                errors.Add(label + " has invalid collection days");
            if (loan.LockedRatePercent < 0 || loan.LockedRatePercent > BankLimits.MaximumLoanDailyRatePercent
                || loan.OverdueMultiplier < 1 || loan.OverdueMultiplier > BankLimits.MaximumOverdueRateMultiplier)
                errors.Add(label + " has invalid loan rates");
            if (loan.LockedMinimumActiveBalanceReserve < 0 || loan.LockedMinimumActiveBalanceReserve > int.MaxValue
                || loan.LockedMinimumWalletReserve < 0 || loan.LockedMinimumWalletReserve > int.MaxValue
                || loan.LockedIncomeGarnishmentPercent < 0 || loan.LockedIncomeGarnishmentPercent > BankLimits.MaximumCollectionPercent
                || loan.LockedMaximumOverdueInterestPercent < 0 || loan.LockedMaximumOverdueInterestPercent > BankLimits.MaximumOverdueInterestCapPercent)
                errors.Add(label + " has invalid locked collection terms");
            if (loan.OverdueInterestCharged < 0) errors.Add(label + " has negative overdue interest charged");
            if (loan.LockedMaximumOverdueInterestPercent > 0
                && loan.OverdueInterestCharged > (decimal)loan.OriginalPrincipal * loan.LockedMaximumOverdueInterestPercent / 100m)
                errors.Add(label + " exceeds its overdue-interest cap");
            if (loan.LastActiveSetoffDay.HasValue && loan.LastActiveSetoffDay.Value < loan.CollectionStartDay)
                errors.Add(label + " has an invalid active-setoff checkpoint");
            if (loan.LastWalletSweepDay.HasValue && loan.LastWalletSweepDay.Value < loan.CollectionStartDay)
                errors.Add(label + " has an invalid wallet-sweep checkpoint");
            if (!Enum.IsDefined(typeof(LoanStatus), loan.Status)) errors.Add(label + " has an invalid loan status");
            if (!Enum.IsDefined(typeof(LoanResolutionKind), loan.Resolution)) errors.Add(label + " has an invalid loan resolution");

            var terminal = loan.Status == LoanStatus.Settled || loan.Status == LoanStatus.Cancelled;
            if (terminal)
            {
                if (!loan.ResolvedDay.HasValue || loan.ResolvedDay.Value < loan.OpenDay || loan.Resolution == LoanResolutionKind.None)
                    errors.Add(label + " has incomplete terminal-loan metadata");
                if (loan.Principal != 0 || loan.AccruedInterest != 0 || loan.InterestRemainder != 0m || loan.GarnishmentRemainder != 0m)
                    errors.Add(label + " has a terminal loan with a remaining balance");
                if (loan.Status == LoanStatus.Settled && loan.Resolution != LoanResolutionKind.Repaid)
                    errors.Add(label + " has an invalid settled-loan resolution");
                if (loan.Status == LoanStatus.Cancelled && loan.Resolution != LoanResolutionKind.AdministrativeCancellation)
                    errors.Add(label + " has an invalid cancelled-loan resolution");
            }
            else if (loan.ResolvedDay.HasValue || loan.Resolution != LoanResolutionKind.None)
            {
                errors.Add(label + " has resolution metadata on an open loan");
            }
        }
    }

    public static class BankStateMigrator
    {
        public static bool TryMigrateToCurrent(BankState source, out BankState migrated)
        {
            var day = source != null && source.LastSettlementDay.HasValue ? source.LastSettlementDay.Value : 0;
            return TryMigrateToCurrent(source, new BankConfig(), day, out migrated);
        }

        public static bool TryMigrateToCurrent(BankState source, BankConfig config, int absoluteDay, out BankState migrated)
        {
            migrated = null;
            if (source == null || config == null || absoluteDay < 0) return false;
            if (source.SchemaVersion != 1 && source.SchemaVersion != 2 && source.SchemaVersion != BankState.CurrentSchemaVersion) return false;
            // This is deliberately an in-memory upgrade of the object read by
            // SMAPI. Avoid cloning here: BankState.Clone supplies safe fallback
            // containers for rollback, which could otherwise hide malformed
            // persisted shapes before SaveRepository validates them.
            if (source.SchemaVersion == 1)
            {
                InitializeFractionalRemainders(source);
                source.SchemaVersion = 2;
            }
            if (source.SchemaVersion == 2)
            {
                InitializeCollectionContracts(source, config, absoluteDay);
                source.SchemaVersion = 3;
            }
            migrated = source;
            return true;
        }

        private static void InitializeFractionalRemainders(BankState state)
        {
            foreach (var account in Accounts(state))
            {
                if (account == null) continue;
                account.ActiveInterestRemainder = 0m;
                if (account.TermDeposits != null)
                    foreach (var term in account.TermDeposits)
                        if (term != null) term.InterestRemainder = 0m;
                if (account.Loan != null) account.Loan.InterestRemainder = 0m;
            }
        }

        private static void InitializeCollectionContracts(BankState state, BankConfig config, int absoluteDay)
        {
            foreach (var account in Accounts(state))
            {
                if (account == null) continue;
                account.CreditRestriction = new CreditRestriction();
                var loan = account.Loan;
                if (loan == null) continue;
                loan.OriginalPrincipal = loan.Principal;
                loan.OverdueInterestCharged = 0m;
                loan.LockedCollectionEnabled = config.EnableOverdueCollection;
                loan.LockedGraceDays = config.OverdueGraceDays;
                loan.LockedActiveBalanceSetoff = config.EnableActiveBalanceSetoff;
                loan.LockedMinimumActiveBalanceReserve = config.MinimumActiveBalanceReserve;
                loan.LockedMinimumWalletReserve = config.MinimumWalletReserve;
                loan.LockedIncomeGarnishmentPercent = config.IncomeGarnishmentPercent;
                loan.LockedMaximumOverdueInterestPercent = config.MaximumOverdueInterestPercent;
                loan.LockedDefaultAfterDays = config.DefaultAfterDays;
                loan.GarnishmentRemainder = 0m;
                loan.CollectionStartDay = SaturatingDay((long)loan.MaturityDay + config.OverdueGraceDays + 1L);
                loan.DefaultDay = SaturatingDay((long)loan.CollectionStartDay + config.DefaultAfterDays);
                loan.LastActiveSetoffDay = null;
                loan.LastWalletSweepDay = null;
                if (loan.Status == LoanStatus.Settled)
                {
                    loan.Principal = 0;
                    loan.AccruedInterest = 0;
                    loan.InterestRemainder = 0m;
                    loan.ResolvedDay = Math.Max(loan.OpenDay, Math.Min(absoluteDay, loan.LastSettledDay));
                    loan.Resolution = LoanResolutionKind.Repaid;
                }
                else
                {
                    loan.ResolvedDay = null;
                    loan.Resolution = LoanResolutionKind.None;
                    loan.Status = loan.GetOpenStatusForDay(absoluteDay);
                }
            }
        }

        private static int SaturatingDay(long day)
        {
            if (day > int.MaxValue) return int.MaxValue;
            if (day < int.MinValue) return int.MinValue;
            return (int)day;
        }

        private static IEnumerable<Account> Accounts(BankState state)
        {
            if (state.SharedAccount != null) yield return state.SharedAccount;
            if (state.PersonalAccounts == null) yield break;
            foreach (var account in state.PersonalAccounts.Values) yield return account;
        }
    }
}
