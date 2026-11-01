using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewValley;

namespace StardewModdingAPI
{
    public enum LogLevel { Trace, Debug, Info, Warn, Error, Alert }
    public enum AssetLoadPriority { Low, Medium, High }

    public struct SButton
    {
        private readonly string name;
        private readonly bool isAction;
        public SButton(string name, bool isAction = false) { this.name = name; this.isAction = isAction; }
        public static SButton MouseRight { get { return new SButton("mouse-right"); } }
        public static SButton MouseLeft { get { return new SButton("mouse-left"); } }
        public static SButton Tab { get { return new SButton("tab"); } }
        public static SButton Escape { get { return new SButton("escape"); } }
        public static SButton E { get { return new SButton("e"); } }
        public static SButton LeftShoulder { get { return new SButton("left-shoulder"); } }
        public static SButton RightShoulder { get { return new SButton("right-shoulder"); } }
        public static SButton ControllerA { get { return new SButton("controller-a", true); } }
        public static SButton ControllerB { get { return new SButton("controller-b"); } }
        public static SButton DPadUp { get { return new SButton("dpad-up"); } }
        public static SButton DPadDown { get { return new SButton("dpad-down"); } }
        public static SButton DPadLeft { get { return new SButton("dpad-left"); } }
        public static SButton DPadRight { get { return new SButton("dpad-right"); } }
        public static SButton LeftThumbstickUp { get { return new SButton("left-thumbstick-up"); } }
        public static SButton LeftThumbstickDown { get { return new SButton("left-thumbstick-down"); } }
        public static SButton LeftThumbstickLeft { get { return new SButton("left-thumbstick-left"); } }
        public static SButton LeftThumbstickRight { get { return new SButton("left-thumbstick-right"); } }
        public bool IsActionButton() { return isAction; }
        public override string ToString() { return name ?? ""; }
        public static bool operator ==(SButton a, SButton b) { return a.name == b.name; }
        public static bool operator !=(SButton a, SButton b) { return !(a == b); }
        public override bool Equals(object obj) { return obj is SButton b && b == this; }
        public override int GetHashCode() { return name == null ? 0 : name.GetHashCode(); }
    }

    public static class SButtonExtensions
    {
        public static bool IsActionButton(this SButton button) { return button.IsActionButton(); }
    }

    public interface IManifest
    {
        string UniqueID { get; }
    }

    public interface IMonitor
    {
        void Log(string message, LogLevel level = LogLevel.Trace);
    }

    public interface ITranslationHelper
    {
        string Get(string key);
        string Get(string key, object tokens);
    }

    public interface IMultiplayerPeer
    {
        long PlayerID { get; }
    }

    public interface IMultiplayerHelper
    {
        void SendMessage<TMessage>(TMessage message, string messageType, string[] modIDs, long[] playerIDs = null);
        IMultiplayerPeer GetConnectedPlayer(long id);
        IEnumerable<IMultiplayerPeer> GetConnectedPlayers();
        long GetNewID();
    }

    public interface IModRegistry
    {
        TApi GetApi<TApi>(string uniqueID) where TApi : class;
    }

    public interface IGameContentHelper
    {
        void InvalidateCache(string assetName);
    }

    public interface IDataHelper
    {
        T ReadSaveData<T>(string key);
        void WriteSaveData<T>(string key, T value);
        T ReadJsonFile<T>(string path) where T : class;
        void WriteJsonFile<T>(string path, T value) where T : class;
    }

    public interface IInputHelper
    {
        void Suppress(SButton button);
        void SuppressActiveKeybinds(Utilities.KeybindList keybinds);
    }

    public interface IConsoleCommandHelper
    {
        void Add(string name, string documentation, Action<string, string[]> callback);
    }

    public interface IModHelper
    {
        Events.IModEvents Events { get; }
        IInputHelper Input { get; }
        IMultiplayerHelper Multiplayer { get; }
        IDataHelper Data { get; }
        IGameContentHelper GameContent { get; }
        IModRegistry ModRegistry { get; }
        ITranslationHelper Translation { get; }
        IConsoleCommandHelper ConsoleCommands { get; }
        T ReadConfig<T>() where T : class, new();
        void WriteConfig<T>(T config) where T : class, new();
    }

    public static class Context
    {
        public static bool IsMainPlayer { get; set; } = true;
        public static bool IsMultiplayer { get; set; }
        public static bool IsWorldReady { get; set; }
        public static bool IsOnHostComputer { get; set; }
        public static int ScreenId { get; set; }
    }

    public abstract class Mod
    {
        public IModHelper Helper { get; protected set; }
        public IMonitor Monitor { get; protected set; }
        public IManifest ModManifest { get; protected set; }
        // Supplies the host-owned properties normally initialized by SMAPI.
        public void InitializeForTest(IModHelper helper, IMonitor monitor)
        {
            Helper = helper;
            Monitor = monitor;
            Entry(helper);
        }
        public abstract void Entry(IModHelper helper);
    }
}

namespace StardewModdingAPI.Events
{
    using StardewModdingAPI;
    using StardewValley;

    public interface IModEvents
    {
        IGameLoopEvents GameLoop { get; }
        IInputEvents Input { get; }
        IMultiplayerEvents Multiplayer { get; }
        IContentEvents Content { get; }
        IPlayerEvents Player { get; }
        IDisplayEvents Display { get; }
    }

    public interface IGameLoopEvents
    {
        event EventHandler<SaveLoadedEventArgs> SaveLoaded;
        event EventHandler<DayStartedEventArgs> DayStarted;
        event EventHandler<SavingEventArgs> Saving;
        event EventHandler<ReturnedToTitleEventArgs> ReturnedToTitle;
        event EventHandler<UpdateTickedEventArgs> UpdateTicked;
        event EventHandler<GameLaunchedEventArgs> GameLaunched;
    }

    public interface IInputEvents
    {
        event EventHandler<ButtonPressedEventArgs> ButtonPressed;
        event EventHandler<ButtonsChangedEventArgs> ButtonsChanged;
    }

    public interface IMultiplayerEvents
    {
        event EventHandler<ModMessageReceivedEventArgs> ModMessageReceived;
        event EventHandler<PeerConnectedEventArgs> PeerConnected;
        event EventHandler<PeerDisconnectedEventArgs> PeerDisconnected;
    }

    public interface IContentEvents
    {
        event EventHandler<AssetRequestedEventArgs> AssetRequested;
        event EventHandler<AssetsInvalidatedEventArgs> AssetsInvalidated;
    }

    public interface IPlayerEvents
    {
        event EventHandler<WarpedEventArgs> Warped;
    }

    public interface IDisplayEvents
    {
        event EventHandler<MenuChangedEventArgs> MenuChanged;
    }

    public class SaveLoadedEventArgs : EventArgs { }
    public class DayStartedEventArgs : EventArgs { }
    public class SavingEventArgs : EventArgs { }
    public class ReturnedToTitleEventArgs : EventArgs { }
    public class UpdateTickedEventArgs : EventArgs { }
    public class GameLaunchedEventArgs : EventArgs { }
    public class ButtonsChangedEventArgs : EventArgs { }
    public class MenuChangedEventArgs : EventArgs { }

    public interface ICursorPosition
    {
        Microsoft.Xna.Framework.Vector2 GrabTile { get; }
    }

    public class ButtonPressedEventArgs : EventArgs
    {
        public SButton Button { get; set; }
        public ICursorPosition Cursor { get; set; }
    }

    public class WarpedEventArgs : EventArgs
    {
        public bool IsLocalPlayer { get; set; }
        public GameLocation OldLocation { get; set; }
        public GameLocation NewLocation { get; set; }
    }

    public interface IAssetName
    {
        bool IsEquivalentTo(string name);
    }

    public class AssetRequestedEventArgs : EventArgs
    {
        public IAssetName NameWithoutLocale { get; set; }
        public void LoadFromModFile<T>(string path, object priority) { }
    }

    public class AssetsInvalidatedEventArgs : EventArgs
    {
        public IEnumerable<IAssetName> NamesWithoutLocale { get; set; }
    }

    public class ModMessageReceivedEventArgs : EventArgs
    {
        public string FromModID { get; set; }
        public string Type { get; set; }
        public long FromPlayerID { get; set; }
        public object Payload { get; set; }
        public T ReadAs<T>() { return Payload == null ? default(T) : (T)Payload; }
    }

    public class PeerConnectedEventArgs : EventArgs
    {
        public IMultiplayerPeer Peer { get; set; }
    }

    public class PeerDisconnectedEventArgs : EventArgs
    {
        public IMultiplayerPeer Peer { get; set; }
    }
}

namespace StardewModdingAPI.Utilities
{
    public sealed class PerScreen<T>
    {
        private readonly Dictionary<int, T> values = new Dictionary<int, T>();
        private readonly Func<T> createNewState;

        public PerScreen() : this(() => default(T)) { }
        public PerScreen(Func<T> createNewState) { this.createNewState = createNewState ?? (() => default(T)); }

        public T Value
        {
            get
            {
                var screenId = StardewModdingAPI.Context.ScreenId;
                T value;
                if (!values.TryGetValue(screenId, out value))
                {
                    value = createNewState();
                    values[screenId] = value;
                }
                return value;
            }
            set { values[StardewModdingAPI.Context.ScreenId] = value; }
        }
    }

    public class Keybind
    {
        public SButton[] Buttons { get; set; }
        public Keybind(SButton button) { Buttons = new[] { button }; }
        public Keybind(SButton[] buttons) { Buttons = buttons; }
    }

    public class KeybindList
    {
        public Keybind[] Keybinds { get; set; } = Array.Empty<Keybind>();
        public KeybindList(SButton button) { Keybinds = new[] { new Keybind(button) }; }
        public KeybindList(Keybind first, Keybind second) { Keybinds = new[] { first, second }; }
        public KeybindList(Keybind[] keybinds) { Keybinds = keybinds; }
        public bool JustPressed() { return false; }
    }
}
