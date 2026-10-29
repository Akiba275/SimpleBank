using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace SimpleBank
{
    internal sealed class InputConfig
    {
        public KeybindList KeyboardNextPage { get; set; } = Bind(SButton.Tab);
        public KeybindList KeyboardClose { get; set; } = BindEither(SButton.Escape, SButton.E);
        public KeybindList ControllerPreviousPage { get; set; } = Bind(SButton.LeftShoulder);
        public KeybindList ControllerNextPage { get; set; } = Bind(SButton.RightShoulder);
        public KeybindList ControllerConfirm { get; set; } = Bind(SButton.ControllerA);
        public KeybindList ControllerBack { get; set; } = Bind(SButton.ControllerB);
        public KeybindList ControllerUp { get; set; } = BindEither(SButton.DPadUp, SButton.LeftThumbstickUp);
        public KeybindList ControllerDown { get; set; } = BindEither(SButton.DPadDown, SButton.LeftThumbstickDown);
        public KeybindList ControllerLeft { get; set; } = BindEither(SButton.DPadLeft, SButton.LeftThumbstickLeft);
        public KeybindList ControllerRight { get; set; } = BindEither(SButton.DPadRight, SButton.LeftThumbstickRight);

        public void RestoreMissingDefaults()
        {
            KeyboardNextPage = KeyboardNextPage ?? Bind(SButton.Tab);
            KeyboardClose = KeyboardClose ?? BindEither(SButton.Escape, SButton.E);
            ControllerPreviousPage = ControllerPreviousPage ?? Bind(SButton.LeftShoulder);
            ControllerNextPage = ControllerNextPage ?? Bind(SButton.RightShoulder);
            ControllerConfirm = ControllerConfirm ?? Bind(SButton.ControllerA);
            ControllerBack = ControllerBack ?? Bind(SButton.ControllerB);
            ControllerUp = ControllerUp ?? BindEither(SButton.DPadUp, SButton.LeftThumbstickUp);
            ControllerDown = ControllerDown ?? BindEither(SButton.DPadDown, SButton.LeftThumbstickDown);
            ControllerLeft = ControllerLeft ?? BindEither(SButton.DPadLeft, SButton.LeftThumbstickLeft);
            ControllerRight = ControllerRight ?? BindEither(SButton.DPadRight, SButton.LeftThumbstickRight);
        }

        public InputConfig Clone()
        {
            RestoreMissingDefaults();
            return new InputConfig
            {
                KeyboardNextPage = CloneBinding(KeyboardNextPage),
                KeyboardClose = CloneBinding(KeyboardClose),
                ControllerPreviousPage = CloneBinding(ControllerPreviousPage),
                ControllerNextPage = CloneBinding(ControllerNextPage),
                ControllerConfirm = CloneBinding(ControllerConfirm),
                ControllerBack = CloneBinding(ControllerBack),
                ControllerUp = CloneBinding(ControllerUp),
                ControllerDown = CloneBinding(ControllerDown),
                ControllerLeft = CloneBinding(ControllerLeft),
                ControllerRight = CloneBinding(ControllerRight)
            };
        }

        private static KeybindList Bind(SButton button)
        {
            return new KeybindList(button);
        }

        private static KeybindList BindEither(SButton first, SButton second)
        {
            return new KeybindList(new Keybind(first), new Keybind(second));
        }

        private static KeybindList CloneBinding(KeybindList source)
        {
            var keybinds = new Keybind[source.Keybinds.Length];
            for (var i = 0; i < source.Keybinds.Length; i++)
                keybinds[i] = new Keybind((SButton[])source.Keybinds[i].Buttons.Clone());
            return new KeybindList(keybinds);
        }
    }
}
