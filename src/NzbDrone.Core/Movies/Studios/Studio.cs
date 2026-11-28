using System;
using System.Collections.Generic;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Movies.Studios
{
    public class Studio : ModelBase
    {
        public Studio()
        {
            Images = new List<MediaCover.MediaCover>();
            Tags = new HashSet<int>();
            Aliases = new List<string>();
        }

        public string ForeignId { get; set; }
        public string TpdbId { get; set; }
        public int? TpdbNumericId { get; set; }
        public int TmdbId { get; set; }
        public string Title { get; set; }
        public string SortTitle { get; set; }
        public string CleanTitle { get; set; }
        public List<string> Aliases { get; set; }
        public string SearchTitle { get; set; }
        public string CleanSearchTitle { get; set; }
        public string Website { get; set; }
        public List<MediaCover.MediaCover> Images { get; set; }
        public string RootFolderPath { get; set; }
        public string Network { get; set; }
        public DateTime Added { get; set; }
        public bool Monitored { get; set; }
        public bool MoviesMonitored { get; set; }
        public bool JavMonitored { get; set; }
        public bool HasJav { get; set; }

        /// <summary>Hidden studios stay listed but keep no artwork and are not monitored.</summary>
        public StudioVisibility Visibility { get; set; } = StudioVisibility.Visible;

        /// <summary>What Skyhook reported for HasJav; null when its body predates the field. Not stored.</summary>
        public bool? HasJavReported { get; set; }
        public bool WhisparrMonitorNewItems { get; set; } = true;
        public StudioStatus Status { get; set; }
        public DateTime? AfterDate { get; set; }
        public int QualityProfileId { get; set; }
        public bool SearchOnAdd { get; set; }
        public DateTime? LastInfoSync { get; set; }
        public HashSet<int> Tags { get; set; }

        /// <summary>Whether artwork is downloaded for this studio. Off keeps the studio listed but stores no covers.</summary>
        public bool DownloadCovers { get; set; } = true;
        public int MovieCount { get; set; }
        public int SceneCount { get; set; }
        public int TotalMovieCount { get; set; }
        public int TotalSceneCount { get; set; }
        public int JavCount { get; set; }
        public int TotalJavCount { get; set; }
        public long SizeOnDisk { get; set; }

        public void ApplyChanges(Studio otherStudio)
        {
            SearchTitle = otherStudio.SearchTitle;
            CleanSearchTitle = otherStudio.CleanSearchTitle;
            QualityProfileId = otherStudio.QualityProfileId;
            SearchOnAdd = otherStudio.SearchOnAdd;
            Monitored = otherStudio.Monitored;
            MoviesMonitored = otherStudio.MoviesMonitored;
            JavMonitored = otherStudio.JavMonitored;
            ApplyVisibility(otherStudio.Visibility);
            WhisparrMonitorNewItems = otherStudio.WhisparrMonitorNewItems;
            Status = otherStudio.Status;
            AfterDate = otherStudio.AfterDate;
            RootFolderPath = otherStudio.RootFolderPath;
            Tags = otherStudio.Tags;
            DownloadCovers = otherStudio.DownloadCovers;
        }

        /// <summary>Applies a visibility after the monitored flags have been set.</summary>
        /// <remarks>Hiding a visible studio unmonitors it; a monitored studio is always visible.</remarks>
        public void ApplyVisibility(StudioVisibility visibility)
        {
            if (visibility == StudioVisibility.Hidden && Visibility == StudioVisibility.Visible)
            {
                Visibility = StudioVisibility.Hidden;
                Monitored = false;
                MoviesMonitored = false;
                JavMonitored = false;

                return;
            }

            Visibility = visibility;

            if (Monitored || MoviesMonitored || JavMonitored)
            {
                Visibility = StudioVisibility.Visible;
            }
        }

        public override string ToString()
        {
            return string.Format("[{0}]", Title.NullSafe());
        }
    }

    public enum StudioVisibility
    {
        Hidden = 0,
        Visible = 1
    }

    public enum StudioStatus
    {
        Active,
        Inactive,
        Unknown,
        Deleted
    }
}
