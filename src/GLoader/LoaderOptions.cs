using System;

namespace GLoader
{
    internal sealed class LoaderOptions
    {
        public bool DisableMods { get; private set; }
        public bool DedicatedServer { get; private set; }

        public static LoaderOptions FromEnvironment()
        {
            var mode = Environment.GetEnvironmentVariable("GLOADER_MODE");
            return new LoaderOptions
            {
                DedicatedServer = string.Equals(mode, "server", StringComparison.OrdinalIgnoreCase),
                DisableMods = IsTrue(Environment.GetEnvironmentVariable("GLOADER_DISABLE_MODS"))
            };
        }

        private static bool IsTrue(string value)
        {
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }
    }
}
