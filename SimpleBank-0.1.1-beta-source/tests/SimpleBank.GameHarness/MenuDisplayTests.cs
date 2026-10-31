using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using SimpleBank;
using SimpleBank.Core;

public static partial class Program
{
    private static void TestMaturityDisplay()
    {
        foreach (var locale in new[] { "zh", "default" })
        {
            var f = new FundingFixture(false, locale);
            var wallet = new GameWalletAdapter(f.Host, WalletMode.Shared, f.Diagnostics);
            Assert(f.Bank.CreateTermDeposit(f.Host.UniqueMultiplayerID, wallet, 0, 1000, 10, "display-term", f.Bank.State.Revision).Status == TransactionStatus.Applied,
                "display fixture opens a real term contract");
            Assert(f.Bank.RequestLoan(f.Host.UniqueMultiplayerID, wallet, 0, 1000, 10, "display-loan", f.Bank.State.Revision).Status == TransactionStatus.Applied,
                "display fixture opens a real loan contract");
            var menu = f.OpenHostMenu();
            foreach (var day in new[] { 10, 11, 17 })
            {
                Game1.stats.DaysPlayed = day;
                var due = day == 17 ? f.Text.Get("menu.maturity.today") : f.Text.Get("menu.maturity.days", new { days = 17 - day });
                foreach (var tabName in new[] { "term", "loan", "overview" })
                {
                    ClickFundingButton(menu, f.Text.Get("menu.tab." + tabName));
                    var drawn = DrawnFundingText(menu);
                    Assert(drawn.Any(text => text.Contains(due)) && drawn.All(text => !text.Contains("{{")),
                        "maturity uses the current game day, including today, in " + tabName + ": " + locale);
                    if (tabName == "loan")
                        Assert(drawn.Where(text => text.Contains(due)).Single().Split(new[] { due }, StringSplitOptions.None).Length == 2,
                            "the current loan shows its maturity once");
                }
            }
            foreach (var day in new[] { 18, 25, 53 })
            {
                Game1.stats.DaysPlayed = day;
                Assert(f.Bank.SettleToDay(day, "display-day-" + day, f.Bank.State.Revision).Status == TransactionStatus.Applied,
                    "loan display follows real settlement stages");
                ClickFundingButton(menu, f.Text.Get("menu.tab.loan"));
                var overdue = f.Text.Get("menu.maturity.overdue", new { days = day - 17 });
                var next = day == 18 ? f.Text.Get("menu.loan.next.collection", new { days = 7 })
                    : day == 25 ? f.Text.Get("menu.loan.next.default", new { days = 28 })
                    : f.Text.Get("menu.loan.next.continues");
                Assert(DrawnFundingText(menu).Any(text => text.Contains(overdue) && text.Contains(next)),
                    "overdue, collection and default dates stay relative: " + locale);
            }
            menu.exitThisMenu();
        }
    }

    private static void TestLoanConfirmationSummary()
    {
        foreach (var locale in new[] { "zh", "default" })
        {
            var f = new FundingFixture(false, locale);
            var menu = f.OpenHostMenu();
            ClickFundingButton(menu, f.Text.Get("menu.tab.loan"));
            ClickFundingButton(menu, "+1k");
            ClickFundingButton(menu, f.Text.Get("action.loan"));
            var summary = f.Text.Get("menu.confirm.loan", new
            {
                amount = "1,000g", days = 7, rate = 0.15m,
                collection = f.Text.Get("menu.confirm.loan.collection", new { grace = 7 })
            });
            Assert(DrawnFundingText(menu).Contains(summary) && DrawnFundingText(menu).Contains(f.Text.Get("menu.loan.terms.show")),
                "loan confirmation starts with amount, duration, rate and collection warning: " + locale);
            var initialRevision = f.Bank.State.Revision;
            foreach (var viewport in new[] { new Rectangle(0, 0, 684, 604), new Rectangle(0, 0, 1280, 720) })
            {
                Game1.uiViewport = viewport;
                ClickFundingButton(menu, f.Text.Get("menu.loan.terms.show"));
                var firstPage = LoanConfirmationBody(menu);
                var allPages = firstPage;
                for (var page = 0; page < 20; page++)
                {
                    var previous = LoanConfirmationBody(menu);
                    Assert(Game1.smallFont.MeasureString(previous).Y <= menu.height - 390,
                        "terms fit above paging and action controls (stub geometry only)");
                    ClickFundingButton(menu, f.Text.Get("menu.loan.terms.next"));
                    var next = LoanConfirmationBody(menu);
                    if (next == previous) break;
                    allPages += "\n" + next;
                }
                var fullTerms = f.Text.Get("menu.loan.terms.details", new
                {
                    collection = f.Text.Get("menu.value.enabled"), grace = 7,
                    activeSetoff = f.Text.Get("menu.value.enabled"), activeReserve = "0g", walletReserve = "1,000g",
                    garnishment = 25, multiplier = 1.5m, cap = f.Text.Get("menu.loan.terms.cap", new { cap = 100 }), defaultDays = 28
                });
                Assert(allPages == fullTerms, "paging exposes every configured term without truncation: " + locale);
                menu.receiveScrollWheelAction(1);
                Assert(LoanConfirmationBody(menu) == firstPage, "scrolling terms goes back without changing the product");
                Assert(f.Bank.State.Revision == initialRevision && f.Bank.State.SharedAccount.Loan == null,
                    "reading and paging terms cannot submit a transaction");
                menu.HandleBack();
                Assert(DrawnFundingText(menu).Contains(summary), "back returns from terms to the pending summary");
                menu.HandleConfirm();
                Assert(DrawnFundingText(menu).Contains(f.Text.Get("menu.loan.terms.back")) && f.Bank.State.SharedAccount.Loan == null,
                    "controller confirm on the focused terms button only opens terms");
                ClickFundingButton(menu, f.Text.Get("menu.loan.terms.back"));
            }
            ClickFundingButton(menu, f.Text.Get("menu.loan.terms.show"));
            menu.receiveKeyPress(Keys.D2);
            Assert(!DrawnFundingText(menu).Contains(f.Text.Get("menu.confirm.button")) && f.Bank.State.Revision == initialRevision,
                "editing the amount while reading terms cancels the old pending transaction");

            // A different configuration must be reflected in both the summary and details.
            var config = f.Bank.Config;
            config.EnableOverdueCollection = false;
            config.MaximumOverdueInterestPercent = 0;
            f.Bank.UpdateConfig(config);
            ClickFundingButton(menu, f.Text.Get("menu.keypad.clear"));
            ClickFundingButton(menu, "+1k");
            ClickFundingButton(menu, f.Text.Get("action.loan"));
            Assert(LoanConfirmationBody(menu).Contains(f.Text.Get("menu.confirm.loan.manual")),
                "disabled collection has an accurate short warning");
            ClickFundingButton(menu, f.Text.Get("menu.loan.terms.show"));
            Assert(LoanConfirmationBody(menu).Contains(f.Text.Get("menu.loan.terms.uncapped")),
                "zero overdue-interest cap is displayed as unlimited");
            ClickFundingButton(menu, f.Text.Get("menu.cancel.button"));
            Assert(f.Bank.State.SharedAccount.Loan == null, "cancel from details does not issue a loan");
            ClickFundingButton(menu, f.Text.Get("action.loan"));
            Assert(DrawnFundingText(menu).Contains(f.Text.Get("menu.loan.terms.show")), "a new application starts collapsed");
            ClickFundingButton(menu, f.Text.Get("menu.confirm.button"));
            var loan = f.Bank.State.SharedAccount.Loan;
            Assert(loan != null && loan.Principal == 1000 && loan.TermDays == 7 && loan.LockedRatePercent == 0.15m
                && !loan.LockedCollectionEnabled && loan.LockedMaximumOverdueInterestPercent == 0
                && f.Bank.State.Revision == initialRevision + 1 && f.Team.GetIndividualMoney(f.Host) == 6000,
                "explicit confirmation issues exactly the reviewed loan once");
            menu.exitThisMenu();
        }
        Game1.uiViewport = new Rectangle(0, 0, 1920, 1080);
    }

    private static string LoanConfirmationBody(AtmMenu menu)
    {
        DrawnFundingText(menu);
        return Utility.DrawnTexts.Single(text => text.Position.X == menu.xPositionOnScreen + 28
            && text.Position.Y == menu.yPositionOnScreen + 232).Text;
    }
}
