using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Fingerprints;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.SkyHook.Fingerprints;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class SharedFolderScannerFixture : CoreTest<SharedFolderScanner>
    {
        private string _folder;
        private Movie _s1;
        private Movie _s2;
        private Movie _outside;

        [SetUp]
        public void Setup()
        {
            _folder = @"C:\lib\scenes\Shady Spa".AsOsAgnostic();
            _s1 = Scene(1, _folder);
            _s2 = Scene(2, _folder);
            _outside = Scene(3, Path.Combine(_folder, "old S3 folder"));

            Mocker.GetMock<ISceneFolderLayout>()
                  .Setup(s => s.GetItemsInFolder(_folder))
                  .Returns(new List<MoviePathEntry>
                  {
                      new() { Id = 1, Path = _folder, ItemType = ItemType.Scene },
                      new() { Id = 2, Path = _folder, ItemType = ItemType.Scene }
                  });

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetMovies(It.IsAny<IEnumerable<int>>()))
                  .Returns(new List<Movie> { _s1, _s2 });

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.FilterFilesTrackedByAnyItem(_folder, It.IsAny<List<string>>()))
                  .Returns((string _, List<string> files) => files);
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesByMovie(It.IsAny<int>()))
                  .Returns(new List<MovieFile>());

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.ParseMinimalPathMovieInfo(It.IsAny<string>()))
                  .Returns(new ParsedMovieInfo());

            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(s => s.GetImportDecisions(It.IsAny<List<string>>(), It.IsAny<Movie>(), false))
                  .Returns((List<string> files, Movie owner, bool _) => files.Select(f => new ImportDecision(new LocalMovie { Movie = owner, Path = f })).ToList());

            Mocker.GetMock<IVideoHashService>().Setup(s => s.ComputeOsHash(It.IsAny<string>())).Returns("hash");
        }

        private static Movie Scene(int id, string path)
        {
            return new Movie
            {
                Id = id,
                Path = path,
                MovieMetadata = new MovieMetadata { ItemType = ItemType.Scene }
            };
        }

        private string F(string name) => Path.Combine(_folder, name);

        private void GivenNameIdentifies(string file, Movie scene)
        {
            var info = new ParsedMovieInfo { ReleaseTitle = file };

            Mocker.GetMock<IParsingService>().Setup(s => s.ParseMinimalPathMovieInfo(file)).Returns(info);
            Mocker.GetMock<IMovieService>().Setup(s => s.FindScene(info, false, null)).Returns(scene);
        }

        private void GivenHashIdentifies(Movie scene)
        {
            Mocker.GetMock<IFingerprintLookupClient>()
                  .Setup(s => s.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), true))
                  .Returns((IReadOnlyCollection<FileFingerprint> files, bool _) => files.ToDictionary(f => f.Key, f => new FingerprintLookupResult { Key = f.Key }));

            Mocker.GetMock<IFileMatchEvaluator>()
                  .Setup(s => s.Evaluate(null, It.IsAny<FingerprintLookupResult>(), It.IsAny<double?>(), false))
                  .Returns(new FileMatchOutcome { Tier = MatchTier.Hash, Target = scene });
        }

        [Test]
        public void imports_each_file_for_the_scene_it_identifies_as()
        {
            GivenNameIdentifies(F("s1.mp4"), _s1);
            GivenNameIdentifies(F("s2.mp4"), _s2);

            var decisions = Subject.IdentifyOwned(_s1, new List<string> { F("s1.mp4"), F("s2.mp4") });

            decisions.Single(d => d.LocalMovie.Path == F("s1.mp4")).LocalMovie.Movie.Id.Should().Be(1);
            decisions.Single(d => d.LocalMovie.Path == F("s2.mp4")).LocalMovie.Movie.Id.Should().Be(2);
        }

        [Test]
        public void identifies_by_hash_when_the_name_does_not()
        {
            GivenHashIdentifies(_s2);

            var decisions = Subject.IdentifyOwned(_s1, new List<string> { F("a8f3c0de.mp4") });

            decisions.Should().ContainSingle().Which.LocalMovie.Movie.Id.Should().Be(2);
        }

        [Test]
        public void identifies_by_the_hash_of_a_file_a_folder_scene_already_has()
        {
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesByMovie(2))
                  .Returns(new List<MovieFile> { new() { MovieId = 2, OsHash = "hash" } });

            var decisions = Subject.IdentifyOwned(_s1, new List<string> { F("copy.mp4") });

            decisions.Should().ContainSingle().Which.LocalMovie.Movie.Id.Should().Be(2);

            Mocker.GetMock<IFingerprintLookupClient>()
                  .Verify(v => v.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void never_adds_scenes_or_unmapped_rows()
        {
            var decisions = Subject.IdentifyOwned(_s1, new List<string> { F("unknown.mp4") });

            decisions.Should().BeEmpty();

            Mocker.GetMock<IMakeImportDecision>()
                  .Verify(v => v.GetSceneImportDecisions(It.IsAny<List<string>>()), Times.Never());
            Mocker.GetMock<IMakeImportDecision>()
                  .Verify(v => v.GetImportDecisions(It.IsAny<List<string>>(), It.IsAny<Movie>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IMediaFileService>()
                  .Verify(v => v.Add(It.IsAny<MovieFile>()), Times.Never());
        }

        [Test]
        public void drops_decisions_for_scenes_outside_the_folder()
        {
            GivenNameIdentifies(F("old S3 folder/s3.mp4"), _outside);

            var decisions = Subject.IdentifyOwned(_s1, new List<string> { F("old S3 folder/s3.mp4") });

            decisions.Should().BeEmpty();

            Mocker.GetMock<IVideoHashService>().Verify(v => v.ComputeOsHash(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void drops_a_hash_match_to_a_scene_outside_the_folder()
        {
            GivenHashIdentifies(_outside);

            Subject.IdentifyOwned(_s1, new List<string> { F("x.mp4") }).Should().BeEmpty();
        }

        [Test]
        public void does_not_move_or_search_online()
        {
            GivenNameIdentifies(F("s1.mp4"), _s1);
            GivenHashIdentifies(_s2);

            Subject.IdentifyOwned(_s1, new List<string> { F("s1.mp4"), F("other.mp4") });

            Mocker.GetMock<ISceneIdentificationService>().Verify(v => v.Identify(It.IsAny<string>(), It.IsAny<ParsedMovieInfo>()), Times.Never());
            Mocker.GetMock<ISearchForNewMovie>().VerifyNoOtherCalls();
            Mocker.GetMock<IDiskTransferService>().VerifyNoOtherCalls();
            Mocker.GetMock<IDiskProvider>().Verify(v => v.CreateFolder(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IFingerprintLookupClient>().Verify(v => v.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), false), Times.Never());
        }

        [Test]
        public void caches_unidentified_files_for_ten_minutes()
        {
            var file = F("unknown.mp4");

            Subject.IdentifyOwned(_s1, new List<string> { file });
            Subject.IdentifyOwned(_s1, new List<string> { file });

            Mocker.GetMock<IParsingService>().Verify(v => v.ParseMinimalPathMovieInfo(file), Times.Once());
        }

        [Test]
        public void identifies_again_when_the_file_changed()
        {
            var file = F("unknown.mp4");

            Subject.IdentifyOwned(_s1, new List<string> { file });

            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFileSize(file)).Returns(999);

            Subject.IdentifyOwned(_s1, new List<string> { file });

            Mocker.GetMock<IParsingService>().Verify(v => v.ParseMinimalPathMovieInfo(file), Times.Exactly(2));
        }

        [Test]
        public void does_not_cache_files_that_identify_as_a_scene_elsewhere()
        {
            var file = F("old S3 folder/s3.mp4");

            GivenNameIdentifies(file, _outside);

            Subject.IdentifyOwned(_s1, new List<string> { file });
            Subject.IdentifyOwned(_s1, new List<string> { file });

            Mocker.GetMock<IParsingService>().Verify(v => v.ParseMinimalPathMovieInfo(file), Times.Exactly(2));
        }

        [Test]
        public void lost_file_check_begins_once_per_window_and_again_when_the_file_changed()
        {
            var file = F("unowned.mp4");

            Subject.TryBeginLostFileCheck(file).Should().BeTrue();
            Subject.TryBeginLostFileCheck(file).Should().BeFalse();

            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFileSize(file)).Returns(999);

            Subject.TryBeginLostFileCheck(file).Should().BeTrue();
        }

        [Test]
        public void lost_file_check_does_not_hide_the_file_from_identification()
        {
            var file = F("unknown.mp4");

            Subject.TryBeginLostFileCheck(file).Should().BeTrue();
            Subject.IdentifyOwned(_s1, new List<string> { file });

            Mocker.GetMock<IParsingService>().Verify(v => v.ParseMinimalPathMovieInfo(file), Times.Once());
        }

        [Test]
        public void ignores_files_tracked_by_any_item()
        {
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.FilterFilesTrackedByAnyItem(_folder, It.IsAny<List<string>>()))
                  .Returns(new List<string>());

            Subject.IdentifyOwned(_s1, new List<string> { F("tracked.mp4") }).Should().BeEmpty();

            Mocker.GetMock<IParsingService>().Verify(v => v.ParseMinimalPathMovieInfo(It.IsAny<string>()), Times.Never());
        }
    }
}
