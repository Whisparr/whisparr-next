using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.MediaFiles.MovieImport;

public interface IRejectedFileExtensionService
{
    ImportRejection GetRejection(IEnumerable<string> files);
}

public class RejectedFileExtensionService : IRejectedFileExtensionService
{
    public ImportRejection GetRejection(IEnumerable<string> files)
    {
        var extensions = files.Select(Path.GetExtension)
                              .Where(e => e.IsNotNullOrWhiteSpace())
                              .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var dangerousExtensions = extensions.Where(FileExtensions.DangerousExtensions.Contains).ToList();

        if (dangerousExtensions.Count > 0)
        {
            return new ImportRejection(ImportRejectionReason.DangerousFile, $"Caution: Found potentially dangerous file with extension: {string.Join(", ", dangerousExtensions)}");
        }

        var executableExtensions = extensions.Where(FileExtensions.ExecutableExtensions.Contains).ToList();

        if (executableExtensions.Count > 0)
        {
            return new ImportRejection(ImportRejectionReason.ExecutableFile, $"Caution: Found executable file with extension: {string.Join(", ", executableExtensions)}");
        }

        return null;
    }
}
