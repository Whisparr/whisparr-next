using System;

namespace NzbDrone.Core.RootFolders
{
    // The top-ranked suggestion for an unmapped file, stored as JSON on ImportFiles.
    public class UnmappedFileMatch
    {
        public string ForeignId { get; set; }
        public string Title { get; set; }
        public string StudioTitle { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public bool HashMatch { get; set; }
        public bool SameDate { get; set; }
        public bool SameStudio { get; set; }

        // Only set when no other result carries the same code, so a code shared by two titles is never shown as a match
        public bool CodeMatch { get; set; }

        // The only matches worth showing in the table: the studio and the date both agree with the file, or the JAV code does
        public bool IsStrong()
        {
            return (SameStudio && SameDate) || CodeMatch;
        }
    }
}
