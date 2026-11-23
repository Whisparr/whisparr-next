using System;
using System.Collections.Generic;
using System.Linq;
using Equ;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.SkyHook.Resource;
using NzbDrone.Core.Movies.AlternativeTitles;
using NzbDrone.Core.Movies.Credits;

namespace NzbDrone.Core.Movies
{
    public class MovieMetadata : Entity<MovieMetadata>
    {
        public MovieMetadata()
        {
            AlternativeTitles = new List<AlternativeTitle>();
            Credits = new List<Credit>();
            Images = new List<MediaCover.MediaCover>();
            Genres = new List<string>();
            OriginalLanguage = Language.English;
            Recommendations = new List<int>();
            Ratings = new Ratings();
            TagIds = new List<string>();
            PerformerNames = new List<string>();
            PerformerForeignIds = new List<string>();
        }

        public string ForeignId { get; set; }

        public List<MediaCover.MediaCover> Images { get; set; }
        public List<string> Genres { get; set; }
        public List<string> TagIds { get; set; }
        public DateTime? ReleaseDateUtc { get; set; }
        public string ReleaseDate { get; set; }
        public int Year { get; set; }
        public int CollectionTmdbId { get; set; }
        public string CollectionTitle { get; set; }
        public Ratings Ratings { get; set; }
        public string StudioForeignId { get; set; }
        public StudioResource Studio { get; set; }
        public string StudioTitle { get; set; }
        public DateTime? LastInfoSync { get; set; }
        public int Runtime { get; set; }
        public string Website { get; set; }
        public string TpdbId { get; set; }
        public string ImdbId { get; set; }
        public int TmdbId { get; set; }
        public string StashId { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string CleanTitle { get; set; }
        public string SortTitle { get; set; }
        public MovieStatusType Status { get; set; }
        public string Overview { get; set; }
        public List<AlternativeTitle> AlternativeTitles { get; set; }
        public Language OriginalLanguage { get; set; }
        public List<int> Recommendations { get; set; }
        public ItemType ItemType { get; set; }
        public MetadataSource MetadataSource { get; set; }
        public List<string> PerformerForeignIds { get; set; }
        public List<string> PerformerNames { get; set; }
        public List<Credit> Credits { get; set; } // Either Passed from SkyHook or Loaded from the database

        [MemberwiseEqualityIgnore]
        public bool IsRecentMovie
        {
            get
            {
                if (ReleaseDateUtc.HasValue && ReleaseDateUtc.Value >= DateTime.UtcNow.AddDays(-21))
                {
                    return true;
                }

                return false;
            }
        }

        public MediaCover.MediaCover Poster
        {
            // Poster, else Screenshot, else (scenes and JAV only, which may carry just a fanart) the Fanart.
            // Movies never use their fanart as a poster.
            get
            {
                return Images.FirstOrDefault(c => c.CoverType == MediaCoverTypes.Poster)
                    ?? Images.FirstOrDefault(i => i.CoverType == MediaCoverTypes.Screenshot)
                    ?? (ItemType != ItemType.Movie ? Images.FirstOrDefault(c => c.CoverType == MediaCoverTypes.Fanart) : null);
            }
        }

        public MediaCover.MediaCover Fanart
        {
            // Fanart, else (JAV only, which may carry just the DVD cover spread) the Poster
            get
            {
                return Images.FirstOrDefault(c => c.CoverType == MediaCoverTypes.Fanart)
                    ?? (ItemType == ItemType.Jav ? Images.FirstOrDefault(c => c.CoverType == MediaCoverTypes.Poster) : null);
            }
        }

        // Every image keyed by its own cover type, plus the poster/fanart slot the item lacks filled from the other
        // (non-movies without a Poster use the Fanart; JAV without a Fanart uses the Poster).
        public IEnumerable<(MediaCoverTypes CoverType, MediaCover.MediaCover Source)> GetEffectiveCovers()
        {
            foreach (var image in Images)
            {
                yield return (image.CoverType, image);
            }

            var hasPoster = Images.Any(c => c.CoverType == MediaCoverTypes.Poster);
            var fanart = Images.FirstOrDefault(c => c.CoverType == MediaCoverTypes.Fanart);

            if (!hasPoster && fanart != null && ItemType != ItemType.Movie)
            {
                yield return (MediaCoverTypes.Poster, fanart);
            }

            var poster = Images.FirstOrDefault(c => c.CoverType == MediaCoverTypes.Poster);

            if (fanart == null && poster != null && ItemType == ItemType.Jav)
            {
                yield return (MediaCoverTypes.Fanart, poster);
            }
        }
    }

    public enum ItemType
    {
        Movie,
        Scene,
        Studio,
        Performer,
        Jav
    }

    public enum MetadataSource
    {
        Tmdb,
        Stash,
        TpdbMovie,

        // Appended so stored values (an int column) keep their numbers.
        TpdbScene,
        TpdbJav
    }
}
