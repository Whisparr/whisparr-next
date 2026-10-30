using System;
using System.IO;

namespace NzbDrone.Core.Datastore.Migration.Setup
{
    // Setup mode takes its sign-in settings from the legacy config.xml on every start. config5.xml is seeded from it,
    // and a small marker next to it records that setup created the file, so a later start can tell it from one the
    // user made and only ever overwrites its own. A hand edit to a setup-made file loses to config.xml on purpose.
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
            File.WriteAllText(MarkerPath(config5Path), DateTime.UtcNow.ToString("O"));

            return true;
        }

        // True when setup created config5.xml. The app adds its own defaults to the file afterwards, so the marker
        // means "setup made this", not "unchanged".
        public static bool IsSetupSeeded(string config5Path)
        {
            return File.Exists(config5Path) && File.Exists(MarkerPath(config5Path));
        }

        public static void ClearMarker(string config5Path)
        {
            File.Delete(MarkerPath(config5Path));
        }

        private static string MarkerPath(string config5Path)
        {
            return config5Path + MarkerSuffix;
        }
    }
}
