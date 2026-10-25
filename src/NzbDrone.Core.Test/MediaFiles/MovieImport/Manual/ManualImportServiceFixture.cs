using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Fingerprints;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.MediaFiles.MovieImport.Aggregation;
using NzbDrone.Core.MediaFiles.MovieImport.Manual;
using NzbDrone.Core.MetadataSource.SkyHook.Fingerprints;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.MovieImport.Manual
{
    [TestFixture]
    public class ManualImportServiceFixture : CoreTest<ManualImportService>
    {
        [Test]
        public void should_return_nothing_for_an_unknown_download()
        {
            Subject.GetMediaFiles(null, "missing", null, true).Should().BeEmpty();
        }

        [Test]
        public void should_return_nothing_for_a_download_that_has_not_completed()
        {
            Mocker.GetMock<ITrackedDownloadService>()
                  .Setup(s => s.Find("downloading"))
                  .Returns(new TrackedDownload { DownloadItem = new DownloadClientItem() });

            Subject.GetMediaFiles(null, "downloading", null, true).Should().BeEmpty();

            Mocker.GetMock<IDiskProvider>().Verify(v => v.FolderExists(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_not_scan_a_missing_movie_folder()
        {
            var movie = new Movie { Id = 1, Path = @"C:\Test\Movies\Missing".AsOsAgnostic() };

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetMovie(movie.Id))
                  .Returns(movie);

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesByMovie(movie.Id))
                  .Returns(new List<MovieFile>());

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(movie.Path))
                  .Returns(false);

            Subject.GetMediaFiles(movie.Id).Should().BeEmpty();

            Mocker.GetMock<IDiskScanService>().Verify(v => v.GetVideoFiles(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void manual_import_for_scene_in_shared_folder_lists_owned_and_identified_files_only()
        {
            var studioFolder = @"C:\Test\Scenes\Studio".AsOsAgnostic();
            var scene = new Movie { Id = 1, Path = studioFolder };
            var owned = System.IO.Path.Combine(studioFolder, "owned.mp4");
            var identified = System.IO.Path.Combine(studioFolder, "identified.mp4");
            var sibling = System.IO.Path.Combine(studioFolder, "sibling.mp4");
            var unknown = System.IO.Path.Combine(studioFolder, "unknown.mp4");

            Mocker.GetMock<IMovieService>().Setup(s => s.GetMovie(scene.Id)).Returns(scene);
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesByMovie(scene.Id))
                  .Returns(new List<MovieFile> { new() { Id = 5, RelativePath = "owned.mp4", Quality = new QualityModel(Quality.Unknown), Languages = new List<Language>() } });
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderExists(studioFolder)).Returns(true);
            Mocker.GetMock<ISceneFolderLayout>().Setup(s => s.IsShared(scene)).Returns(true);
            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.GetVideoFiles(studioFolder, false))
                  .Returns(new[] { owned, identified, sibling, unknown });
            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.FilterPaths(studioFolder, It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()))
                  .Returns<string, IEnumerable<string>, bool>((_, paths, _) => paths.ToList());

            var siblingScene = new Movie { Id = 2, Path = studioFolder };

            Mocker.GetMock<ISharedFolderScanner>()
                  .Setup(s => s.IdentifyOwned(scene, It.Is<List<string>>(l => !l.Contains(owned))))
                  .Returns(new List<ImportDecision>
                  {
                      new(new LocalMovie { Path = identified, Movie = scene, Quality = new QualityModel(Quality.Unknown), Languages = new List<Language>() }),
                      new(new LocalMovie { Path = sibling, Movie = siblingScene, Quality = new QualityModel(Quality.Unknown), Languages = new List<Language>() })
                  });

            var items = Subject.GetMediaFiles(scene.Id);

            items.Select(i => i.Path).Should().BeEquivalentTo(new List<string> { owned, identified });
            items.Should().OnlyContain(i => i.Movie.Id == scene.Id);
        }

        [Test]
        public void shared_folder_does_not_resolve_to_a_scene_by_path()
        {
            GivenUnknownFiles("a.mp4");

            Mocker.GetMock<ISceneFolderLayout>().Setup(s => s.IsFlat()).Returns(false);
            Mocker.GetMock<ISceneFolderLayout>()
                  .Setup(s => s.GetItemsInFolder(Folder))
                  .Returns(new List<MoviePathEntry> { new() { Id = 1, Path = Folder }, new() { Id = 2, Path = Folder } });
            Mocker.GetMock<IMovieService>().Setup(s => s.FindByPath(Folder)).Returns(new Movie { Id = 1, Path = Folder });

            var items = Subject.GetMediaFiles(Folder, null, null, true);

            items.Should().ContainSingle().Which.Movie.Should().BeNull();
            Mocker.GetMock<IMovieService>().Verify(v => v.FindByPath(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void flat_format_does_not_resolve_to_a_scene_by_path()
        {
            GivenUnknownFiles("a.mp4");

            Mocker.GetMock<ISceneFolderLayout>().Setup(s => s.IsFlat()).Returns(true);

            Subject.GetMediaFiles(Folder, null, null, true).Should().ContainSingle().Which.Movie.Should().BeNull();
            Mocker.GetMock<IMovieService>().Verify(v => v.FindByPath(It.IsAny<string>()), Times.Never());
        }

        private static readonly string Folder = @"C:\Test\Unknown".AsOsAgnostic();

        private List<string> GivenUnknownFiles(params string[] names)
        {
            var files = names.Select(n => System.IO.Path.Combine(Folder, n)).ToList();

            Mocker.GetMock<ISceneFolderLayout>()
                  .Setup(s => s.GetItemsInFolder(It.IsAny<string>()))
                  .Returns(new List<MoviePathEntry>());
            Mocker.GetMock<IConfigService>().SetupGet(s => s.WhisparrFolderLimit).Returns(100);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderExists(Folder)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetDirectories(Folder)).Returns(Array.Empty<string>());
            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.FilterPaths(Folder, It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()))
                  .Returns<string, IEnumerable<string>, bool>((_, paths, _) => paths.ToList());
            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.GetVideoFiles(Folder, false))
                  .Returns(files.ToArray());
            Mocker.GetMock<IAggregationService>()
                  .Setup(s => s.Augment(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>()))
                  .Returns<LocalMovie, DownloadClientItem>((lm, _) => lm);
            Mocker.GetMock<IVideoHashService>()
                  .Setup(s => s.ComputeOsHash(It.IsAny<string>()))
                  .Returns<string>(p => "hash-" + System.IO.Path.GetFileName(p));

            return files;
        }

        [Test]
        public void should_do_one_upstream_lookup_for_all_unknown_files_and_attach_candidates()
        {
            var files = GivenUnknownFiles("a.mp4", "b.mp4");
            var candidate = new FingerprintCandidate { SceneId = "s1", MovieId = 7 };
            IReadOnlyCollection<FileFingerprint> sent = null;

            Mocker.GetMock<IFingerprintLookupClient>()
                  .Setup(s => s.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), false))
                  .Callback<IReadOnlyCollection<FileFingerprint>, bool>((f, _) => sent = f)
                  .Returns<IReadOnlyCollection<FileFingerprint>, bool>((f, _) => f.ToDictionary(
                      x => x.Key,
                      x => new FingerprintLookupResult
                      {
                          Key = x.Key,
                          Status = FingerprintLookupStatus.Matched,
                          Candidates = new List<FingerprintCandidateResult> { new() { SceneId = "s1" } }
                      }));
            Mocker.GetMock<IFileMatchEvaluator>()
                  .Setup(s => s.BuildCandidates(It.IsAny<FingerprintLookupResult>()))
                  .Returns(new List<FingerprintCandidate> { candidate });

            var items = Subject.GetMediaFiles(Folder, null, null, true);

            items.Should().HaveCount(2);
            items.Should().OnlyContain(i => i.FingerprintCandidates.Count == 1 && i.FingerprintCandidates[0].SceneId == "s1");
            items.Should().OnlyContain(i => i.Rejections.Any(r => r.Reason == ImportRejectionReason.UnknownMovie));
            sent.Select(f => f.Key).Should().BeEquivalentTo(files);
            Mocker.GetMock<IFingerprintLookupClient>()
                  .Verify(v => v.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), false), Times.Once());
            Mocker.GetMock<IFingerprintLookupClient>()
                  .Verify(v => v.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), true), Times.Never());
        }

        [Test]
        public void should_return_items_without_candidates_when_the_lookup_throws()
        {
            GivenUnknownFiles("a.mp4");

            Mocker.GetMock<IFingerprintLookupClient>()
                  .Setup(s => s.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), false))
                  .Throws(new InvalidOperationException("boom"));

            var items = Subject.GetMediaFiles(Folder, null, null, true);

            items.Should().ContainSingle();
            items[0].FingerprintCandidates.Should().BeEmpty();
            items[0].Rejections.Should().Contain(r => r.Reason == ImportRejectionReason.UnknownMovie);
        }

        [Test]
        public void should_not_look_up_files_without_an_oshash()
        {
            GivenUnknownFiles("a.mp4");

            Mocker.GetMock<IVideoHashService>()
                  .Setup(s => s.ComputeOsHash(It.IsAny<string>()))
                  .Returns((string)null);

            var items = Subject.GetMediaFiles(Folder, null, null, true);

            items.Should().ContainSingle();
            Mocker.GetMock<IFingerprintLookupClient>()
                  .Verify(v => v.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_surface_conflict_candidates_on_the_item()
        {
            var movie = new Movie { Id = 3, Path = Folder };
            var file = System.IO.Path.Combine(Folder, "a.mp4");
            var candidate = new FingerprintCandidate { SceneId = "other", MovieId = 9 };
            var localMovie = new LocalMovie
            {
                Path = file,
                Movie = movie,
                Quality = new QualityModel(Quality.Unknown),
                Languages = new List<Language>(),
                FingerprintCandidates = new List<FingerprintCandidate> { candidate }
            };

            Mocker.GetMock<IMovieService>().Setup(s => s.GetMovie(movie.Id)).Returns(movie);
            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(s => s.GetImportDecisions(It.IsAny<List<string>>(), movie, It.IsAny<DownloadClientItem>(), It.IsAny<ParsedMovieInfo>(), It.IsAny<bool>(), It.IsAny<bool>()))
                  .Returns(new List<ImportDecision>
                  {
                      new(localMovie, new ImportRejection(ImportRejectionReason.FingerprintConflict, "conflict"))
                  });
            Mocker.GetMock<IConfigService>().SetupGet(s => s.WhisparrFolderLimit).Returns(100);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FolderExists(Folder)).Returns(true);
            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.GetVideoFiles(Folder, true))
                  .Returns(new[] { file });
            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.FilterPaths(Folder, It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()))
                  .Returns<string, IEnumerable<string>, bool>((_, paths, _) => paths.ToList());

            var items = Subject.GetMediaFiles(Folder, null, movie.Id, true);

            items.Should().ContainSingle();
            items[0].FingerprintCandidates.Should().ContainSingle().Which.SceneId.Should().Be("other");
            items[0].Rejections.Should().Contain(r => r.Reason == ImportRejectionReason.FingerprintConflict);
            Mocker.GetMock<IFingerprintLookupClient>()
                  .Verify(v => v.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), It.IsAny<bool>()), Times.Never());
        }

        private (Movie Movie, string Path) GivenReprocessableConflict()
        {
            var movie = new Movie { Id = 3, Path = @"C:\Test\Movies\Scene".AsOsAgnostic() };
            movie.MovieMetadata.Value.OriginalLanguage = Language.English;

            var path = System.IO.Path.Combine(Folder, "a.mp4");

            Mocker.GetMock<IMovieService>().Setup(s => s.GetMovie(movie.Id)).Returns(movie);
            Mocker.GetMock<IAggregationService>()
                  .Setup(s => s.Augment(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>()))
                  .Returns<LocalMovie, DownloadClientItem>((lm, _) => lm);
            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(s => s.GetDecision(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>()))
                  .Returns<LocalMovie, DownloadClientItem>((lm, _) => new ImportDecision(lm));
            Mocker.GetMock<IVideoHashService>().Setup(s => s.ComputeOsHash(path)).Returns("0123456789abcdef");
            Mocker.GetMock<IFingerprintLookupClient>()
                  .Setup(s => s.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), true))
                  .Returns(new Dictionary<string, FingerprintLookupResult> { { path, new FingerprintLookupResult { Status = FingerprintLookupStatus.Matched } } });
            Mocker.GetMock<IFileMatchEvaluator>()
                  .Setup(s => s.Evaluate(It.IsAny<Movie>(), It.IsAny<FingerprintLookupResult>(), It.IsAny<double?>()))
                  .Returns(new FileMatchOutcome
                  {
                      Tier = MatchTier.Conflict,
                      Conflict = true,
                      NeedsReview = true,
                      Candidates = new List<FingerprintCandidate> { new() { SceneId = "other" } }
                  });
            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString("FingerprintConflictRejection", It.IsAny<Dictionary<string, object>>()))
                  .Returns("conflict");

            return (movie, path);
        }

        private ManualImportItem Reprocess(Movie movie, string path, int? conflictMovieId)
        {
            return Subject.ReprocessItem(path, null, movie.Id, null, new QualityModel(Quality.Unknown), new List<Language> { Language.English }, 0, conflictMovieId);
        }

        [Test]
        public void should_keep_the_fingerprint_conflict_when_reprocessing_the_same_scene()
        {
            var (movie, path) = GivenReprocessableConflict();

            var item = Reprocess(movie, path, movie.Id);

            item.Rejections.Should().ContainSingle(r => r.Reason == ImportRejectionReason.FingerprintConflict && r.Message == "conflict");
            item.FingerprintCandidates.Should().ContainSingle().Which.SceneId.Should().Be("other");
            item.FingerprintConflictMovieId.Should().Be(movie.Id);
        }

        [Test]
        public void should_drop_the_fingerprint_conflict_when_another_scene_was_picked()
        {
            var (movie, path) = GivenReprocessableConflict();

            var item = Reprocess(movie, path, movie.Id + 1);

            item.Rejections.Should().BeEmpty();
            item.FingerprintConflictMovieId.Should().BeNull();
            Mocker.GetMock<IFingerprintLookupClient>()
                  .Verify(v => v.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_not_look_up_on_reprocess_when_the_item_had_no_conflict()
        {
            var (movie, path) = GivenReprocessableConflict();

            var item = Reprocess(movie, path, null);

            item.Rejections.Should().BeEmpty();
            Mocker.GetMock<IFingerprintLookupClient>()
                  .Verify(v => v.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_clear_the_conflict_on_reprocess_when_the_hash_no_longer_contradicts_the_scene()
        {
            var (movie, path) = GivenReprocessableConflict();

            Mocker.GetMock<IFileMatchEvaluator>()
                  .Setup(s => s.Evaluate(It.IsAny<Movie>(), It.IsAny<FingerprintLookupResult>(), It.IsAny<double?>()))
                  .Returns(new FileMatchOutcome { Tier = MatchTier.Confirmed });

            Reprocess(movie, path, movie.Id).Rejections.Should().BeEmpty();
        }

        [Test]
        public void should_import_with_the_manual_match_tier()
        {
            var movie = new Movie { Id = 3, Path = @"C:\Test\Movies\Scene".AsOsAgnostic() };
            var path = System.IO.Path.Combine(Folder, "a.mp4");
            List<ImportDecision> imported = null;

            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(path)).Returns(true);
            Mocker.GetMock<IMovieService>().Setup(s => s.GetMovie(movie.Id)).Returns(movie);
            Mocker.GetMock<IAggregationService>()
                  .Setup(s => s.Augment(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>()))
                  .Returns<LocalMovie, DownloadClientItem>((lm, _) => lm);
            Mocker.GetMock<IImportApprovedMovie>()
                  .Setup(s => s.Import(It.IsAny<List<ImportDecision>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()))
                  .Callback<List<ImportDecision>, bool, DownloadClientItem, ImportMode>((d, _, _, _) => imported = d)
                  .Returns(new List<ImportResult>());

            Subject.Execute(new ManualImportCommand
            {
                ImportMode = ImportMode.Auto,
                Files = new List<ManualImportFile>
                {
                    new()
                    {
                        Path = path,
                        MovieId = movie.Id,
                        Quality = new QualityModel(Quality.Unknown),
                        Languages = new List<Language> { Language.English }
                    }
                }
            });

            imported.Should().ContainSingle();
            imported[0].LocalMovie.MatchTier.Should().Be(MatchTier.Manual);
        }
    }
}
