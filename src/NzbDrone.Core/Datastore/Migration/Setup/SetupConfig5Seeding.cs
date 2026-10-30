using System;
using System.IO;
using System.Security.Cryptography;

namespace NzbDrone.Core.Datastore.Migration.Setup
{
    // Setup mode takes its sign-in settings from the legacy config.xml on every start. config5.xml is seeded from it,
    // and a small marker next to it holds the hash of what was written, so a later start can tell its own file from
    // one the user made (or changed) and only ever overwrites its own.
    public static class SetupConfig5Seeding
    {
        private const string MarkerSuffix = ".setup-seeded";

        // True when config5.xml was written from the legacy config.xml on this start, or by an earlier setup-mode start.
        // A config5.xml that setup did not write is left alone.
        public static bool Seed(IConfig5Seeder seeder, string legacyConfigPath, string config5Path, LegacyInstall install)
        {
            if (install == null || !File.Exists(legacyConfigPath))
            {
                return IsSetupSeeded(config5Path);
            }

            var exists = File.Exists(config5Path);

            if (exists && !IsSetupSeeded(config5Path))
            {
                return false;
            }

            seeder.Seed(legacyConfigPath, config5Path, install);
            File.WriteAllText(MarkerPath(config5Path), Hash(config5Path));

            return true;
        }

        // True when config5.xml is still byte for byte what setup wrote.
        public static bool IsSetupSeeded(string config5Path)
        {
            var marker = MarkerPath(config5Path);

            return File.Exists(config5Path) && File.Exists(marker) && string.Equals(File.ReadAllText(marker).Trim(), Hash(config5Path), StringComparison.Ordinal);
        }

        public static void ClearMarker(string config5Path)
        {
            File.Delete(MarkerPath(config5Path));
        }

        private static string MarkerPath(string config5Path)
        {
            return config5Path + MarkerSuffix;
        }

        private static string Hash(string path)
        {
            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        }
    }
}
