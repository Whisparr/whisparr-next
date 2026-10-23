using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Download;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Fingerprints;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.MediaFiles.MovieImport.Aggregation;
using NzbDrone.Core.MetadataSource.SkyHook.Fingerprints;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.MovieImport
{
    [TestFixture]

    // TODO: Add tests to ensure helpers for augmenters are correctly passed.
    public class ImportDecisionMakerFixture : CoreTest<ImportDecisionMaker>
    {
        private List<string> _videoFiles;
        private LocalMovie _localMovie;
        private Movie _movie;
        private QualityModel _quality;
        private ParsedMovieInfo _fileInfo;

        private Mock<IImportDecisionEngineSpecification> _pass1;
        private Mock<IImportDecisionEngineSpecification> _pass2;
        private Mock<IImportDecisionEngineSpecification> _pass3;

        private Mock<IImportDecisionEngineSpecification> _fail1;
        private Mock<IImportDecisionEngineSpecification> _fail2;
        private Mock<IImportDecisionEngineSpecification> _fail3;

        [SetUp]
        public void Setup()
        {
            _pass1 = new Mock<IImportDecisionEngineSpecification>();
            _pass2 = new Mock<IImportDecisionEngineSpecification>();
            _pass3 = new Mock<IImportDecisionEngineSpecification>();

            _fail1 = new Mock<IImportDecisionEngineSpecification>();
            _fail2 = new Mock<IImportDecisionEngineSpecification>();
            _fail3 = new Mock<IImportDecisionEngineSpecification>();

            _pass1.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>())).Returns(ImportSpecDecision.Accept());
            _pass2.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>())).Returns(ImportSpecDecision.Accept());
            _pass3.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>())).Returns(ImportSpecDecision.Accept());

            _fail1.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>())).Returns(ImportSpecDecision.Reject(ImportRejectionReason.Unknown, "_fail1"));
            _fail2.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>())).Returns(ImportSpecDecision.Reject(ImportRejectionReason.Unknown, "_fail2"));
            _fail3.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>())).Returns(ImportSpecDecision.Reject(ImportRejectionReason.Unknown, "_fail3"));

            _movie = Builder<Movie>.CreateNew()
                                     .With(e => e.Path = @"C:\Test\Movie".AsOsAgnostic())
                                     .With(e => e.QualityProfile = new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() })
                                     .Build();

            _quality = new QualityModel(Quality.DVD);

            _localMovie = new LocalMovie
            {
                Movie = _movie,
                Quality = _quality,
                Path = @"C:\Test\Unsorted\The.Movie.2018.DVDRip.XviD-OSiTV.avi"
            };

            _fileInfo = new ParsedMovieInfo
            {
                MovieTitles = new List<string> { "The Movie" },
                Year = 2018,
                Quality = _quality
            };

            Mocker.GetMock<IParsingService>()
                .Setup(c => c.ParseMinimalPathMovieInfo(It.IsAny<string>()))
                .Returns(_fileInfo);

            GivenVideoFiles(new List<string> { @"C:\Test\Unsorted\The.Movie.2018.DVDRip.XviD-OSiTV.avi".AsOsAgnostic() });
        }

        private void GivenSpecifications(params Mock<IImportDecisionEngineSpecification>[] mocks)
        {
            Mocker.SetConstant(mocks.Select(c => c.Object));
        }

        private void GivenVideoFiles(IEnumerable<string> videoFiles)
        {
            _videoFiles = videoFiles.ToList();

            Mocker.GetMock<IMediaFileService>()
                  .Setup(c => c.FilterExistingFiles(_videoFiles, It.IsAny<Movie>()))
                  .Returns(_videoFiles);
        }

        private void GivenAugmentationSuccess()
        {
            Mocker.GetMock<IAggregationService>()
                  .Setup(s => s.Augment(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>()))
                  .Callback<LocalMovie, DownloadClientItem>((localMovie, downloadClientItem) =>
                  {
                      localMovie.Movie = _localMovie.Movie;
                  });
        }

        [Test]
        public void should_hash_unmapped_scene_import_files()
        {
            var file = @"C:\Test\Unsorted\Unknown.Studio.24.01.02.Unknown.Scene.mp4".AsOsAgnostic();
            var lastWrite = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

            Mocker.GetMock<ISceneIdentificationService>()
                  .Setup(s => s.Identify(It.IsAny<string>(), It.IsAny<ParsedMovieInfo>()))
                  .Returns(new Movie());

            Mocker.GetMock<IAggregationService>()
                  .Setup(s => s.Augment(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>()))
                  .Returns((LocalMovie localMovie, DownloadClientItem _) => localMovie);

            Mocker.GetMock<IVideoHashService>().Setup(s => s.ComputeOsHash(file)).Returns("0123456789abcdef");
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileGetLastWrite(file)).Returns(lastWrite);

            var result = Subject.GetSceneImportDecisions(new List<string> { file }, null, null, false, false);

            var unmapped = result.UnmappedFiles.Should().ContainSingle().Subject;
            unmapped.OsHash.Should().Be("0123456789abcdef");
            unmapped.HashedFileModifiedAt.Should().Be(lastWrite);
        }

        private void GivenUnparsedSceneFile(string file, string osHash, double durationSeconds = 600)
        {
            Mocker.GetMock<ISceneIdentificationService>()
                  .Setup(s => s.Identify(It.IsAny<string>(), It.IsAny<ParsedMovieInfo>()))
                  .Returns(new Movie());

            Mocker.GetMock<IAggregationService>()
                  .Setup(s => s.Augment(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>()))
                  .Returns((LocalMovie localMovie, DownloadClientItem _) => localMovie);

            Mocker.GetMock<IVideoHashService>().Setup(s => s.ComputeOsHash(file)).Returns(osHash);

            Mocker.GetMock<IVideoFileInfoReader>()
                  .Setup(s => s.GetMediaInfo(file))
                  .Returns(new MediaInfoModel { RunTime = TimeSpan.FromSeconds(durationSeconds) });
        }

        private void GivenUpstreamLookup(Dictionary<string, FingerprintLookupResult> results)
        {
            Mocker.GetMock<IFingerprintLookupClient>()
                  .Setup(s => s.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), false))
                  .Returns(results);
        }

        [Test]
        public void should_link_unparsed_scene_file_with_hash_tier_outcome_as_a_decision()
        {
            var file = @"C:\Test\Unsorted\Unknown.Studio.24.01.02.Unknown.Scene.mp4".AsOsAgnostic();
            var target = GivenSceneInDatabase();
            var lookup = new FingerprintLookupResult { Status = FingerprintLookupStatus.Matched };

            GivenUnparsedSceneFile(file, "0123456789abcdef", 600);
            GivenUpstreamLookup(new Dictionary<string, FingerprintLookupResult> { { file, lookup } });

            Mocker.GetMock<IFileMatchEvaluator>()
                  .Setup(s => s.Evaluate(null, lookup, 600d))
                  .Returns(new FileMatchOutcome { Tier = MatchTier.Hash, Target = target });

            var result = Subject.GetSceneImportDecisions(new List<string> { file }, null, null, false, false);

            result.UnmappedFiles.Should().BeEmpty();
            result.MoviesToAdd.Should().BeEmpty();

            var decision = result.Decisions.Should().ContainSingle().Subject;
            decision.Approved.Should().BeTrue();
            decision.LocalMovie.Path.Should().Be(file);
            decision.LocalMovie.Movie.Should().BeSameAs(target);
            decision.LocalMovie.MatchTier.Should().Be(MatchTier.Hash);
            decision.LocalMovie.OsHash.Should().Be("0123456789abcdef");
            decision.LocalMovie.HashedFileModifiedAt.Should().NotBeNull();
        }

        [Test]
        public void should_link_only_the_first_of_two_unparsed_files_hashing_to_the_same_scene()
        {
            var files = new List<string>
            {
                @"C:\Test\Unsorted\Unknown.A.mp4".AsOsAgnostic(),
                @"C:\Test\Unsorted\Unknown.B.mp4".AsOsAgnostic()
            };
            var target = GivenSceneInDatabase();
            var lookup = new FingerprintLookupResult { Status = FingerprintLookupStatus.Matched };

            GivenUnparsedSceneFile(files[0], "0123456789abcdef");
            Mocker.GetMock<IVideoHashService>().Setup(s => s.ComputeOsHash(files[1])).Returns("fedcba9876543210");
            Mocker.GetMock<IVideoFileInfoReader>().Setup(s => s.GetMediaInfo(files[1])).Returns(new MediaInfoModel());
            GivenUpstreamLookup(files.ToDictionary(f => f, _ => lookup));
            Mocker.GetMock<IFileMatchEvaluator>()
                  .Setup(s => s.Evaluate(It.IsAny<Movie>(), It.IsAny<FingerprintLookupResult>(), It.IsAny<double?>()))
                  .Returns(new FileMatchOutcome { Tier = MatchTier.Hash, Target = target });

            var result = Subject.GetSceneImportDecisions(files, null, null, false, false);

            result.Decisions.Should().ContainSingle().Which.LocalMovie.Path.Should().Be(files[0]);
            result.UnmappedFiles.Should().ContainSingle().Which.OriginalFilePath.Should().Be(files[1]);
        }

        [Test]
        public void should_look_up_all_unparsed_scene_files_upstream_in_one_call()
        {
            var files = new List<string>
            {
                @"C:\Test\Unsorted\Unknown.A.mp4".AsOsAgnostic(),
                @"C:\Test\Unsorted\Unknown.B.mp4".AsOsAgnostic()
            };

            GivenUnparsedSceneFile(files[0], "0123456789abcdef");
            Mocker.GetMock<IVideoHashService>().Setup(s => s.ComputeOsHash(files[1])).Returns("fedcba9876543210");
            Mocker.GetMock<IVideoFileInfoReader>().Setup(s => s.GetMediaInfo(files[1])).Returns(new MediaInfoModel());
            GivenUpstreamLookup(new Dictionary<string, FingerprintLookupResult>());

            var result = Subject.GetSceneImportDecisions(files, null, null, false, false);

            result.UnmappedFiles.Should().HaveCount(2);
            Mocker.GetMock<IFingerprintLookupClient>().Verify(
                s => s.Lookup(It.Is<IReadOnlyCollection<FileFingerprint>>(f => f.Count == 2 && f.Select(x => x.Key).SequenceEqual(files)), false),
                Times.Once());
            Mocker.GetMock<IFingerprintLookupClient>().Verify(s => s.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), It.IsAny<bool>()), Times.Once());
        }

        [TestCase(MatchTier.Unknown)]
        [TestCase(MatchTier.Name)]
        [TestCase(MatchTier.Confirmed)]
        public void should_leave_unparsed_scene_file_unmapped_for_other_tiers(MatchTier tier)
        {
            var file = @"C:\Test\Unsorted\Unknown.Studio.24.01.02.Unknown.Scene.mp4".AsOsAgnostic();
            var lookup = new FingerprintLookupResult { Status = FingerprintLookupStatus.Matched };

            GivenUnparsedSceneFile(file, "0123456789abcdef");
            GivenUpstreamLookup(new Dictionary<string, FingerprintLookupResult> { { file, lookup } });
            Mocker.GetMock<IFileMatchEvaluator>()
                  .Setup(s => s.Evaluate(It.IsAny<Movie>(), It.IsAny<FingerprintLookupResult>(), It.IsAny<double?>()))
                  .Returns(new FileMatchOutcome { Tier = tier, NeedsReview = true });

            var result = Subject.GetSceneImportDecisions(new List<string> { file }, null, null, false, false);

            result.Decisions.Should().BeEmpty();
            result.MoviesToAdd.Should().BeEmpty();
            result.UnmappedFiles.Should().ContainSingle().Which.OsHash.Should().Be("0123456789abcdef");
        }

        [Test]
        public void should_leave_unparsed_scene_file_unmapped_when_hash_target_is_not_in_the_library()
        {
            var file = @"C:\Test\Unsorted\Unknown.Studio.24.01.02.Unknown.Scene.mp4".AsOsAgnostic();
            var lookup = new FingerprintLookupResult { Status = FingerprintLookupStatus.Matched };

            GivenUnparsedSceneFile(file, "0123456789abcdef");
            GivenUpstreamLookup(new Dictionary<string, FingerprintLookupResult> { { file, lookup } });
            Mocker.GetMock<IFileMatchEvaluator>()
                  .Setup(s => s.Evaluate(It.IsAny<Movie>(), It.IsAny<FingerprintLookupResult>(), It.IsAny<double?>()))
                  .Returns(new FileMatchOutcome { Tier = MatchTier.Hash, Target = new Movie { Id = 0 } });

            var result = Subject.GetSceneImportDecisions(new List<string> { file }, null, null, false, false);

            result.Decisions.Should().BeEmpty();
            result.MoviesToAdd.Should().BeEmpty();
            result.UnmappedFiles.Should().ContainSingle();
        }

        [Test]
        public void should_leave_unparsed_scene_files_unmapped_when_upstream_lookup_throws()
        {
            var file = @"C:\Test\Unsorted\Unknown.Studio.24.01.02.Unknown.Scene.mp4".AsOsAgnostic();

            GivenUnparsedSceneFile(file, "0123456789abcdef");
            Mocker.GetMock<IFingerprintLookupClient>()
                  .Setup(s => s.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), It.IsAny<bool>()))
                  .Throws(new InvalidOperationException("boom"));

            var result = Subject.GetSceneImportDecisions(new List<string> { file }, null, null, false, false);

            result.Decisions.Should().BeEmpty();
            result.UnmappedFiles.Should().ContainSingle().Which.OsHash.Should().Be("0123456789abcdef");
        }

        [Test]
        public void should_leave_unparsed_scene_files_unmapped_when_evaluator_throws()
        {
            var file = @"C:\Test\Unsorted\Unknown.Studio.24.01.02.Unknown.Scene.mp4".AsOsAgnostic();

            GivenUnparsedSceneFile(file, "0123456789abcdef");
            GivenUpstreamLookup(new Dictionary<string, FingerprintLookupResult> { { file, new FingerprintLookupResult { Status = FingerprintLookupStatus.Matched } } });
            Mocker.GetMock<IFileMatchEvaluator>()
                  .Setup(s => s.Evaluate(It.IsAny<Movie>(), It.IsAny<FingerprintLookupResult>(), It.IsAny<double?>()))
                  .Throws(new InvalidOperationException("boom"));

            var result = Subject.GetSceneImportDecisions(new List<string> { file }, null, null, false, false);

            result.Decisions.Should().BeEmpty();
            result.UnmappedFiles.Should().ContainSingle();
        }

        [Test]
        public void should_not_look_up_when_no_scene_file_could_be_hashed()
        {
            var file = @"C:\Test\Unsorted\Unknown.Studio.24.01.02.Unknown.Scene.mp4".AsOsAgnostic();

            GivenUnparsedSceneFile(file, null);

            var result = Subject.GetSceneImportDecisions(new List<string> { file }, null, null, false, false);

            result.UnmappedFiles.Should().ContainSingle();
            Mocker.GetMock<IFingerprintLookupClient>().Verify(s => s.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), It.IsAny<bool>()), Times.Never());
        }

        private class ThrowOnceLazyMetadata : LazyLoaded<MovieMetadata>
        {
            private readonly int _throwOnAccess;
            private int _accesses;

            public ThrowOnceLazyMetadata(MovieMetadata value, int throwOnAccess)
                : base(value)
            {
                _throwOnAccess = throwOnAccess;
            }

            public override void LazyLoad()
            {
                if (++_accesses == _throwOnAccess)
                {
                    throw new InvalidOperationException("lazy load failed");
                }
            }
        }

        [Test]
        public void should_still_make_decisions_when_the_lookup_phase_throws()
        {
            var dbMovie = GivenSceneInDatabase();

            // First access is the sample count, the second is the fingerprint lookup setup
            _movie.MovieMetadata = new ThrowOnceLazyMetadata(_movie.MovieMetadata.Value, 2);

            var result = Subject.GetImportDecisions(_videoFiles, _movie);

            result.Should().ContainSingle().Which.LocalMovie.Movie.Should().BeSameAs(dbMovie);
            Mocker.GetMock<IFingerprintLookupClient>().Verify(s => s.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_call_all_specifications()
        {
            var downloadClientItem = Builder<DownloadClientItem>.CreateNew().Build();
            GivenAugmentationSuccess();
            GivenSpecifications(_pass1, _pass2, _pass3, _fail1, _fail2, _fail3);

            Subject.GetImportDecisions(_videoFiles, _movie, downloadClientItem, null, false, true);

            _fail1.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalMovie>(), downloadClientItem), Times.Once());
            _fail2.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalMovie>(), downloadClientItem), Times.Once());
            _fail3.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalMovie>(), downloadClientItem), Times.Once());
            _pass1.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalMovie>(), downloadClientItem), Times.Once());
            _pass2.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalMovie>(), downloadClientItem), Times.Once());
            _pass3.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalMovie>(), downloadClientItem), Times.Once());
        }

        [Test]
        public void should_switch_scene_to_movie_from_database()
        {
            _movie.MovieMetadata.Value.ItemType = ItemType.Scene;

            var dbMovie = Builder<Movie>.CreateNew()
                                        .With(e => e.Id = _movie.Id)
                                        .With(e => e.Path = _movie.Path)
                                        .With(e => e.QualityProfile = new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() })
                                        .Build();
            dbMovie.MovieMetadata.Value.ItemType = ItemType.Scene;

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetMovie(_movie.Id))
                  .Returns(dbMovie);

            GivenSpecifications(_pass1);

            var result = Subject.GetImportDecisions(_videoFiles, _movie);

            result.Single().Approved.Should().BeTrue();
            result.Single().LocalMovie.Movie.Should().BeSameAs(dbMovie);
        }

        private Movie GivenSceneInDatabase()
        {
            _movie.MovieMetadata.Value.ItemType = ItemType.Scene;

            var dbMovie = Builder<Movie>.CreateNew()
                                        .With(e => e.Id = _movie.Id)
                                        .With(e => e.Path = _movie.Path)
                                        .With(e => e.QualityProfile = new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() })
                                        .Build();
            dbMovie.MovieMetadata.Value.ItemType = ItemType.Scene;

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetMovie(_movie.Id))
                  .Returns(dbMovie);

            GivenSpecifications(_pass1);

            return dbMovie;
        }

        private void GivenLookup(string file, FingerprintLookupResult result)
        {
            Mocker.GetMock<IVideoHashService>().Setup(s => s.ComputeOsHash(file)).Returns("0123456789abcdef");

            Mocker.GetMock<IFingerprintLookupClient>()
                  .Setup(s => s.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), true))
                  .Returns(new Dictionary<string, FingerprintLookupResult> { { file, result } });
        }

        private void GivenOutcome(MatchTier tier, bool conflict = false, params FingerprintCandidate[] candidates)
        {
            Mocker.GetMock<IFileMatchEvaluator>()
                  .Setup(s => s.Evaluate(It.IsAny<Movie>(), It.IsAny<FingerprintLookupResult>(), It.IsAny<double?>()))
                  .Returns(new FileMatchOutcome
                  {
                      Tier = tier,
                      Conflict = conflict,
                      NeedsReview = conflict,
                      Candidates = candidates.ToList()
                  });
        }

        [Test]
        public void should_hash_each_scene_file_once_and_look_up_the_batch_in_one_local_only_call()
        {
            GivenSceneInDatabase();

            var files = new List<string>
            {
                @"C:\Test\Unsorted\One.mp4".AsOsAgnostic(),
                @"C:\Test\Unsorted\Two.mp4".AsOsAgnostic()
            };

            GivenVideoFiles(files);

            Mocker.GetMock<IVideoHashService>().Setup(s => s.ComputeOsHash(It.IsAny<string>())).Returns("0123456789abcdef");
            Mocker.GetMock<IFingerprintLookupClient>()
                  .Setup(s => s.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), true))
                  .Returns(new Dictionary<string, FingerprintLookupResult>());
            GivenOutcome(MatchTier.Name);

            var decisions = Subject.GetImportDecisions(_videoFiles, _movie);

            Mocker.GetMock<IFingerprintLookupClient>().Verify(
                s => s.Lookup(It.Is<IReadOnlyCollection<FileFingerprint>>(f => f.Count == 2 && f.Select(x => x.Key).SequenceEqual(files)), true),
                Times.Once());
            Mocker.GetMock<IFingerprintLookupClient>().Verify(s => s.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), It.IsAny<bool>()), Times.Once());
            Mocker.GetMock<IVideoHashService>().Verify(s => s.ComputeOsHash(files[0]), Times.Once());
            Mocker.GetMock<IVideoHashService>().Verify(s => s.ComputeOsHash(files[1]), Times.Once());

            decisions.Should().OnlyContain(d => d.LocalMovie.OsHash == "0123456789abcdef");
        }

        [Test]
        public void should_accept_and_record_confirmed_tier_when_hash_agrees()
        {
            GivenSceneInDatabase();
            GivenLookup(_videoFiles[0], new FingerprintLookupResult { Status = FingerprintLookupStatus.Matched });
            GivenOutcome(MatchTier.Confirmed);

            var result = Subject.GetImportDecisions(_videoFiles, _movie).Single();

            result.Approved.Should().BeTrue();
            result.LocalMovie.MatchTier.Should().Be(MatchTier.Confirmed);
        }

        [TestCase(FingerprintLookupStatus.NotFound)]
        [TestCase(FingerprintLookupStatus.TryLater)]
        public void should_accept_as_name_tier_without_hash_data(FingerprintLookupStatus status)
        {
            GivenSceneInDatabase();
            GivenLookup(_videoFiles[0], new FingerprintLookupResult { Status = status });
            Mocker.GetMock<IFileMatchEvaluator>()
                  .Setup(s => s.Evaluate(It.IsAny<Movie>(), It.Is<FingerprintLookupResult>(l => l.Status == status), It.IsAny<double?>()))
                  .Returns(new FileMatchOutcome { Tier = MatchTier.Name });

            var result = Subject.GetImportDecisions(_videoFiles, _movie).Single();

            result.Approved.Should().BeTrue();
            result.LocalMovie.MatchTier.Should().Be(MatchTier.Name);
        }

        [Test]
        public void should_accept_as_name_tier_when_client_returns_nothing()
        {
            GivenSceneInDatabase();
            Mocker.GetMock<IVideoHashService>().Setup(s => s.ComputeOsHash(It.IsAny<string>())).Returns("0123456789abcdef");
            Mocker.GetMock<IFingerprintLookupClient>()
                  .Setup(s => s.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), true))
                  .Returns(new Dictionary<string, FingerprintLookupResult>());
            Mocker.GetMock<IFileMatchEvaluator>()
                  .Setup(s => s.Evaluate(It.IsAny<Movie>(), null, It.IsAny<double?>()))
                  .Returns(new FileMatchOutcome { Tier = MatchTier.Name });

            var result = Subject.GetImportDecisions(_videoFiles, _movie).Single();

            result.Approved.Should().BeTrue();
            result.LocalMovie.MatchTier.Should().Be(MatchTier.Name);
        }

        [Test]
        public void should_accept_when_lookup_client_throws()
        {
            GivenSceneInDatabase();
            Mocker.GetMock<IVideoHashService>().Setup(s => s.ComputeOsHash(It.IsAny<string>())).Returns("0123456789abcdef");
            Mocker.GetMock<IFingerprintLookupClient>()
                  .Setup(s => s.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), It.IsAny<bool>()))
                  .Throws(new InvalidOperationException("boom"));
            GivenOutcome(MatchTier.Name);

            var result = Subject.GetImportDecisions(_videoFiles, _movie).Single();

            result.Approved.Should().BeTrue();
        }

        [Test]
        public void should_accept_as_name_tier_when_evaluator_throws()
        {
            GivenSceneInDatabase();
            GivenLookup(_videoFiles[0], new FingerprintLookupResult { Status = FingerprintLookupStatus.Matched });
            Mocker.GetMock<IFileMatchEvaluator>()
                  .Setup(s => s.Evaluate(It.IsAny<Movie>(), It.IsAny<FingerprintLookupResult>(), It.IsAny<double?>()))
                  .Throws(new InvalidOperationException("boom"));

            var result = Subject.GetImportDecisions(_videoFiles, _movie).Single();

            result.Approved.Should().BeTrue();
            result.LocalMovie.MatchTier.Should().Be(MatchTier.Name);
        }

        [Test]
        public void should_reject_a_fingerprint_conflict_naming_the_other_scene()
        {
            GivenSceneInDatabase();
            GivenLookup(_videoFiles[0], new FingerprintLookupResult { Status = FingerprintLookupStatus.Matched });
            GivenOutcome(MatchTier.Conflict, true, new FingerprintCandidate { SceneId = "scene-b", Title = "Other Scene" });

            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString("FingerprintConflictRejection", It.Is<Dictionary<string, object>>(t => (string)t["title"] == "Other Scene")))
                  .Returns("Fingerprint matches a different scene: Other Scene");

            var result = Subject.GetImportDecisions(_videoFiles, _movie).Single();

            result.Approved.Should().BeFalse();
            result.Rejections.Should().ContainSingle(r => r.Reason == ImportRejectionReason.FingerprintConflict && r.Message == "Fingerprint matches a different scene: Other Scene");
            result.LocalMovie.FingerprintCandidates.Should().ContainSingle().Which.SceneId.Should().Be("scene-b");
        }

        [Test]
        public void should_name_the_other_scene_by_id_when_its_title_is_unknown()
        {
            GivenSceneInDatabase();
            GivenLookup(_videoFiles[0], new FingerprintLookupResult { Status = FingerprintLookupStatus.Matched });
            GivenOutcome(MatchTier.Conflict, true, new FingerprintCandidate { SceneId = "scene-b" });

            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString("FingerprintConflictRejection", It.Is<Dictionary<string, object>>(t => (string)t["title"] == "scene-b")))
                  .Returns("Fingerprint matches a different scene: scene-b");

            var result = Subject.GetImportDecisions(_videoFiles, _movie).Single();

            result.Rejections.Should().ContainSingle(r => r.Reason == ImportRejectionReason.FingerprintConflict && r.Message.EndsWith("scene-b"));
        }

        [Test]
        public void should_keep_spec_rejections_alongside_a_fingerprint_conflict()
        {
            GivenSceneInDatabase();
            GivenSpecifications(_fail1);
            GivenLookup(_videoFiles[0], new FingerprintLookupResult { Status = FingerprintLookupStatus.Matched });
            GivenOutcome(MatchTier.Conflict, true, new FingerprintCandidate { SceneId = "scene-b" });

            var result = Subject.GetImportDecisions(_videoFiles, _movie).Single();

            result.Rejections.Should().HaveCount(2);
        }

        [Test]
        public void should_pass_the_file_duration_to_the_evaluator()
        {
            GivenSceneInDatabase();
            GivenLookup(_videoFiles[0], new FingerprintLookupResult { Status = FingerprintLookupStatus.Matched });
            GivenOutcome(MatchTier.Confirmed);

            Mocker.GetMock<IAggregationService>()
                  .Setup(s => s.Augment(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>()))
                  .Callback<LocalMovie, DownloadClientItem>((localMovie, _) => localMovie.MediaInfo = new NzbDrone.Core.MediaFiles.MediaInfo.MediaInfoModel { RunTime = TimeSpan.FromSeconds(944) });

            Subject.GetImportDecisions(_videoFiles, _movie);

            Mocker.GetMock<IFileMatchEvaluator>().Verify(s => s.Evaluate(It.IsAny<Movie>(), It.IsAny<FingerprintLookupResult>(), 944d), Times.Once());
        }

        [Test]
        public void should_pass_no_duration_when_media_info_is_missing()
        {
            GivenSceneInDatabase();
            GivenLookup(_videoFiles[0], new FingerprintLookupResult { Status = FingerprintLookupStatus.Matched });
            GivenOutcome(MatchTier.Confirmed);

            Subject.GetImportDecisions(_videoFiles, _movie);

            Mocker.GetMock<IFileMatchEvaluator>().Verify(s => s.Evaluate(It.IsAny<Movie>(), It.IsAny<FingerprintLookupResult>(), null), Times.Once());
        }

        [Test]
        public void should_skip_fingerprinting_for_movies()
        {
            GivenAugmentationSuccess();
            GivenSpecifications(_pass1);

            _movie.MovieMetadata.Value.ItemType = ItemType.Movie;

            var result = Subject.GetImportDecisions(_videoFiles, _movie).Single();

            result.Approved.Should().BeTrue();
            result.LocalMovie.MatchTier.Should().Be(MatchTier.Unknown);
            Mocker.GetMock<IVideoHashService>().Verify(s => s.ComputeOsHash(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IFingerprintLookupClient>().Verify(s => s.Lookup(It.IsAny<IReadOnlyCollection<FileFingerprint>>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IFileMatchEvaluator>().Verify(s => s.Evaluate(It.IsAny<Movie>(), It.IsAny<FingerprintLookupResult>(), It.IsAny<double?>()), Times.Never());
        }

        [Test]
        public void should_return_rejected_if_single_specs_fail()
        {
            GivenSpecifications(_fail1);

            var result = Subject.GetImportDecisions(_videoFiles, _movie);

            result.Single().Approved.Should().BeFalse();
        }

        [Test]
        public void should_return_rejected_if_one_of_specs_fail()
        {
            GivenSpecifications(_pass1, _fail1, _pass2, _pass3);

            var result = Subject.GetImportDecisions(_videoFiles, _movie);

            result.Single().Approved.Should().BeFalse();
        }

        [Test]
        public void should_return_approved_if_all_specs_pass()
        {
            GivenAugmentationSuccess();
            GivenSpecifications(_pass1, _pass2, _pass3);

            var result = Subject.GetImportDecisions(_videoFiles, _movie);

            result.Single().Approved.Should().BeTrue();
        }

        [Test]
        public void should_have_same_number_of_rejections_as_specs_that_failed()
        {
            GivenAugmentationSuccess();
            GivenSpecifications(_pass1, _pass2, _pass3, _fail1, _fail2, _fail3);

            var result = Subject.GetImportDecisions(_videoFiles, _movie);
            result.Single().Rejections.Should().HaveCount(3);
        }

        [Test]
        public void should_not_blowup_the_process_due_to_failed_parse()
        {
            GivenSpecifications(_pass1);

            Mocker.GetMock<IAggregationService>()
                  .Setup(c => c.Augment(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>()))
                  .Throws<TestException>();

            _videoFiles = new List<string>
                {
                    "The.Movie.2021.DVDRip.XviD-OSiTV",
                    "The.Movie.2021.DVDRip.XviD-OSiTV",
                    "The.Movie.2021.DVDRip.XviD-OSiTV"
                };

            GivenVideoFiles(_videoFiles);

            Subject.GetImportDecisions(_videoFiles, _movie);

            Mocker.GetMock<IAggregationService>()
                  .Verify(c => c.Augment(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>()), Times.Exactly(_videoFiles.Count));

            ExceptionVerification.ExpectedErrors(3);
        }

        [Test]
        public void should_call_parsing_service_with_filename_as_simpletitle()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);

            Mocker.GetMock<IParsingService>()
                .Setup(c => c.ParseMinimalPathMovieInfo(It.IsAny<string>()))
                .Returns<ParsedMovieInfo>(null);

            var folderInfo = new ParsedMovieInfo { SimpleReleaseTitle = "A Movie Folder 2018", Quality = _quality };

            var result = Subject.GetImportDecisions(_videoFiles, _movie, null, folderInfo, true);

            var fileNames = _videoFiles.Select(System.IO.Path.GetFileName);

            Mocker.GetMock<IAggregationService>()
                  .Setup(c => c.Augment(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>()))
                  .Throws<TestException>();
        }

        [Test]
        public void should_return_a_decision_when_exception_is_caught()
        {
            Mocker.GetMock<IAggregationService>()
                  .Setup(c => c.Augment(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>()))
                  .Throws<TestException>();

            _videoFiles = new List<string>
                {
                    "The.Movie.2021.DVDRip.XviD-OSiTV"
                };

            GivenVideoFiles(_videoFiles);

            Subject.GetImportDecisions(_videoFiles, _movie).Should().HaveCount(1);

            ExceptionVerification.ExpectedErrors(1);
        }
    }
}
