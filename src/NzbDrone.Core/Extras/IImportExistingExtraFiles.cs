using System.Collections.Generic;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.Movies;

namespace NzbDrone.Core.Extras
{
    public interface IImportExistingExtraFiles
    {
        int Order { get; }

        // resolvedOwners, when given, maps a side file to its owner's MovieFile.Id and replaces the ownership lookup
        IEnumerable<ExtraFile> ProcessFiles(Movie movie, List<string> filesOnDisk, List<string> importedFiles, string fileNameBeforeRename, IReadOnlyDictionary<string, int> resolvedOwners = null);
    }
}
