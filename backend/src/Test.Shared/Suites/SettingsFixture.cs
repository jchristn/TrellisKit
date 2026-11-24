namespace Test.Shared.Suites
{
    using System;
    using System.IO;
    using SyslogLogging;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Settings;

    /// <summary>
    /// A settings store, running settings, and settings service backed by a temporary file.
    /// </summary>
    internal sealed class SettingsFixture : IDisposable
    {
        internal AppSettings Running { get; }

        internal SettingsStore Store { get; }

        internal SettingsService Service { get; }

        private readonly LoggingModule _Module;

        private SettingsFixture(AppSettings running, SettingsStore store, SettingsService service, LoggingModule module)
        {
            Running = running;
            Store = store;
            Service = service;
            _Module = module;
        }

        internal static SettingsFixture Create(Func<string, string?> environment)
        {
            SettingsStore store = new SettingsStore(Path.Combine(TestEnvironment.TempRoot, "settings-" + Guid.NewGuid().ToString("N") + ".json"), environment);
            AppSettings running = store.LoadEffective(out string? _);
            LoggingModule module = TestLogging.CreateModule();
            return new SettingsFixture(running, store, new SettingsService(running, store, new AppLogger(module)), module);
        }

        public void Dispose()
        {
            Service.Dispose();
            _Module.Dispose();
            try
            {
                File.Delete(Store.FilePath);
            }
            catch (IOException)
            {
                // Temp file cleanup is best-effort.
            }
        }
    }
}
