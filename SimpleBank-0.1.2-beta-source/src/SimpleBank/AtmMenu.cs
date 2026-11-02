using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using SimpleBank.Core;

namespace SimpleBank
{
    internal sealed class AtmMenu : IClickableMenu
    {
        private const int CloseId = 10;
        private const int TabId = 100;
        private const int ProductPreviousId = 200;
        private const int ProductNextId = 201;
        private const int TermPreviousId = 202;
        private const int TermNextId = 203;
        private const int KeypadId = 300;
        private const int ActiveWithdrawAllId = 320;
        private const int ActiveWithdrawHalfId = 321;
        private const int ActiveWithdrawQuarterId = 322;
        private const int TermFromWalletId = 330;
        private const int TermFromActiveId = 331;
        private const int PrimaryId = 400;
        private const int SecondaryId = 401;
        private const int LoanTermsId = 402;
        private const int LoanTermsPreviousId = 403;
        private const int LoanTermsNextId = 404;

        private readonly BankService bank;
        private readonly MultiplayerCoordinator multiplayer;
        private readonly Farmer farmer;
        private readonly ITranslationHelper i18n;
        private readonly InputConfig inputConfig;
        private readonly ModDiagnostics diagnostics;
        private readonly GameWalletAdapter wallet;
        private readonly List<MenuButton> buttons = new List<MenuButton>();
        private readonly string[] tabs = { "overview", "active", "term", "loan" };
        private readonly string[] keypadLabels = { "7", "8", "9", "+1k", "4", "5", "6", "-1k", "1", "2", "3", "max", "clear", "0", "00", "back" };

        private int tab;
        private long amount;
        private int productIndex;
        private int termDepositIndex;
        private bool termFromActive;
        private bool typingAmount;
        private bool awaitingConfirmation;
        private int loanTermsPage = -1;
        private bool IsLoanConfirmation => awaitingConfirmation && pendingAction == "loan";
        private string pendingAction;
        private string remoteAction;
        private string statusMessage;
        private int hoveredId = -1;
        private int layoutViewportWidth = -1;
        private int layoutViewportHeight = -1;
        private BankConfig cachedConfig;
        private long cachedConfigRevision = -1;
        private int[] cachedTermProductIndices = Array.Empty<int>();
        private int[] cachedLoanProductIndices = Array.Empty<int>();
        private BankSnapshot cachedSnapshot;
        private long cachedSnapshotRevision = -1;
        private long cachedSnapshotConfigRevision = -1;
        private long cachedWalletBalance = -1;
        private long cachedHistoricalEarnings = -1;
        private string pendingSnapshotReason = "menu-open";
        private TermDeposit[] cachedTermDeposits = Array.Empty<TermDeposit>();
        private long cachedTermDepositRevision = -1;

        public AtmMenu(BankService bank, MultiplayerCoordinator multiplayer, Farmer farmer, ITranslationHelper i18n, InputConfig inputConfig, ModDiagnostics diagnostics)
            : base(0, 0, 820, 620, false)
        {
            this.bank = bank;
            this.multiplayer = multiplayer;
            this.farmer = farmer;
            this.i18n = i18n;
            this.inputConfig = inputConfig;
            this.diagnostics = diagnostics;
            wallet = new GameWalletAdapter(farmer, bank.State.WalletMode, diagnostics);
            multiplayer.ResponseReceived += OnResponseReceived;
            multiplayer.BankStateChanged += OnBankStateChanged;
            GetCachedConfig();
            UpdateLayout(true);
            currentlySnappedComponent = FindButton(TabId).Component;
            if (diagnostics.Enabled)
                diagnostics.Log("atm-ui: opened, screenId=" + Context.ScreenId + ", playerId=" + farmer.UniqueMultiplayerID + ", mode=" + bank.State.WalletMode + ", revision=" + bank.State.Revision);
        }

        public override void draw(SpriteBatch b)
        {
            UpdateLayout(false);
            var snapshot = GetCachedSnapshot();
            var currentConfig = GetCachedConfig();
            drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), xPositionOnScreen, yPositionOnScreen, width, height, Color.White);

            DrawHeader(b);
            DrawTabs(b);
            DrawSummaryCards(b, snapshot);
            DrawWorkspacePanels(b);
            DrawPageContent(b, snapshot, currentConfig);
            DrawKeypad(b);
            DrawActiveWithdrawalShortcuts(b);
            DrawTermSources(b);
            DrawActionButtons(b);

            var footer = statusMessage ?? i18n.Get("menu.controls.compact", new
            {
                page = inputConfig.KeyboardNextPage,
                confirm = inputConfig.ControllerConfirm
            });
            var footerPosition = new Vector2(xPositionOnScreen + 30, yPositionOnScreen + height - 42);
            Utility.drawTextWithShadow(b, Game1.parseText(footer, Game1.smallFont, width - 60), Game1.smallFont, footerPosition, Game1.textColor * 0.82f);
            drawMouse(b);
        }

        protected override void cleanupBeforeExit()
        {
            if (diagnostics.Enabled)
                diagnostics.Log("atm-ui: closed, screenId=" + Context.ScreenId + ", playerId=" + farmer.UniqueMultiplayerID + ", tab=" + tabs[tab] + ", pendingAction=" + (pendingAction ?? "none") + ", remoteAction=" + (remoteAction ?? "none"));
            multiplayer.ResponseReceived -= OnResponseReceived;
            multiplayer.BankStateChanged -= OnBankStateChanged;
            base.cleanupBeforeExit();
        }

        public override void receiveKeyPress(Keys key)
        {
            // Keyboard input is intentionally limited to amount editing. Page
            // switching is handled through the configurable SMAPI binding.
            if (!RequiresAmountInput() || !string.IsNullOrEmpty(remoteAction) || IsConfiguredMenuCommandPressed()) return;
            var editsAmount = key >= Keys.D0 && key <= Keys.D9 || key >= Keys.NumPad0 && key <= Keys.NumPad9 || key == Keys.Back || key == Keys.Delete;
            if (awaitingConfirmation && editsAmount) CancelConfirmation();
            if (key >= Keys.D0 && key <= Keys.D9) SetDigit((int)key - (int)Keys.D0);
            else if (key >= Keys.NumPad0 && key <= Keys.NumPad9) SetDigit((int)key - (int)Keys.NumPad0);
            else if (key == Keys.Back) BackspaceAmount();
            else if (key == Keys.Delete) ClearAmount();
        }

        public override void receiveGamePadButton(Buttons button)
        {
            // All controller commands are handled by ModEntry through the
            // configurable KeybindList values and ButtonsChanged event.
        }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            UpdateLayout(false);
            var button = buttons.LastOrDefault(p => p.Enabled && p.Component.containsPoint(x, y));
            if (button != null) Activate(button.Id);
        }

        public override void performHoverAction(int x, int y)
        {
            hoveredId = buttons.LastOrDefault(p => p.Enabled && p.Component.containsPoint(x, y))?.Id ?? -1;
        }

        public override void receiveScrollWheelAction(int direction)
        {
            if (IsLoanConfirmation && loanTermsPage >= 0)
            {
                ChangeLoanTermsPage(direction > 0 ? -1 : 1);
                return;
            }
            if (tab == 2 || tab == 3) ChangeProduct(direction > 0 ? -1 : 1);
        }

        internal void HandlePage(int direction)
        {
            ChangeTab(direction);
        }

        internal void HandleBack()
        {
            if (IsLoanConfirmation && loanTermsPage >= 0)
            {
                loanTermsPage = -1;
                UpdateLayout(true);
                currentlySnappedComponent = FindButton(LoanTermsId).Component;
            }
            else if (awaitingConfirmation) CancelConfirmation();
            else exitThisMenu();
        }

        internal void HandleConfirm()
        {
            UpdateLayout(false);
            if (currentlySnappedComponent == null)
                currentlySnappedComponent = FindButton(PrimaryId)?.Component ?? FindButton(TabId + tab)?.Component;
            if (currentlySnappedComponent == null) return;
            Activate(currentlySnappedComponent.myID);
        }

        internal void HandleNavigation(int horizontal, int vertical)
        {
            UpdateLayout(false);
            var enabled = buttons.Where(p => p.Enabled).ToList();
            if (enabled.Count == 0) return;
            var current = enabled.FirstOrDefault(p => currentlySnappedComponent != null && p.Id == currentlySnappedComponent.myID)
                ?? enabled.FirstOrDefault(p => p.Id == TabId + tab)
                ?? enabled[0];
            var center = Center(current.Component.bounds);
            MenuButton best = null;
            var bestScore = double.MaxValue;

            foreach (var candidate in enabled)
            {
                if (candidate.Id == current.Id) continue;
                var target = Center(candidate.Component.bounds);
                var dx = target.X - center.X;
                var dy = target.Y - center.Y;
                if (horizontal > 0 && dx <= 2 || horizontal < 0 && dx >= -2 || vertical > 0 && dy <= 2 || vertical < 0 && dy >= -2) continue;
                var primary = horizontal == 0 ? Math.Abs(dy) : Math.Abs(dx);
                var secondary = horizontal == 0 ? Math.Abs(dx) : Math.Abs(dy);
                var score = primary + secondary * 2.35;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            if (best == null) return;
            currentlySnappedComponent = best.Component;
            if (Game1.options.SnappyMenus) snapCursorToCurrentSnappedComponent();
            Game1.playSound("shiny4");
        }

        private void UpdateLayout(bool force)
        {
            if (!force && layoutViewportWidth == Game1.uiViewport.Width && layoutViewportHeight == Game1.uiViewport.Height) return;
            var focusId = currentlySnappedComponent?.myID ?? TabId + tab;
            layoutViewportWidth = Game1.uiViewport.Width;
            layoutViewportHeight = Game1.uiViewport.Height;
            width = Math.Min(820, Math.Max(620, Game1.uiViewport.Width - 64));
            height = Math.Min(620, Math.Max(540, Game1.uiViewport.Height - 64));
            xPositionOnScreen = (Game1.uiViewport.Width - width) / 2;
            yPositionOnScreen = (Game1.uiViewport.Height - height) / 2;
            RebuildButtons();
            currentlySnappedComponent = FindButton(focusId)?.Component ?? FindButton(TabId + tab)?.Component;
        }

        private void RebuildButtons()
        {
            buttons.Clear();
            AddButton(CloseId, new Rectangle(xPositionOnScreen + width - 54, yPositionOnScreen + 18, 34, 32), "×", IconKind.Close);

            var tabGap = 8;
            var tabWidth = (width - 56 - tabGap * 3) / 4;
            for (var i = 0; i < tabs.Length; i++)
            {
                var bounds = new Rectangle(xPositionOnScreen + 28 + i * (tabWidth + tabGap), yPositionOnScreen + 58, tabWidth, 46);
                AddButton(TabId + i, bounds, i18n.Get("menu.tab." + tabs[i]), (IconKind)(i + 1), selected: i == tab);
            }

            var bodyTop = yPositionOnScreen + 224;
            var keypadWidth = 238;
            var showKeypad = RequiresAmountInput() && !IsLoanConfirmation;
            var keypadLeft = showKeypad ? xPositionOnScreen + width - 28 - keypadWidth : xPositionOnScreen + width - 28;
            var contentLeft = xPositionOnScreen + 28;
            var leftWidth = keypadLeft - contentLeft - (showKeypad ? 18 : 0);
            var actionTop = yPositionOnScreen + height - 108;

            if ((tab == 2 || tab == 3) && !awaitingConfirmation)
            {
                AddButton(ProductPreviousId, new Rectangle(xPositionOnScreen + 28, bodyTop + 112, 46, 42), "‹", IconKind.ArrowLeft);
                AddButton(ProductNextId, new Rectangle(xPositionOnScreen + 28 + leftWidth - 46, bodyTop + 112, 46, 42), "›", IconKind.ArrowRight);
            }
            if (tab == 2 && !awaitingConfirmation && GetTermDeposits().Length > 1)
            {
                AddButton(TermPreviousId, new Rectangle(xPositionOnScreen + 28, bodyTop + 166, 38, 38), "‹", IconKind.ArrowLeft);
                AddButton(TermNextId, new Rectangle(xPositionOnScreen + 28 + leftWidth - 38, bodyTop + 166, 38, 38), "›", IconKind.ArrowRight);
            }

            if (showKeypad)
            {
                var gap = 6;
                var cellWidth = (keypadWidth - gap * 3) / 4;
                var cellHeight = tab == 1 || tab == 2
                    ? Math.Min(47, Math.Max(34, (actionTop - bodyTop - 68) / 4))
                    : 47;
                for (var index = 0; index < keypadLabels.Length; index++)
                {
                    var column = index % 4;
                    var row = index / 4;
                    var bounds = new Rectangle(keypadLeft + column * (cellWidth + gap), bodyTop + row * (cellHeight + gap), cellWidth, cellHeight);
                    AddButton(KeypadId + index, bounds, GetKeypadLabel(keypadLabels[index]), IconKind.None);
                }

                if (tab == 2)
                {
                    var sourceTop = bodyTop + cellHeight * 4 + gap * 3 + 8;
                    var sourceHeight = Math.Min(42, Math.Max(30, actionTop - sourceTop - 6));
                    var sourceWidth = (keypadWidth - gap) / 2;
                    AddButton(TermFromWalletId, new Rectangle(keypadLeft, sourceTop, sourceWidth, sourceHeight), i18n.Get("menu.term.source.wallet"), IconKind.None, selected: !termFromActive);
                    AddButton(TermFromActiveId, new Rectangle(keypadLeft + sourceWidth + gap, sourceTop, sourceWidth, sourceHeight), i18n.Get("menu.term.source.active"), IconKind.None, selected: termFromActive);
                }
                if (tab == 1)
                {
                    var shortcutTop = bodyTop + cellHeight * 4 + gap * 3 + 8;
                    var shortcutHeight = Math.Min(42, Math.Max(30, actionTop - shortcutTop - 6));
                    var shortcutWidth = (keypadWidth - gap * 2) / 3;
                    AddButton(ActiveWithdrawAllId, new Rectangle(keypadLeft, shortcutTop, shortcutWidth, shortcutHeight), i18n.Get("menu.keypad.withdraw-all"), IconKind.None);
                    AddButton(ActiveWithdrawHalfId, new Rectangle(keypadLeft + shortcutWidth + gap, shortcutTop, shortcutWidth, shortcutHeight), i18n.Get("menu.keypad.withdraw-half"), IconKind.None);
                    AddButton(ActiveWithdrawQuarterId, new Rectangle(keypadLeft + (shortcutWidth + gap) * 2, shortcutTop, keypadWidth - (shortcutWidth + gap) * 2, shortcutHeight), i18n.Get("menu.keypad.withdraw-quarter"), IconKind.None);
                }
            }

            if (tab == 0) return;

            if (IsLoanConfirmation)
            {
                var detailsVisible = loanTermsPage >= 0;
                var termsWidth = detailsVisible ? (leftWidth - 16) / 3 : leftWidth;
                AddButton(LoanTermsId, new Rectangle(contentLeft, actionTop - 42, termsWidth, 34),
                    i18n.Get(detailsVisible ? "menu.loan.terms.back" : "menu.loan.terms.show"), IconKind.None);
                if (detailsVisible)
                {
                    var pages = GetLoanTermsPages();
                    loanTermsPage = Math.Min(loanTermsPage, pages.Length - 1);
                    AddButton(LoanTermsPreviousId, new Rectangle(contentLeft + termsWidth + 8, actionTop - 42, termsWidth, 34),
                        i18n.Get("menu.loan.terms.previous"), IconKind.None, enabled: loanTermsPage > 0);
                    AddButton(LoanTermsNextId, new Rectangle(contentLeft + (termsWidth + 8) * 2, actionTop - 42, termsWidth, 34),
                        i18n.Get("menu.loan.terms.next"), IconKind.None, enabled: loanTermsPage < pages.Length - 1);
                }
            }

            var actionWidth = (leftWidth - 10) / 2;
            var primaryText = awaitingConfirmation ? i18n.Get("menu.confirm.button") : i18n.Get("action." + GetPrimaryAction());
            var secondaryText = awaitingConfirmation ? i18n.Get("menu.cancel.button") : i18n.Get("action." + GetSecondaryAction());
            var primaryEnabled = string.IsNullOrEmpty(remoteAction) && (awaitingConfirmation || IsActionAllowedDuringCollection(GetPrimaryAction()));
            var secondaryEnabled = string.IsNullOrEmpty(remoteAction) && (awaitingConfirmation || IsActionAllowedDuringCollection(GetSecondaryAction()));
            AddButton(PrimaryId, new Rectangle(contentLeft, actionTop, actionWidth, 50), primaryText, IconKind.Confirm, emphasized: true, enabled: primaryEnabled);
            AddButton(SecondaryId, new Rectangle(contentLeft + actionWidth + 10, actionTop, actionWidth, 50), secondaryText, awaitingConfirmation ? IconKind.Close : IconKind.Secondary, enabled: secondaryEnabled);
        }

        private void DrawHeader(SpriteBatch b)
        {
            var titleLeft = xPositionOnScreen + 32;
            var titleTop = yPositionOnScreen + 24;
            DrawIcon(b, IconKind.Overview, new Rectangle(titleLeft, titleTop, 22, 22), new Color(83, 62, 39));
            Utility.drawTextWithShadow(b, i18n.Get("menu.title"), Game1.smallFont, new Vector2(titleLeft + 32, titleTop + 1), Game1.textColor);
            DrawButton(b, FindButton(CloseId));
        }

        private void DrawTabs(SpriteBatch b)
        {
            foreach (var button in buttons)
                if (button.Id >= TabId && button.Id < TabId + tabs.Length) DrawButton(b, button);
        }

        private void DrawSummaryCards(SpriteBatch b, BankSnapshot snapshot)
        {
            var labels = new[]
            {
                i18n.Get("menu.summary.wallet"),
                i18n.Get("menu.summary.active"),
                i18n.Get("menu.summary.term"),
                i18n.Get("menu.summary.loan")
            };
            var loanTotal = SaturatingAdd(snapshot.LoanPrincipal, snapshot.LoanInterest);
            var values = new[] { snapshot.WalletBalance, snapshot.ActiveBalance, snapshot.TermBalance, loanTotal };
            var strip = new Rectangle(xPositionOnScreen + 28, yPositionOnScreen + 116, width - 56, 82);
            DrawPanel(b, strip, new Color(255, 248, 218), new Color(126, 79, 42));
            var cardWidth = strip.Width / 4;
            for (var i = 0; i < 4; i++)
            {
                var rect = new Rectangle(strip.X + i * cardWidth, strip.Y, i == 3 ? strip.Right - (strip.X + i * cardWidth) : cardWidth, strip.Height);
                if (i > 0)
                    b.Draw(Game1.staminaRect, new Rectangle(rect.X, rect.Y + 12, 2, rect.Height - 24), new Color(126, 79, 42) * 0.45f);
                DrawIcon(b, (IconKind)(i + 1), new Rectangle(rect.X + 10, rect.Y + 12, 24, 24), new Color(71, 80, 84));
                var labelScale = Math.Min(1f, (rect.Width - 52) / Math.Max(1f, Game1.smallFont.MeasureString(labels[i]).X));
                Utility.drawTextWithShadow(b, labels[i], Game1.smallFont, new Vector2(rect.X + 40, rect.Y + 9), Game1.textColor * 0.75f, labelScale);
                Utility.drawTextWithShadow(b, FormatGold(values[i]), Game1.smallFont, new Vector2(rect.X + 12, rect.Y + 46), Game1.textColor);
            }
        }

        private void DrawWorkspacePanels(SpriteBatch b)
        {
            var bodyTop = yPositionOnScreen + 210;
            var bodyBottom = yPositionOnScreen + height - 58;
            var showKeypad = RequiresAmountInput() && !IsLoanConfirmation;
            var keypadLeft = showKeypad ? xPositionOnScreen + width - 28 - 238 : xPositionOnScreen + width - 28;
            var contentRightPadding = showKeypad ? 8 : -8;
            DrawPanel(b, new Rectangle(xPositionOnScreen + 20, bodyTop, keypadLeft - (xPositionOnScreen + 20) - contentRightPadding, bodyBottom - bodyTop), new Color(255, 248, 220), new Color(151, 112, 68), 1);
            if (showKeypad)
            {
                var lastButton = FindButton(tab == 1 ? ActiveWithdrawQuarterId : tab == 2 ? TermFromActiveId : KeypadId + keypadLabels.Length - 1);
                var keypadPanelHeight = (lastButton?.Component.bounds.Bottom ?? bodyTop + 220) - bodyTop + 6;
                DrawPanel(b, new Rectangle(keypadLeft - 8, bodyTop, 254, keypadPanelHeight), new Color(248, 235, 199), new Color(151, 112, 68), 1);
            }
        }

        private void DrawPageContent(SpriteBatch b, BankSnapshot snapshot, BankConfig currentConfig)
        {
            var bodyTop = yPositionOnScreen + 224;
            var left = xPositionOnScreen + 28;
            if (tab == 0)
            {
                DrawOverviewContent(b, snapshot, left, bodyTop, width - 56);
                return;
            }

            var keypadLeft = xPositionOnScreen + width - 28 - 238;
            var leftWidth = keypadLeft - left - 18;
            if (IsLoanConfirmation)
            {
                var pages = loanTermsPage >= 0 ? GetLoanTermsPages() : null;
                var text = pages == null ? Game1.parseText(GetConfirmationText(), Game1.smallFont, width - 56)
                    : pages[Math.Min(loanTermsPage, pages.Length - 1)];
                Utility.drawTextWithShadow(b, text, Game1.smallFont,
                    new Vector2(left, bodyTop + 8), new Color(130, 55, 35));
                return;
            }
            if (tab == 2 && awaitingConfirmation)
            {
                // Give the full left pane to confirmation, including wrapped large amounts and early-close details.
                var text = Game1.parseText(GetConfirmationText(), Game1.smallFont, leftWidth);
                Utility.drawTextWithShadow(b, text, Game1.smallFont, new Vector2(left, bodyTop + 8), new Color(130, 55, 35));
                return;
            }
            var amountRect = new Rectangle(left, bodyTop, leftWidth, tab == 2 ? 104 : 88);
            DrawPanel(b, amountRect, new Color(232, 238, 221), new Color(75, 106, 89), 1);
            if (tab != 2)
                DrawIcon(b, IconKind.Wallet, new Rectangle(amountRect.X + 16, amountRect.Y + 19, 34, 34), new Color(50, 92, 84));
            var textLeft = amountRect.X + (tab == 2 ? 8 : 62);
            Utility.drawTextWithShadow(b, i18n.Get("menu.amount.label"), Game1.smallFont, new Vector2(textLeft, amountRect.Y + (tab == 2 ? 6 : 10)), Game1.textColor * 0.72f);
            Utility.drawTextWithShadow(b, FormatGold(amount), tab == 2 ? Game1.smallFont : Game1.dialogueFont, new Vector2(textLeft, amountRect.Y + (tab == 2 ? 38 : 33)), Game1.textColor);

            if (tab == 2)
            {
                var available = GetTermSourceBalance(snapshot);
                var balanceText = i18n.Get("menu.term.available", new { amount = FormatGold(available) });
                Utility.drawTextWithShadow(b, balanceText, Game1.smallFont, new Vector2(left + 8, bodyTop + 70), amount > available ? new Color(130, 55, 35) : Game1.textColor * 0.82f);
            }

            if (tab == 2 || tab == 3)
            {
                var product = GetProductDescription();
                var productRect = new Rectangle(left + 52, bodyTop + 112, leftWidth - 104, 42);
                DrawPanel(b, productRect, new Color(246, 234, 199), new Color(154, 111, 54), 1);
                var text = Game1.parseText(product ?? i18n.Get("menu.product.none"), Game1.smallFont, productRect.Width - 16);
                Utility.drawTextWithShadow(b, text, Game1.smallFont, new Vector2(productRect.X + 8, productRect.Y + 8), Game1.textColor);
                DrawButton(b, FindButton(ProductPreviousId));
                DrawButton(b, FindButton(ProductNextId));
            }
            else
            {
                var hint = i18n.Get("menu.active.hint", new { rate = currentConfig.ActivityDailyRatePercent });
                Utility.drawTextWithShadow(b, Game1.parseText(hint, Game1.smallFont, leftWidth), Game1.smallFont, new Vector2(left, bodyTop + 112), Game1.textColor * 0.82f);
            }

            if (awaitingConfirmation)
            {
                var confirm = Game1.parseText(GetConfirmationText(), Game1.smallFont, leftWidth);
                Utility.drawTextWithShadow(b, confirm, Game1.smallFont, new Vector2(left, bodyTop + 170), new Color(130, 55, 35));
            }
            else if (!string.IsNullOrEmpty(remoteAction))
            {
                Utility.drawTextWithShadow(b, i18n.Get("message.waiting-host"), Game1.smallFont, new Vector2(left, bodyTop + 170), new Color(85, 75, 145));
            }
            else if (tab == 2)
            {
                var account = bank.GetAccount(farmer.UniqueMultiplayerID);
                var deposit = GetSelectedTermDeposit();
                var details = deposit == null
                    ? i18n.Get("menu.term.none")
                    : i18n.Get("menu.term.details", new
                    {
                        index = termDepositIndex + 1,
                        count = account.TermDeposits.Count,
                        amount = FormatGold(deposit.PayableBalance),
                        due = FormatMaturity(deposit.MaturityDay, (int)Game1.stats.DaysPlayed)
                    });
                var detailLeft = account.TermDeposits.Count > 1 ? left + 46 : left;
                var detailWidth = account.TermDeposits.Count > 1 ? leftWidth - 92 : leftWidth;
                Utility.drawTextWithShadow(b, Game1.parseText(details, Game1.smallFont, detailWidth), Game1.smallFont, new Vector2(detailLeft, bodyTop + 170), Game1.textColor * 0.82f);
                DrawButton(b, FindButton(TermPreviousId));
                DrawButton(b, FindButton(TermNextId));
            }
            else if (tab == 3)
            {
                var loan = bank.GetAccount(farmer.UniqueMultiplayerID).Loan;
                var currentDay = (int)Game1.stats.DaysPlayed;
                var details = loan == null || loan.Status == LoanStatus.Settled || loan.Status == LoanStatus.Cancelled
                    ? i18n.Get("menu.loan.none")
                    : i18n.Get("menu.loan.details", new
                    {
                        principal = FormatGold(loan.Principal),
                        interest = FormatGold(loan.PayableInterest),
                        due = FormatMaturity(loan.MaturityDay, currentDay),
                        status = i18n.Get("menu.loan.status." + loan.Status.ToString().ToLowerInvariant()),
                        next = loan.Status == LoanStatus.Overdue
                            ? loan.LockedCollectionEnabled
                                ? i18n.Get("menu.loan.next.collection", new { days = Math.Max(0, loan.CollectionStartDay - currentDay) })
                                : i18n.Get("menu.loan.next.manual")
                            : loan.Status == LoanStatus.Collection
                                ? i18n.Get("menu.loan.next.default", new { days = Math.Max(0, loan.DefaultDay - currentDay) })
                                : loan.Status == LoanStatus.Defaulted
                                    ? i18n.Get("menu.loan.next.continues")
                                    : ""
                    });
                Utility.drawTextWithShadow(b, Game1.parseText(details, Game1.smallFont, leftWidth), Game1.smallFont, new Vector2(left, bodyTop + 170), Game1.textColor * 0.82f);
            }
        }

        private void DrawOverviewContent(SpriteBatch b, BankSnapshot snapshot, int left, int bodyTop, int contentWidth)
        {
            var accountType = i18n.Get(bank.State.WalletMode == WalletMode.Shared ? "menu.account.shared" : "menu.account.personal");
            var accountStatus = i18n.Get(bank.State.DataInvalid
                ? "menu.account.status.locked"
                : bank.State.ModeMismatch ? "menu.account.status.mismatch" : "menu.account.status.normal");
            var currentDay = (int)Game1.stats.DaysPlayed;
            var account = bank.GetAccount(farmer.UniqueMultiplayerID);
            var deposits = GetTermDeposits();
            var nearestDeposit = deposits.Length == 0 ? null : deposits[0];
            var openLoan = account.Loan != null && account.Loan.Status != LoanStatus.Settled && account.Loan.Status != LoanStatus.Cancelled ? account.Loan : null;
            var details = new[]
            {
                i18n.Get("menu.overview.account-line", new { status = accountStatus, account = accountType }),
                i18n.Get("menu.overview.active-line", new { active = FormatGold(snapshot.ActiveBalance) }),
                i18n.Get("menu.overview.term-line", new
                {
                    count = deposits.Length,
                    total = FormatGold(snapshot.TermBalance),
                    due = nearestDeposit == null ? i18n.Get("menu.maturity.none") : FormatMaturity(nearestDeposit.MaturityDay, currentDay)
                }),
                i18n.Get("menu.overview.loan-line", new
                {
                    credit = FormatGold(snapshot.CreditLimit),
                    principal = FormatGold(openLoan == null ? 0 : openLoan.Principal),
                    interest = FormatGold(openLoan == null ? 0 : openLoan.PayableInterest),
                    due = openLoan == null ? i18n.Get("menu.maturity.none") : FormatMaturity(openLoan.MaturityDay, currentDay)
                })
            };
            var panelHeight = height - 282;
            var panel = new Rectangle(left, bodyTop, contentWidth, panelHeight);
            DrawPanel(b, panel, new Color(250, 241, 207), new Color(171, 130, 76), 1);
            DrawIcon(b, IconKind.Overview, new Rectangle(panel.X + 18, panel.Y + 18, 30, 30), new Color(92, 66, 38));
            Utility.drawTextWithShadow(b, i18n.Get("menu.overview.title"), Game1.dialogueFont, new Vector2(panel.X + 62, panel.Y + 14), Game1.textColor);
            var textY = panel.Y + 68;
            foreach (var line in details)
            {
                var parsed = Game1.parseText(line, Game1.smallFont, panel.Width - 40);
                Utility.drawTextWithShadow(b, parsed, Game1.smallFont, new Vector2(panel.X + 20, textY), Game1.textColor * 0.86f);
                textY += Math.Max(36, (int)Game1.smallFont.MeasureString(parsed).Y + 8);
            }

        }

        private void DrawKeypad(SpriteBatch b)
        {
            if (!RequiresAmountInput()) return;
            foreach (var button in buttons)
                if (button.Id >= KeypadId && button.Id < KeypadId + keypadLabels.Length) DrawButton(b, button);
        }

        private void DrawActiveWithdrawalShortcuts(SpriteBatch b)
        {
            if (tab != 1) return;
            DrawButton(b, FindButton(ActiveWithdrawAllId));
            DrawButton(b, FindButton(ActiveWithdrawHalfId));
            DrawButton(b, FindButton(ActiveWithdrawQuarterId));
        }

        private void DrawActionButtons(SpriteBatch b)
        {
            DrawButton(b, FindButton(LoanTermsId));
            DrawButton(b, FindButton(LoanTermsPreviousId));
            DrawButton(b, FindButton(LoanTermsNextId));
            DrawButton(b, FindButton(PrimaryId));
            DrawButton(b, FindButton(SecondaryId));
        }

        private void DrawTermSources(SpriteBatch b)
        {
            if (tab != 2) return;
            DrawButton(b, FindButton(TermFromWalletId));
            DrawButton(b, FindButton(TermFromActiveId));
        }

        private void DrawButton(SpriteBatch b, MenuButton button)
        {
            if (button == null) return;
            var focused = currentlySnappedComponent != null && currentlySnappedComponent.myID == button.Id;
            var highlighted = button.Selected || focused || hoveredId == button.Id;
            var fill = button.Emphasized ? new Color(205, 226, 193) : button.Selected ? new Color(224, 235, 205) : new Color(255, 244, 207);
            var border = highlighted ? new Color(235, 167, 61) : new Color(127, 84, 45);
            if (!button.Enabled) fill = new Color(196, 188, 169);
            var bounds = button.Component.bounds;
            b.Draw(Game1.staminaRect, new Rectangle(bounds.X + 2, bounds.Y + 3, bounds.Width, bounds.Height), Color.Black * 0.14f);
            DrawPanel(b, bounds, fill, border, 2);
            if (highlighted)
                DrawBorder(b, new Rectangle(bounds.X + 3, bounds.Y + 3, bounds.Width - 6, bounds.Height - 6), new Color(255, 206, 91), 1);

            var textColor = button.Enabled ? Game1.textColor : Color.DimGray;
            var textSize = Game1.smallFont.MeasureString(button.Label);
            var textX = button.Component.bounds.Center.X - textSize.X / 2;
            if (button.Icon == IconKind.ArrowLeft || button.Icon == IconKind.ArrowRight)
            {
                DrawIcon(b, button.Icon, new Rectangle(button.Component.bounds.Center.X - 12, button.Component.bounds.Center.Y - 12, 24, 24), textColor);
                return;
            }
            if (button.Icon != IconKind.None && button.Icon != IconKind.Close)
            {
                DrawIcon(b, button.Icon, new Rectangle(button.Component.bounds.X + 10, button.Component.bounds.Y + (button.Component.bounds.Height - 22) / 2, 22, 22), textColor);
                textX = button.Component.bounds.X + 39;
            }
            Utility.drawTextWithShadow(b, button.Label, Game1.smallFont, new Vector2(textX, button.Component.bounds.Center.Y - textSize.Y / 2), textColor);
        }

        private static void DrawPanel(SpriteBatch b, Rectangle rect, Color fill, Color border, int thickness = 1)
        {
            b.Draw(Game1.staminaRect, rect, fill);
            if (thickness > 0) DrawBorder(b, rect, border, thickness);
        }

        private static void DrawBorder(SpriteBatch b, Rectangle rect, Color border, int thickness)
        {
            b.Draw(Game1.staminaRect, new Rectangle(rect.X, rect.Y, rect.Width, thickness), border);
            b.Draw(Game1.staminaRect, new Rectangle(rect.X, rect.Bottom - thickness, rect.Width, thickness), border);
            b.Draw(Game1.staminaRect, new Rectangle(rect.X, rect.Y + thickness, thickness, rect.Height - thickness * 2), border);
            b.Draw(Game1.staminaRect, new Rectangle(rect.Right - thickness, rect.Y + thickness, thickness, rect.Height - thickness * 2), border);
        }

        private static void DrawIcon(SpriteBatch b, IconKind icon, Rectangle rect, Color color)
        {
            var x = rect.X;
            var y = rect.Y;
            var w = rect.Width;
            var h = rect.Height;
            switch (icon)
            {
                case IconKind.Overview:
                    b.Draw(Game1.staminaRect, new Rectangle(x + 2, y + 5, w - 4, 3), color);
                    b.Draw(Game1.staminaRect, new Rectangle(x + 5, y + 2, w - 10, 3), color);
                    b.Draw(Game1.staminaRect, new Rectangle(x + 4, y + 9, 3, h - 13), color);
                    b.Draw(Game1.staminaRect, new Rectangle(x + w / 2 - 1, y + 9, 3, h - 13), color);
                    b.Draw(Game1.staminaRect, new Rectangle(x + w - 7, y + 9, 3, h - 13), color);
                    b.Draw(Game1.staminaRect, new Rectangle(x + 2, y + h - 4, w - 4, 3), color);
                    break;
                case IconKind.Wallet:
                    b.Draw(Game1.staminaRect, new Rectangle(x + 2, y + 5, w - 4, h - 8), color);
                    b.Draw(Game1.staminaRect, new Rectangle(x + w / 2, y + 9, w / 2 - 1, h / 3), Color.White * 0.75f);
                    break;
                case IconKind.Term:
                    b.Draw(Game1.staminaRect, new Rectangle(x + 3, y + 4, w - 6, h - 7), color);
                    b.Draw(Game1.staminaRect, new Rectangle(x + 6, y + 1, 3, 7), color);
                    b.Draw(Game1.staminaRect, new Rectangle(x + w - 9, y + 1, 3, 7), color);
                    b.Draw(Game1.staminaRect, new Rectangle(x + 6, y + 11, w - 12, 3), Color.White * 0.8f);
                    break;
                case IconKind.Loan:
                    b.Draw(Game1.staminaRect, new Rectangle(x + 3, y + 3, w - 6, 4), color);
                    b.Draw(Game1.staminaRect, new Rectangle(x + 6, y + 9, w - 12, 4), color);
                    b.Draw(Game1.staminaRect, new Rectangle(x + 9, y + 15, w - 18, 4), color);
                    break;
                case IconKind.ArrowLeft:
                case IconKind.ArrowRight:
                    var direction = icon == IconKind.ArrowLeft ? 1 : -1;
                    var middle = x + w / 2;
                    for (var i = -2; i <= 2; i++)
                    {
                        var px = middle + direction * Math.Abs(i) * 3;
                        b.Draw(Game1.staminaRect, new Rectangle(px, y + h / 2 + i * 3, 4, 4), color);
                    }
                    break;
                case IconKind.Confirm:
                    b.Draw(Game1.staminaRect, new Rectangle(x + 3, y + h / 2, 5, 4), color);
                    b.Draw(Game1.staminaRect, new Rectangle(x + 7, y + h / 2 + 3, 5, 4), color);
                    b.Draw(Game1.staminaRect, new Rectangle(x + 11, y + h / 2 - 4, 8, 4), color);
                    break;
                case IconKind.Secondary:
                    b.Draw(Game1.staminaRect, new Rectangle(x + 3, y + 4, w - 6, h - 8), color);
                    b.Draw(Game1.staminaRect, new Rectangle(x + 7, y + 8, w - 14, h - 16), Color.White * 0.8f);
                    break;
            }
        }

        private void Activate(int id)
        {
            var button = FindButton(id);
            if (button == null || !button.Enabled) return;

            // A pending transaction is only confirmed through the focused
            // confirmation button. Reading terms preserves the pending loan;
            // other controls abandon it before changing transaction inputs.
            if (awaitingConfirmation && id != PrimaryId && id != SecondaryId
                && id != LoanTermsId && id != LoanTermsPreviousId && id != LoanTermsNextId)
            {
                CancelConfirmation();
                button = FindButton(id);
                if (button == null || !button.Enabled) return;
            }

            currentlySnappedComponent = button.Component;
            Game1.playSound("smallSelect");
            if (id == LoanTermsId)
            {
                loanTermsPage = loanTermsPage < 0 ? 0 : -1;
                UpdateLayout(true);
                if (diagnostics.Enabled)
                    diagnostics.Log("atm-ui: loan-terms-toggled, playerId=" + farmer.UniqueMultiplayerID + ", visible=" + (loanTermsPage >= 0));
                return;
            }
            if (id == LoanTermsPreviousId) { ChangeLoanTermsPage(-1); return; }
            if (id == LoanTermsNextId) { ChangeLoanTermsPage(1); return; }
            if (id == CloseId) { HandleBack(); return; }
            if (id >= TabId && id < TabId + tabs.Length) { ChangeTab(id - TabId - tab); return; }
            if (id == ProductPreviousId) { ChangeProduct(-1); return; }
            if (id == ProductNextId) { ChangeProduct(1); return; }
            if (id == TermPreviousId) { ChangeTermDeposit(-1); return; }
            if (id == TermNextId) { ChangeTermDeposit(1); return; }
            if (id == TermFromWalletId || id == TermFromActiveId) { ChangeTermSource(id == TermFromActiveId); return; }
            if (id >= KeypadId && id < KeypadId + keypadLabels.Length) { ActivateKeypad(keypadLabels[id - KeypadId]); return; }
            if (id == ActiveWithdrawAllId) { SetActiveWithdrawalFraction(1); return; }
            if (id == ActiveWithdrawHalfId) { SetActiveWithdrawalFraction(2); return; }
            if (id == ActiveWithdrawQuarterId) { SetActiveWithdrawalFraction(4); return; }
            if (id == PrimaryId) { if (awaitingConfirmation) ConfirmOrExecute(); else BeginPrimaryAction(); return; }
            if (id == SecondaryId) { if (awaitingConfirmation) CancelConfirmation(); else BeginSecondaryAction(); }
        }

        private void ActivateKeypad(string key)
        {
            int digit;
            if (int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out digit) && key.Length == 1) SetDigit(digit);
            else if (key == "00") { SetDigit(0); SetDigit(0); }
            else if (key == "+1k") AdjustAmount(1000);
            else if (key == "-1k") AdjustAmount(-1000);
            else if (key == "max") SetMaximumAmount();
            else if (key == "clear") ClearAmount();
            else if (key == "back") BackspaceAmount();
        }

        private bool IsConfiguredMenuCommandPressed()
        {
            return inputConfig.KeyboardNextPage.JustPressed()
                || inputConfig.KeyboardClose.JustPressed()
                || inputConfig.ControllerPreviousPage.JustPressed()
                || inputConfig.ControllerNextPage.JustPressed()
                || inputConfig.ControllerConfirm.JustPressed()
                || inputConfig.ControllerBack.JustPressed()
                || inputConfig.ControllerUp.JustPressed()
                || inputConfig.ControllerDown.JustPressed()
                || inputConfig.ControllerLeft.JustPressed()
                || inputConfig.ControllerRight.JustPressed();
        }

        private void BeginAction(string action, bool requiresConfirmation)
        {
            if (!string.IsNullOrEmpty(remoteAction)) return;
            if (IsTermCreation(action))
            {
                var error = !IsActionAllowedDuringCollection(action) ? "collection-new-term-blocked"
                    : amount <= 0 ? "amount-must-be-positive"
                    : amount > GetTermSourceBalance(GetCachedSnapshot()) ? (termFromActive ? "active-insufficient" : "wallet-insufficient")
                    : GetProductIndices().Length == 0 ? "term-product-invalid" : null;
                if (error != null)
                {
                    statusMessage = i18n.Get("error." + error);
                    return;
                }
            }
            if (diagnostics.Enabled)
                diagnostics.Log("atm-ui: action-selected, playerId=" + farmer.UniqueMultiplayerID + ", action=" + action + ", amount=" + amount + ", productIndex=" + GetSelectedProductIndex() + ", confirmationRequired=" + requiresConfirmation + ", revision=" + bank.State.Revision);
            pendingAction = action;
            awaitingConfirmation = requiresConfirmation;
            loanTermsPage = -1;
            statusMessage = null;
            if (!requiresConfirmation) ConfirmOrExecute();
            UpdateLayout(true);
        }

        private void ConfirmOrExecute()
        {
            if (awaitingConfirmation)
            {
                if (diagnostics.Enabled)
                    diagnostics.Log("atm-ui: confirmation-accepted, playerId=" + farmer.UniqueMultiplayerID + ", action=" + (pendingAction ?? "none") + ", amount=" + amount);
                awaitingConfirmation = false;
                Execute(pendingAction);
            }
            else if (!string.IsNullOrEmpty(pendingAction)) Execute(pendingAction);
            else BeginPrimaryAction();
            UpdateLayout(true);
        }

        private void Execute(string action)
        {
            TransactionResult result;
            var id = "ui-" + Guid.NewGuid().ToString("N");
            if (diagnostics.Enabled)
                diagnostics.Log("atm-ui: transaction-submit, playerId=" + farmer.UniqueMultiplayerID + ", action=" + action + ", amount=" + amount + ", productIndex=" + GetSelectedProductIndex() + ", transactionId=" + id + ", revision=" + bank.State.Revision);
            if (action == "term-early")
            {
                var deposit = GetSelectedTermDeposit();
                if (deposit == null) { statusMessage = i18n.Get("error.term-not-found"); pendingAction = null; return; }
                if (!multiplayer.TryExecuteEarlyWithdrawal(farmer, deposit.Id, (int)Game1.stats.DaysPlayed, id, out result))
                {
                    remoteAction = "term-early";
                    statusMessage = i18n.Get("message.waiting-host");
                    pendingAction = null;
                    if (diagnostics.Enabled)
                        diagnostics.Log("atm-ui: waiting-host, playerId=" + farmer.UniqueMultiplayerID + ", action=term-early, transactionId=" + id);
                    return;
                }
            }
            else if (!multiplayer.TryExecuteOrRequest(farmer, action, amount, GetSelectedProductIndex(), (int)Game1.stats.DaysPlayed, id, out result))
            {
                remoteAction = action;
                statusMessage = i18n.Get("message.waiting-host");
                pendingAction = null;
                if (diagnostics.Enabled)
                    diagnostics.Log("atm-ui: waiting-host, playerId=" + farmer.UniqueMultiplayerID + ", action=" + action + ", transactionId=" + id);
                return;
            }
            statusMessage = result.Status == TransactionStatus.Applied ? GetSuccessMessage(action, result.Message) : i18n.Get("error." + result.Message);
            pendingAction = null;
            if (diagnostics.Enabled)
                diagnostics.Log("atm-ui: local-result, playerId=" + farmer.UniqueMultiplayerID + ", action=" + action + ", status=" + result.Status + ", message=" + result.Message + ", revision=" + result.NewRevision);
        }

        private void OnResponseReceived(BankResponse response)
        {
            if (Game1.activeClickableMenu != this) return;
            var action = remoteAction;
            remoteAction = null;
            statusMessage = response.Status == TransactionStatus.Applied && !string.IsNullOrEmpty(action)
                ? GetSuccessMessage(action, response.Message)
                : i18n.Get("error." + response.Message);
            if (diagnostics.Enabled)
                diagnostics.Log("atm-ui: host-response, playerId=" + farmer.UniqueMultiplayerID + ", action=" + (action ?? "none")
                    + ", status=" + response.Status
                    + ", message=" + response.Message
                    + ", revision=" + bank.State.Revision);
            UpdateLayout(true);
        }

        private void OnBankStateChanged()
        {
            // A host transaction or an accepted host snapshot can replace the
            // state without changing the local wallet value. Drop all derived
            // UI data so shared-account changes made by another player are
            // visible immediately, including equal-revision initial snapshots.
            cachedSnapshot = null;
            cachedSnapshotRevision = -1;
            cachedSnapshotConfigRevision = -1;
            cachedWalletBalance = -1;
            cachedHistoricalEarnings = -1;
            cachedTermDeposits = Array.Empty<TermDeposit>();
            cachedTermDepositRevision = -1;
            pendingSnapshotReason = "bank-state-changed";
            if (diagnostics.Enabled)
                diagnostics.Log("atm-ui: state-cache-invalidated, playerId=" + farmer.UniqueMultiplayerID + ", revision=" + bank.State.Revision + ", configRevision=" + bank.ConfigRevision);
            UpdateLayout(true);
        }

        private void SetDigit(int digit)
        {
            if (!typingAmount)
                amount = digit;
            else if (amount > (long.MaxValue - digit) / 10)
                amount = long.MaxValue;
            else
                amount = amount * 10 + digit;
            typingAmount = true;
            UpdateTermAmountFeedback();
        }

        private void BackspaceAmount()
        {
            amount /= 10;
            typingAmount = true;
            UpdateTermAmountFeedback();
        }

        private void ClearAmount()
        {
            amount = 0;
            typingAmount = true;
            UpdateTermAmountFeedback();
        }

        private void AdjustAmount(long delta)
        {
            if (delta >= 0)
                amount = amount > long.MaxValue - delta ? long.MaxValue : amount + delta;
            else
                amount = amount < -delta ? 0 : amount + delta;
            typingAmount = false;
            UpdateTermAmountFeedback();
        }

        private void SetMaximumAmount()
        {
            var snapshot = GetCachedSnapshot();
            var walletCapacity = Math.Max(0L, (long)int.MaxValue - snapshot.WalletBalance);
            if (tab == 1)
                amount = snapshot.WalletBalance;
            else if (tab == 2)
                amount = GetTermSourceBalance(snapshot);
            else
            {
                var owed = SaturatingAdd(snapshot.LoanPrincipal, snapshot.LoanInterest);
                amount = owed > 0 ? Math.Min(snapshot.WalletBalance, owed) : Math.Min(snapshot.CreditLimit, walletCapacity);
            }
            typingAmount = false;
            UpdateTermAmountFeedback();
        }

        private void SetActiveWithdrawalFraction(int denominator)
        {
            amount = BankMath.RoundedWithdrawalFraction(GetCachedSnapshot().ActiveBalance, denominator);
            typingAmount = false;
        }

        private static long SaturatingAdd(long left, long right)
        {
            return left > long.MaxValue - right ? long.MaxValue : left + right;
        }

        private void ChangeTab(int delta)
        {
            if (!string.IsNullOrEmpty(remoteAction)) return;
            var previousTab = tabs[tab];
            tab = (tab + tabs.Length + delta % tabs.Length) % tabs.Length;
            productIndex = 0;
            termDepositIndex = 0;
            pendingAction = null;
            awaitingConfirmation = false;
            statusMessage = null;
            UpdateLayout(true);
            currentlySnappedComponent = FindButton(TabId + tab).Component;
            if (diagnostics.Enabled)
            {
                if (previousTab == tabs[tab])
                    diagnostics.Log("atm-ui: tab-reselected, playerId=" + farmer.UniqueMultiplayerID + ", tab=" + tabs[tab]);
                else
                    diagnostics.Log("atm-ui: tab-changed, playerId=" + farmer.UniqueMultiplayerID + ", from=" + previousTab + ", to=" + tabs[tab]);
            }
        }

        private void ChangeProduct(int delta)
        {
            if (!string.IsNullOrEmpty(remoteAction)) return;
            var count = GetProductIndices().Length;
            if (count == 0) return;
            productIndex = (productIndex + count + delta % count) % count;
            pendingAction = null;
            awaitingConfirmation = false;
            UpdateLayout(true);
            if (diagnostics.Enabled)
                diagnostics.Log("atm-ui: product-changed, playerId=" + farmer.UniqueMultiplayerID + ", tab=" + tabs[tab] + ", displayIndex=" + productIndex + ", configIndex=" + GetSelectedProductIndex());
        }

        private void ChangeTermDeposit(int delta)
        {
            var count = GetTermDeposits().Length;
            if (count == 0) return;
            termDepositIndex = (termDepositIndex + count + delta % count) % count;
            pendingAction = null;
            awaitingConfirmation = false;
            UpdateLayout(true);
            if (diagnostics.Enabled)
            {
                var selected = GetSelectedTermDeposit();
                diagnostics.Log("atm-ui: term-selection-changed, playerId=" + farmer.UniqueMultiplayerID + ", displayIndex=" + termDepositIndex + ", depositId=" + (selected == null ? "none" : selected.Id));
            }
        }

        private void BeginPrimaryAction()
        {
            if (tab == 0) return;
            var action = GetPrimaryAction();
            BeginAction(action, IsTermCreation(action) || action == "loan");
        }

        private void BeginSecondaryAction()
        {
            var action = GetSecondaryAction();
            BeginAction(action, action == "term-early" || action == "repay");
        }

        private string GetPrimaryAction()
        {
            return tab == 1 ? "deposit" : tab == 2 ? (termFromActive ? "term-active" : "term") : "loan";
        }

        private static bool IsTermCreation(string action)
        {
            return action == "term" || action == "term-active";
        }

        private long GetTermSourceBalance(BankSnapshot snapshot)
        {
            return termFromActive ? snapshot.ActiveBalance : snapshot.WalletBalance;
        }

        private void ChangeTermSource(bool fromActive)
        {
            termFromActive = fromActive;
            typingAmount = false;
            UpdateTermAmountFeedback();
            UpdateLayout(true);
            if (diagnostics.Enabled)
                diagnostics.Log("atm-ui: term-source-changed, playerId=" + farmer.UniqueMultiplayerID + ", source=" + (fromActive ? "active" : "wallet") + ", amount=" + amount);
        }

        private void UpdateTermAmountFeedback()
        {
            if (tab != 2) return;
            statusMessage = amount > GetTermSourceBalance(GetCachedSnapshot())
                ? i18n.Get(termFromActive ? "error.active-insufficient" : "error.wallet-insufficient") : null;
        }

        private string GetSecondaryAction()
        {
            return tab == 1 ? "withdraw" : tab == 2 ? "term-early" : "repay";
        }

        private void CancelConfirmation()
        {
            if (diagnostics.Enabled)
                diagnostics.Log("atm-ui: confirmation-cancelled, playerId=" + farmer.UniqueMultiplayerID + ", action=" + (pendingAction ?? "none") + ", amount=" + amount);
            awaitingConfirmation = false;
            pendingAction = null;
            statusMessage = null;
            UpdateLayout(true);
        }

        private bool RequiresAmountInput()
        {
            return tab != 0;
        }

        private int[] GetProductIndices()
        {
            GetCachedConfig();
            if (tab == 2) return cachedTermProductIndices;
            if (tab == 3) return cachedLoanProductIndices;
            return Array.Empty<int>();
        }

        private int GetSelectedProductIndex()
        {
            var choices = GetProductIndices();
            if (choices.Length == 0) return 0;
            productIndex = Math.Max(0, Math.Min(productIndex, choices.Length - 1));
            return choices[productIndex];
        }

        private string GetProductDescription()
        {
            var choices = GetProductIndices();
            if (choices.Length == 0) return null;
            productIndex = Math.Max(0, Math.Min(productIndex, choices.Length - 1));
            var config = GetCachedConfig();
            if (tab == 2)
            {
                var product = config.TermProducts[choices[productIndex]];
                return i18n.Get("menu.product.term", new { index = productIndex + 1, count = choices.Length, days = product.Days, rate = product.DailyRatePercent });
            }
            var loan = config.LoanProducts[choices[productIndex]];
            return i18n.Get("menu.product.loan", new { index = productIndex + 1, count = choices.Length, days = loan.Days, rate = loan.DailyRatePercent });
        }

        private string GetConfirmationText()
        {
            if (IsTermCreation(pendingAction))
            {
                var index = GetSelectedProductIndex();
                var config = GetCachedConfig();
                if (index >= 0 && index < config.TermProducts.Count)
                {
                    var product = config.TermProducts[index];
                    return i18n.Get("menu.confirm.term", new
                    {
                        source = i18n.Get(pendingAction == "term-active" ? "menu.term.source.active" : "menu.term.source.wallet"),
                        amount = FormatGold(amount), days = product.Days, rate = product.DailyRatePercent
                    });
                }
            }
            if (pendingAction == "term-early")
            {
                var deposit = GetSelectedTermDeposit();
                if (deposit != null)
                {
                    try
                    {
                        var currentDay = (int)Game1.stats.DaysPlayed;
                        var completedDays = Math.Max(0, Math.Min(deposit.DaysSettled, currentDay - deposit.OpenDay));
                        var returned = BankMath.Compound(deposit.Principal, deposit.LockedActivityRatePercent, completedDays);
                        var forfeited = Math.Max(0, deposit.PayableBalance - returned);
                        return i18n.Get("menu.confirm.term-early", new { principal = deposit.Principal, days = completedDays, forfeited, returned });
                    }
                    catch (OverflowException) { }
                }
            }
            if (pendingAction == "repay")
            {
                var snapshot = GetCachedSnapshot();
                var totalOwed = SaturatingAdd(snapshot.LoanPrincipal, snapshot.LoanInterest);
                return i18n.Get("menu.confirm.repay", new { payment = FormatGold(Math.Min(amount, totalOwed)) });
            }
            if (pendingAction == "loan")
            {
                var config = GetCachedConfig();
                var choices = GetProductIndices();
                if (choices.Length > 0)
                {
                    var product = config.LoanProducts[choices[Math.Max(0, Math.Min(productIndex, choices.Length - 1))]];
                    return i18n.Get("menu.confirm.loan", new
                    {
                        amount = FormatGold(amount),
                        days = product.Days,
                        rate = product.DailyRatePercent,
                        collection = i18n.Get(config.EnableOverdueCollection ? "menu.confirm.loan.collection" : "menu.confirm.loan.manual",
                            new { grace = config.OverdueGraceDays })
                    });
                }
            }
            return i18n.Get("menu.confirm.amount", new { action = i18n.Get("action." + pendingAction), amount });
        }

        private string[] GetLoanTermsPages()
        {
            var config = GetCachedConfig();
            var text = i18n.Get("menu.loan.terms.details", new
            {
                collection = i18n.Get(config.EnableOverdueCollection ? "menu.value.enabled" : "menu.value.disabled"),
                grace = config.OverdueGraceDays,
                activeSetoff = i18n.Get(config.EnableActiveBalanceSetoff ? "menu.value.enabled" : "menu.value.disabled"),
                activeReserve = FormatGold(config.MinimumActiveBalanceReserve),
                walletReserve = FormatGold(config.MinimumWalletReserve),
                garnishment = config.IncomeGarnishmentPercent,
                multiplier = config.OverdueRateMultiplier,
                cap = config.MaximumOverdueInterestPercent == 0
                    ? i18n.Get("menu.loan.terms.uncapped")
                    : i18n.Get("menu.loan.terms.cap", new { cap = config.MaximumOverdueInterestPercent }),
                defaultDays = config.DefaultAfterDays
            });
            var lines = Game1.parseText(text, Game1.smallFont, width - 56).Split('\n');
            var pages = new List<string>();
            var page = "";
            var availableHeight = height - 390;
            foreach (var line in lines)
            {
                var candidate = page.Length == 0 ? line : page + "\n" + line;
                if (page.Length > 0 && Game1.smallFont.MeasureString(candidate).Y > availableHeight)
                {
                    pages.Add(page);
                    page = line;
                }
                else page = candidate;
            }
            pages.Add(page);
            return pages.ToArray();
        }

        private void ChangeLoanTermsPage(int direction)
        {
            loanTermsPage = Math.Max(0, Math.Min(GetLoanTermsPages().Length - 1, loanTermsPage + direction));
            UpdateLayout(true);
            if (diagnostics.Enabled)
                diagnostics.Log("atm-ui: loan-terms-page, playerId=" + farmer.UniqueMultiplayerID + ", page=" + loanTermsPage);
        }

        private bool IsActionAllowedDuringCollection(string action)
        {
            var loan = bank.GetAccount(farmer.UniqueMultiplayerID).Loan;
            var collectionLocked = loan != null && (loan.Status == LoanStatus.Collection || loan.Status == LoanStatus.Defaulted);
            if (!collectionLocked) return true;
            return action == "deposit" || action == "repay" || action == "term-early";
        }

        private string GetSuccessMessage(string action, string resultMessage)
        {
            if (resultMessage == "deposit-applied-to-overdue-loan") return i18n.Get("message.deposit.collection-success");
            if (resultMessage == "term-closed-early-to-overdue-loan") return i18n.Get("message.term-early.collection-success");
            return i18n.Get("message." + action + ".success");
        }

        private TermDeposit GetSelectedTermDeposit()
        {
            var deposits = GetTermDeposits();
            if (deposits.Length == 0) return null;
            termDepositIndex = Math.Max(0, Math.Min(termDepositIndex, deposits.Length - 1));
            return deposits[termDepositIndex];
        }

        private BankConfig GetCachedConfig()
        {
            var revision = bank.ConfigRevision;
            if (cachedConfig != null && cachedConfigRevision == revision) return cachedConfig;
            cachedConfig = bank.Config;
            cachedConfigRevision = revision;
            cachedTermProductIndices = cachedConfig.TermProducts.Take(10).Select((product, index) => new { product, index }).Where(x => x.product.Enabled).OrderBy(x => x.product.Days).Select(x => x.index).ToArray();
            cachedLoanProductIndices = cachedConfig.LoanProducts.Take(10).Select((product, index) => new { product, index }).Where(x => x.product.Enabled).OrderBy(x => x.product.Days).Select(x => x.index).ToArray();
            return cachedConfig;
        }

        private BankSnapshot GetCachedSnapshot()
        {
            var revision = bank.State.Revision;
            var configRevision = bank.ConfigRevision;
            var walletBalance = wallet.Balance;
            var earnings = wallet.HistoricalTotalEarnings;
            var cacheEmpty = cachedSnapshot == null;
            var bankRevisionChanged = cachedSnapshotRevision != revision;
            var configRevisionChanged = cachedSnapshotConfigRevision != configRevision;
            var walletChanged = cachedWalletBalance != walletBalance;
            var earningsChanged = cachedHistoricalEarnings != earnings;
            if (cacheEmpty || bankRevisionChanged || configRevisionChanged || walletChanged || earningsChanged)
            {
                if (diagnostics.Enabled)
                    diagnostics.Log("atm-ui: snapshot-refresh, playerId=" + farmer.UniqueMultiplayerID
                        + ", reason=" + (pendingSnapshotReason ?? (walletChanged ? "wallet-field-changed" : earningsChanged ? "earnings-field-changed" : configRevisionChanged ? "config-changed" : "bank-revision-changed"))
                        + ", bankRevisionChanged=" + bankRevisionChanged
                        + ", configRevisionChanged=" + configRevisionChanged
                        + ", walletChanged=" + walletChanged
                        + ", earningsChanged=" + earningsChanged
                        + ", revision=" + revision
                        + ", configRevision=" + configRevision);
                cachedSnapshot = bank.Snapshot(farmer.UniqueMultiplayerID, wallet);
                cachedSnapshotRevision = revision;
                cachedSnapshotConfigRevision = configRevision;
                cachedWalletBalance = walletBalance;
                cachedHistoricalEarnings = earnings;
                pendingSnapshotReason = null;
            }
            return cachedSnapshot;
        }

        private TermDeposit[] GetTermDeposits()
        {
            var revision = bank.State.Revision;
            if (cachedTermDepositRevision != revision)
            {
                cachedTermDeposits = bank.GetAccount(farmer.UniqueMultiplayerID).TermDeposits
                    .OrderBy(p => p.MaturityDay)
                    .ThenBy(p => p.Id, StringComparer.Ordinal)
                    .ToArray();
                cachedTermDepositRevision = revision;
            }
            return cachedTermDeposits;
        }

        private string GetKeypadLabel(string key)
        {
            if (key == "max") return i18n.Get("menu.keypad.max");
            if (key == "clear") return i18n.Get("menu.keypad.clear");
            if (key == "back") return "⌫";
            return key;
        }

        private void AddButton(int id, Rectangle bounds, string label, IconKind icon, bool selected = false, bool emphasized = false, bool enabled = true)
        {
            var component = new ClickableComponent(bounds, label) { myID = id };
            buttons.Add(new MenuButton(id, component, label, icon, selected, emphasized, enabled && (id == CloseId || string.IsNullOrEmpty(remoteAction))));
        }

        private MenuButton FindButton(int id)
        {
            return buttons.FirstOrDefault(p => p.Id == id);
        }

        private static Point Center(Rectangle rectangle)
        {
            return new Point(rectangle.Center.X, rectangle.Center.Y);
        }

        private static string FormatGold(long value)
        {
            return value.ToString("N0", CultureInfo.CurrentCulture) + "g";
        }

        private string FormatMaturity(int maturityDay, int currentDay)
        {
            var remaining = maturityDay - currentDay;
            if (remaining == 0) return i18n.Get("menu.maturity.today");
            return remaining > 0
                ? i18n.Get("menu.maturity.days", new { days = remaining })
                : i18n.Get("menu.maturity.overdue", new { days = -remaining });
        }

        private sealed class MenuButton
        {
            public int Id { get; }
            public ClickableComponent Component { get; }
            public string Label { get; }
            public IconKind Icon { get; }
            public bool Selected { get; }
            public bool Emphasized { get; }
            public bool Enabled { get; }

            public MenuButton(int id, ClickableComponent component, string label, IconKind icon, bool selected, bool emphasized, bool enabled)
            {
                Id = id;
                Component = component;
                Label = label;
                Icon = icon;
                Selected = selected;
                Emphasized = emphasized;
                Enabled = enabled;
            }
        }

        private enum IconKind
        {
            None,
            Overview,
            Wallet,
            Term,
            Loan,
            ArrowLeft,
            ArrowRight,
            Confirm,
            Secondary,
            Close
        }
    }

    internal sealed class GameWalletAdapter : IWalletAdapter
    {
        private readonly Farmer farmer;
        private readonly WalletMode mode;
        private readonly ModDiagnostics diagnostics;
        public GameWalletAdapter(Farmer farmer, WalletMode mode, ModDiagnostics diagnostics)
        {
            this.farmer = farmer;
            this.mode = mode;
            this.diagnostics = diagnostics;
        }
        public long Balance { get { return farmer.team.GetIndividualMoney(farmer); } }
        public long HistoricalTotalEarnings { get { return mode == WalletMode.Shared ? (long)farmer.team.totalMoneyEarned.Value : (long)farmer.totalMoneyEarned; } }
        public bool TryRemove(long amount)
        {
            var current = Balance;
            if (amount < 0 || amount > current)
            {
                if (diagnostics.Enabled)
                    diagnostics.Log("wallet: remove-rejected, playerId=" + farmer.UniqueMultiplayerID + ", mode=" + mode + ", amount=" + amount + ", balance=" + current);
                return false;
            }
            var success = TrySetBalance(current - amount);
            if (diagnostics.Enabled)
                diagnostics.Log("wallet: remove-result, playerId=" + farmer.UniqueMultiplayerID + ", mode=" + mode + ", amount=" + amount + ", before=" + current + ", after=" + Balance + ", success=" + success);
            return success;
        }

        public bool TryAdd(long amount)
        {
            var current = Balance;
            if (amount < 0 || amount > int.MaxValue || current > int.MaxValue - amount)
            {
                if (diagnostics.Enabled)
                    diagnostics.Log("wallet: add-rejected, playerId=" + farmer.UniqueMultiplayerID + ", mode=" + mode + ", amount=" + amount + ", balance=" + current);
                return false;
            }
            var success = TrySetBalance(current + amount);
            if (diagnostics.Enabled)
                diagnostics.Log("wallet: add-result, playerId=" + farmer.UniqueMultiplayerID + ", mode=" + mode + ", amount=" + amount + ", before=" + current + ", after=" + Balance + ", success=" + success);
            return success;
        }

        public void RestoreBalance(long balance)
        {
            // BankService calls this from its exception path. The captured
            // balance came from the game's int wallet, so an invalid value is
            // never expected here. Do not throw on that path: a second wallet
            // exception would mask the original bank rejection and could leave
            // the caller with no usable transaction result.
            if (balance < 0 || balance > int.MaxValue) return;
            var restored = TrySetBalance(balance);
            if (diagnostics.Enabled)
                diagnostics.Log("wallet: rollback-result, playerId=" + farmer.UniqueMultiplayerID + ", mode=" + mode + ", target=" + balance + ", actual=" + Balance + ", success=" + restored);
        }

        private bool TrySetBalance(long balance)
        {
            if (balance < 0 || balance > int.MaxValue) return false;

            var team = farmer.team;
            var previousBalance = team.GetIndividualMoney(farmer);
            if (previousBalance == balance) return true;

            var personalTotal = farmer.totalMoneyEarned;
            var sharedTotal = team.totalMoneyEarned.Value;
            try
            {
                SetWalletValue(team, (int)balance, previousBalance);
                if (team.GetIndividualMoney(farmer) == (int)balance
                    && farmer.totalMoneyEarned == personalTotal
                    && team.totalMoneyEarned.Value == sharedTotal)
                    return true;
            }

            catch (Exception ex)
            {
                // The game wallet setter is expected to be infallible for a
                // legal int value, but a failed NetField write must not escape
                // into BankService's rollback path as a second exception.
                if (diagnostics.Enabled)
                    diagnostics.Log("wallet: write-exception, playerId=" + farmer.UniqueMultiplayerID + ", mode=" + mode + ", target=" + balance + ", exception=" + ex.GetType().Name + ": " + ex.Message);
            }

            TryRestoreWalletValue(team, (int)previousBalance);
            TryRestoreHistoricalTotals(personalTotal, sharedTotal);
            if (diagnostics.Enabled)
                diagnostics.Log("wallet: write-rolled-back, playerId=" + farmer.UniqueMultiplayerID + ", mode=" + mode + ", target=" + balance + ", restoredBalance=" + team.GetIndividualMoney(farmer) + ", personalEarningsPreserved=" + (farmer.totalMoneyEarned == personalTotal) + ", sharedEarningsPreserved=" + (team.totalMoneyEarned.Value == sharedTotal));
            return false;
        }

        private void SetWalletValue(FarmerTeam team, int balance, long previousBalance)
        {
            var delta = checked(balance - (int)previousBalance);
            // addUnearnedMoney is the local shared-wallet path: it changes only
            // the synchronized wallet value and intentionally bypasses the
            // vanilla income/achievement counters. In personal-wallet mode,
            // use SetIndividualMoney even for the host so the independent
            // NetIntDelta is selected explicitly and the same path is used by
            // a farmhand and by rollback.
            if (mode == WalletMode.Shared && ReferenceEquals(farmer, Game1.player))
                farmer.addUnearnedMoney(delta);
            else
                team.SetIndividualMoney(farmer, balance);
        }

        private void TryRestoreWalletValue(FarmerTeam team, int balance)
        {
            try
            {
                // Always restore through the explicit team API. This avoids
                // re-entering addUnearnedMoney during rollback and therefore
                // cannot increment any vanilla income counter.
                team.SetIndividualMoney(farmer, balance);
            }
            catch
            {
                // BankService must still receive its rejected result. The
                // normal SMAPI/Netcode path does not throw for a legal value.
            }
        }

        private void TryRestoreHistoricalTotals(uint personalTotal, int sharedTotal)
        {
            try { farmer.totalMoneyEarned = personalTotal; } catch { }
            try { farmer.team.totalMoneyEarned.Value = sharedTotal; } catch { }
        }
    }
}
