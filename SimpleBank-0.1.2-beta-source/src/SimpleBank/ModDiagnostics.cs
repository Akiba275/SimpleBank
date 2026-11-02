using System;
using StardewModdingAPI;

namespace SimpleBank
{
    /// <summary>Central low-overhead gate for optional diagnostic logging.</summary>
    internal sealed class ModDiagnostics
    {
        private readonly IMonitor monitor;
        private readonly Func<bool> isEnabled;

        public bool Enabled
        {
            get
            {
                try { return isEnabled != null && isEnabled(); }
                catch { return false; }
            }
        }

        public ModDiagnostics(IMonitor monitor, Func<bool> isEnabled)
        {
            this.monitor = monitor;
            this.isEnabled = isEnabled;
        }

        public void Log(string message)
        {
            if (Enabled)
                monitor.Log("debug: " + message, LogLevel.Debug);
        }
    }
}
