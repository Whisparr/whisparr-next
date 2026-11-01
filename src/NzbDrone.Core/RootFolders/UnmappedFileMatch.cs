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

        // The only matches worth showing in the table: the studio and the date both agree with the file
        public bool IsStrong()
        {
            return SameStudio && SameDate;
        }
    }
}
