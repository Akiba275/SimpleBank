using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewValley;
using SimpleBank.Core;

namespace SimpleBank
{
    // One instance per local screen. Receipts and deduplication live only for this session.
    internal sealed class MaturityNotifier
    {
        private readonly ITranslationHelper i18n;
        private readonly ModDiagnostics diagnostics;
        private readonly HashSet<string> notifiedDeposits = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> notifiedLoans = new HashSet<string>(StringComparer.Ordinal);
        private int? notificationDay;

        public MaturityNotifier(ITranslationHelper i18n, ModDiagnostics diagnostics)
        {
            this.i18n = i18n;
            this.diagnostics = diagnostics;
        }

        public void Check(BankService bank, long playerId, int day, IEnumerable<TermMaturity> receipts)
        {
            // Wait for the host's settlement, including today's final interest, and
            // for the local game date to catch up if a snapshot arrives during loading.
            if (bank == null || bank.State.DataInvalid || bank.State.ModeMismatch || bank.State.LastSettlementDay != day) return;
            if (notificationDay != day)
            {
                Reset();
                notificationDay = day;
            }

            var matured = receipts.Where(receipt => receipt.Day == day
                && (!receipt.PlayerId.HasValue || receipt.PlayerId == playerId)
                && notifiedDeposits.Add(receipt.DepositId)).ToList();
            if (matured.Count > 0)
            {
                Game1.addHUDMessage(new HUDMessage(i18n.Get("message.term-matured", new { count = matured.Count }), HUDMessage.newQuest_type));
                if (diagnostics.Enabled)
                {
                    // Decimal avoids overflow when summing receipts for diagnostics.
                    var amount = matured.Sum(receipt => (decimal)receipt.Amount);
                    diagnostics.Log("maturity-notification: term-shown, screenId=" + Context.ScreenId + ", playerId=" + playerId
                        + ", day=" + day + ", depositIds=" + string.Join(",", matured.Select(receipt => receipt.DepositId)) + ", amount=" + amount);
                }
            }

            Account account;
            if (bank.State.WalletMode == WalletMode.Shared) account = bank.State.SharedAccount;
            else if (!bank.State.PersonalAccounts.TryGetValue(playerId, out account)) return;
            var loan = account == null ? null : account.Loan;
            if (loan == null || loan.Status != LoanStatus.Current || loan.MaturityDay != day
                || string.IsNullOrWhiteSpace(loan.Id) || !notifiedLoans.Add(loan.Id)) return;
            Game1.addHUDMessage(new HUDMessage(i18n.Get("message.loan-due-today"), HUDMessage.error_type));
            if (diagnostics.Enabled)
                diagnostics.Log("maturity-notification: loan-shown, screenId=" + Context.ScreenId + ", playerId=" + playerId
                    + ", day=" + day + ", loanId=" + loan.Id + ", principal=" + loan.Principal + ", interest=" + loan.PayableInterest);
        }

        public void Reset()
        {
            notificationDay = null;
            notifiedDeposits.Clear();
            notifiedLoans.Clear();
        }
    }
}
