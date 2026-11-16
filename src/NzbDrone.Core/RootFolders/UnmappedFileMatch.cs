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
        public bool SameTitle { get; set; }
        public bool SameYear { get; set; }

        // The only matches worth showing in the table: studio and date agree with the file, the JAV code does,
        // or (for movies) the title and year do
        public bool IsStrong()
        {
            return (SameStudio && SameDate) || CodeMatch || (SameTitle && SameYear);
        }
    }
}
