using System;

namespace NzbDrone.Core.LegacyMigration
{
    // A studio or performer catalog import gave up on some pages. The scenes on them are not in the library, so the
    // first refresh after the migration would add them with the owner's settings (monitored, perhaps) and grab them.
    // Transient: the import runs again on a later pass, which finds the missing pages.
    public sealed class CatalogIncompleteException : Exception
    {
        public CatalogIncompleteException(string owner, int gaveUp)
            : base($"{gaveUp} catalog page(s) for {owner} could not be fetched")
        {
        }
    }
}
