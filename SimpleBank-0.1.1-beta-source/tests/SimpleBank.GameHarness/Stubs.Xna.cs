using System;

namespace Microsoft.Xna.Framework
{
    public class GameTime { }

    public struct Vector2
    {
        public float X;
        public float Y;
        public Vector2(float x, float y) { X = x; Y = y; }
        public static Vector2 Zero { get { return new Vector2(0, 0); } }
        public static float Distance(Vector2 a, Vector2 b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }

    public struct Point
    {
        public int X;
        public int Y;
        public Point(int x, int y) { X = x; Y = y; }
        public static bool operator ==(Point a, Point b) { return a.X == b.X && a.Y == b.Y; }
        public static bool operator !=(Point a, Point b) { return !(a == b); }
        public override bool Equals(object obj) { return obj is Point p && p == this; }
        public override int GetHashCode() { return (X * 397) ^ Y; }
    }

    public struct Rectangle
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public Rectangle(int x, int y, int width, int height) { X = x; Y = y; Width = width; Height = height; }
        public int Right { get { return X + Width; } }
        public int Bottom { get { return Y + Height; } }
        public Point Center { get { return new Point(X + Width / 2, Y + Height / 2); } }
        public bool Contains(int x, int y) { return x >= X && x < X + Width && y >= Y && y < Y + Height; }
        public bool Contains(Point p) { return Contains(p.X, p.Y); }
    }

    public struct Color
    {
        public byte R;
        public byte G;
        public byte B;
        public byte A;
        public Color(byte r, byte g, byte b, byte a) { R = r; G = g; B = b; A = a; }
        public Color(int r, int g, int b, int a) { R = (byte)r; G = (byte)g; B = (byte)b; A = (byte)a; }
        public Color(int r, int g, int b) { R = (byte)r; G = (byte)g; B = (byte)b; A = 255; }
        public static Color White { get { return new Color(255, 255, 255, 255); } }
        public static Color Black { get { return new Color(0, 0, 0, 255); } }
        public static Color DimGray { get { return new Color(105, 105, 105, 255); } }
        public static Color operator *(Color c, float f)
        {
            return new Color((int)(c.R * f), (int)(c.G * f), (int)(c.B * f), (int)(c.A * f));
        }
    }
}

namespace Microsoft.Xna.Framework.Graphics
{
    public class GraphicsDevice { }

    public class Texture2D { }

    public class SpriteBatch
    {
        public SpriteBatch(GraphicsDevice graphicsDevice) { }
        public void Begin() { }
        public void End() { }
        public void Draw(Texture2D texture, Microsoft.Xna.Framework.Rectangle destinationRectangle, Microsoft.Xna.Framework.Color color) { }
        public void Draw(Texture2D texture, Microsoft.Xna.Framework.Rectangle destinationRectangle, Microsoft.Xna.Framework.Rectangle? sourceRectangle, Microsoft.Xna.Framework.Color color) { }
    }
}

namespace Microsoft.Xna.Framework.Input
{
    public enum Keys
    {
        None = 0, D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
        NumPad0, NumPad1, NumPad2, NumPad3, NumPad4, NumPad5, NumPad6, NumPad7, NumPad8, NumPad9,
        Back, Delete, Tab, Enter, Space, Escape
    }

    public enum Buttons
    {
        None = 0, A, B, X, Y, DPadUp, DPadDown, DPadLeft, DPadRight,
        LeftShoulder, RightShoulder, LeftTrigger, RightTrigger, Start, Back, LeftStick, RightStick
    }
}
