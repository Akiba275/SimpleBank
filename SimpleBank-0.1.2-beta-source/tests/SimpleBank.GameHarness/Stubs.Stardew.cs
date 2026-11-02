using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;

namespace Netcode
{
    public class NetBool
    {
        private bool value;
        public bool Value
        {
            get { return value; }
            set
            {
                if (this.value == value) return;
                var oldValue = this.value;
                this.value = value;
                fieldChangeEvent?.Invoke(this, oldValue, value);
            }
        }
        public event Action<NetBool, bool, bool> fieldChangeEvent;
    }

    public class NetInt
    {
        public int Value { get; set; }
    }
}

namespace StardewValley
{
    public class SpriteFont
    {
        public Vector2 MeasureString(string text)
        {
            var lines = text.Split('\n');
            return new Vector2(lines.Max(line => line.Length) * 8, lines.Length * 16);
        }
    }

    public class Options
    {
        public bool SnappyMenus { get; set; }
    }

    public class HUDMessage
    {
        public static int error_type = 3;
        public static int newQuest_type = 2;
        public string Message { get; }
        public int Type { get; }
        public HUDMessage(string message, int type) { Message = message; Type = type; }
    }

    public class TemporaryAnimatedSprite
    {
        public TemporaryAnimatedSprite(string textureName, Rectangle sourceRect, float animationInterval, int animationFrames, int loopCount, Vector2 position, bool flicker, bool flipped, float layerDepth, float alphaFade, Color color, float scale, float scaleChange, float rotation, float rotationChange, bool local) { }
        public int id;
    }

    public class GameMap
    {
        public Dictionary<string, object> Properties { get; } = new Dictionary<string, object>();
    }

    public class GameLocation
    {
        public string NameOrUniqueName { get; set; }
        public GameMap Map { get; set; }
        public List<TemporaryAnimatedSprite> temporarySprites { get; } = new List<TemporaryAnimatedSprite>();
        public void removeTemporarySpritesWithIDLocal(int id) { }
    }

    public class FarmerTeam
    {
        public Netcode.NetBool useSeparateWallets { get; } = new Netcode.NetBool();
        public Netcode.NetInt totalMoneyEarned { get; } = new Netcode.NetInt();
        private readonly Dictionary<long, int> individualMoney = new Dictionary<long, int>();
        public int SharedMoney { get; private set; }
        public int GetIndividualMoney(Farmer who)
        {
            if (!useSeparateWallets.Value) return SharedMoney;
            int value;
            return who != null && individualMoney.TryGetValue(who.UniqueMultiplayerID, out value) ? value : 0;
        }
        public void SetIndividualMoney(Farmer who, int value)
        {
            if (who == null) return;
            if (useSeparateWallets.Value) individualMoney[who.UniqueMultiplayerID] = value;
            else SharedMoney = value;
        }
    }

    public class Farmer
    {
        public long UniqueMultiplayerID { get; set; }
        public string Name { get; set; }
        public FarmerTeam team { get; set; } = new FarmerTeam();
        public GameLocation currentLocation { get; set; }
        public Point TilePoint { get; set; }
        public int FacingDirection { get; set; }
        public Netcode.NetBool changeWalletTypeTonight { get; } = new Netcode.NetBool();
        public uint totalMoneyEarned { get; set; }
        public bool isMoving() { return false; }
        public void addUnearnedMoney(int amount) { team.SetIndividualMoney(this, checked(team.GetIndividualMoney(this) + amount)); }
    }

    public class Stats
    {
        public int DaysPlayed { get; set; }
    }

    public static class Game1
    {
        public static Farmer player;
        public static Farmer MasterPlayer;
        public static Stats stats = new Stats();
        public static GameLocation currentLocation;
        public static StardewValley.Menus.IClickableMenu activeClickableMenu;
        public static bool eventUp;
        public const int up = 0;
        public const int down = 2;
        public const int left = 3;
        public const int right = 1;
        public const int tileSize = 64;
        public static Texture2D menuTexture = new Texture2D();
        public static Texture2D staminaRect = new Texture2D();
        public static SpriteFont smallFont = new SpriteFont();
        public static SpriteFont dialogueFont = new SpriteFont();
        public static Color textColor = Color.White;
        public static Rectangle uiViewport = new Rectangle(0, 0, 1920, 1080);
        public static Options options = new Options();
        public static List<Farmer> OnlineFarmers { get; } = new List<Farmer>();
        public static IEnumerable<Farmer> getOnlineFarmers()
        {
            return OnlineFarmers.Count == 0 && player != null ? new List<Farmer> { player } : OnlineFarmers;
        }
        public static GameLocation getLocationFromName(string name) { return currentLocation; }
        public static void drawObjectDialogue(string message) { }
        public static List<HUDMessage> HudMessages { get; } = new List<HUDMessage>();
        public static void addHUDMessage(HUDMessage message) { HudMessages.Add(message); }
        public static void playSound(string cueName) { }
        public static string parseText(string text, SpriteFont font, int width) { return text; }
    }

    public static class Utility
    {
        public sealed class DrawnText
        {
            public string Text;
            public Vector2 Position;
        }
        public static List<DrawnText> DrawnTexts { get; } = new List<DrawnText>();
        public static void drawTextWithShadow(SpriteBatch b, string text, SpriteFont font, Vector2 position, Color color, float scale = 1f)
        {
            DrawnTexts.Add(new DrawnText { Text = text, Position = position });
        }
        public static void drawBoldText(SpriteBatch b, string text, SpriteFont font, Vector2 position, Color color) { }
        public static Vector2 getTopLeftPositionForCenteringOnScreen(int width, int height) { return Vector2.Zero; }
    }
}

namespace StardewValley.Menus
{
    public class ClickableComponent
    {
        public int myID;
        public Rectangle bounds;
        public string name;
        public ClickableComponent(Rectangle bounds, string name) { this.bounds = bounds; this.name = name; }
        public bool containsPoint(int x, int y) { return bounds.Contains(x, y); }
    }

    public class IClickableMenu
    {
        public int xPositionOnScreen;
        public int yPositionOnScreen;
        public int width;
        public int height;
        public ClickableComponent currentlySnappedComponent;

        protected IClickableMenu() { }
        protected IClickableMenu(int x, int y, int width, int height, bool showUpperRightCloseButton = false) { }
        public virtual void exitThisMenu() { cleanupBeforeExit(); Game1.activeClickableMenu = null; }
        protected virtual void cleanupBeforeExit() { }
        public virtual void receiveLeftClick(int x, int y, bool playSound = true) { }
        public virtual void receiveRightClick(int x, int y, bool playSound = true) { }
        public virtual void performHoverAction(int x, int y) { }
        public virtual void receiveScrollWheelAction(int direction) { }
        public virtual void receiveKeyPress(Microsoft.Xna.Framework.Input.Keys key) { }
        public virtual void receiveGamePadButton(Microsoft.Xna.Framework.Input.Buttons button) { }
        public virtual void update(GameTime gameTime) { }
        public virtual void draw(SpriteBatch b) { }

        protected static void drawTextureBox(SpriteBatch b, Texture2D texture, Rectangle sourceRect, int x, int y, int width, int height, Color color) { }
        protected void drawMouse(SpriteBatch b) { }
        protected void snapCursorToCurrentSnappedComponent() { }
    }

}
