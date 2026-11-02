using StardewModdingAPI;
using SimpleBank.Core;

namespace SimpleBank
{
    internal sealed class SaveRepository
    {
        private readonly IModHelper helper;
        private readonly IMonitor monitor;
        private readonly ModDiagnostics diagnostics;
        public bool CanWrite { get; private set; } = true;
        public bool HasUnsupportedSchema { get; private set; }
        public bool HasInvalidData { get; private set; }
        public SaveRepository(IModHelper helper, IMonitor monitor, ModDiagnostics diagnostics)
        {
            this.helper = helper;
            this.monitor = monitor;
            this.diagnostics = diagnostics;
        }
        public BankState Read(BankConfig config, int absoluteDay)
        {
            CanWrite = true;
            HasUnsupportedSchema = false;
            HasInvalidData = false;
            BankState state;
            try { state = helper.Data.ReadSaveData<BankState>("bank-state"); }
            catch (System.Exception ex)
            {
                CanWrite = false;
                HasInvalidData = true;
                monitor.Log("Could not deserialize bank save data; preserving it and locking bank transactions: " + ex.Message, LogLevel.Error);
                return null;
            }
            if (state == null)
            {
                diagnostics.Log("save-read: no existing bank-state record");
                return null;
            }
            var sourceSchema = state.SchemaVersion;
            BankState migrated;
            if (!BankStateMigrator.TryMigrateToCurrent(state, config, absoluteDay, out migrated))
            {
                CanWrite = false;
                HasUnsupportedSchema = true;
                monitor.Log("Unknown save schema; preserving data and locking bank transactions.", LogLevel.Warn);
                return null;
            }
            state = migrated;
            if (sourceSchema != state.SchemaVersion)
                diagnostics.Log("save-read: migrated schema=" + sourceSchema + " to schema=" + state.SchemaVersion + " with explicit fractional-interest and collection-contract defaults");
            var errors = BankStateValidator.Validate(state);
            if (errors.Count > 0)
            {
                CanWrite = false;
                HasInvalidData = true;
                monitor.Log("Invalid bank save data; preserving it and locking bank transactions: " + string.Join("; ", errors), LogLevel.Error);
                return null;
            }
            if (diagnostics.Enabled)
                diagnostics.Log("save-read: schema=" + state.SchemaVersion + ", mode=" + state.WalletMode + ", revision=" + state.Revision + ", personalAccounts=" + state.PersonalAccounts.Count);
            return state;
        }
        public void Write(BankState state)
        {
            if (!CanWrite) { monitor.Log("Skipped bank save write because the existing schema is unsupported.", LogLevel.Warn); return; }
            helper.Data.WriteSaveData("bank-state", state);
            if (diagnostics.Enabled)
                diagnostics.Log("save-write: mode=" + state.WalletMode + ", revision=" + state.Revision + ", personalAccounts=" + state.PersonalAccounts.Count);
        }
    }
}
