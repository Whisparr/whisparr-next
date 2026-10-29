using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration.Setup
{
    [TestFixture]
    public class MixedMovieResolverFixture : DbTest
    {
        private const int Tmdb = 0;
        private const int TpdbMovie = 2;
        private const int MovieItem = 0;
        private const int SceneItem = 1;

        private IDbConnection _connection;

        [SetUp]
        public void Setup()
        {
            _connection = Mocker.Resolve<IMainDatabase>().OpenConnection();

            // Ids 1 and 2, in this order
            _connection.Execute("INSERT INTO \"RootFolders\" (\"Path\") VALUES ('/library')");
            _connection.Execute("INSERT INTO \"RootFolders\" (\"Path\") VALUES ('/library/movies')");
        }

        [TearDown]
        public void Teardown()
        {
            _connection?.Dispose();
        }

        private MixedMovieResult Resolve(MovieMetadataType chosen)
        {
            using var transaction = _connection.BeginTransaction();

            var result = new MixedMovieResolver().Resolve(_connection, transaction, chosen);

            transaction.Commit();

            return result;
        }

        private void AddMovie(int id, int source, int itemType, int tmdbId, string title, int year, bool monitored = true, int qualityProfileId = 1, string tags = "[]")
        {
            // PostgreSQL ids are GENERATED ALWAYS, so ids are assigned in call order: id must be the call's position.
            var metadataId = _connection.ExecuteScalar<int>(
                "INSERT INTO \"MovieMetadata\" (\"ForeignId\", \"MetadataSource\", \"ImdbId\", \"TmdbId\", \"Images\", \"Title\", \"OriginalLanguage\", \"Status\", \"Runtime\", \"Year\", \"Recommendations\", \"ItemType\") " +
                "VALUES (@foreignId, @source, @imdbId, @tmdbId, '[]', @title, 1, 0, 0, @year, '[]', @itemType) RETURNING \"Id\"",
                new { foreignId = $"fid-{id}", source, imdbId = $"tt{id}", tmdbId, title, year, itemType });

            metadataId.Should().Be(id);

            _connection.Execute(
                "INSERT INTO \"Movies\" (\"Path\", \"Monitored\", \"QualityProfileId\", \"Tags\", \"MovieFileId\", \"MovieMetadataId\") " +
                "VALUES (@path, @monitored, @qualityProfileId, @tags, 0, @id)",
                new { id, path = $"/library/movies/{title} ({year})", monitored, qualityProfileId, tags });

            _connection.Execute(
                "INSERT INTO \"Credits\" (\"MovieMetadataId\", \"PerformerForeignId\", \"Type\", \"Order\") VALUES (@id, 'p1', 0, 0)",
                new { id });

            _connection.Execute(
                "INSERT INTO \"AlternativeTitles\" (\"Title\", \"CleanTitle\", \"SourceType\", \"MovieMetadataId\") VALUES (@title, @clean, 0, @id)",
                new { id, title = $"Alt {id}", clean = $"alt{id}" });
        }

        private void AddFile(int movieId, string relativePath, string osHash)
        {
            var fileId = _connection.ExecuteScalar<int>(
                "INSERT INTO \"MovieFiles\" (\"MovieId\", \"Quality\", \"Size\", \"DateAdded\", \"RelativePath\", \"OsHash\", \"PHash\", \"PHashAttemptedAt\", \"HashedFileModifiedAt\") " +
                "VALUES (@movieId, '{}', 100, @now, @relativePath, @osHash, @pHash, @now, @now) RETURNING \"Id\"",
                new { movieId, relativePath, osHash, pHash = $"ph-{osHash}", now = DateTime.UtcNow });

            _connection.Execute("UPDATE \"Movies\" SET \"MovieFileId\" = @fileId WHERE \"Id\" = @movieId", new { movieId, fileId });
        }

        private int Count(string table)
        {
            return _connection.ExecuteScalar<int>($"SELECT COUNT(*) FROM \"{table}\"");
        }

        private List<int> Ids(string table)
        {
            return _connection.Query<int>($"SELECT \"Id\" FROM \"{table}\" ORDER BY \"Id\"").ToList();
        }

        [Test]
        public void should_keep_rows_matching_chosen_source()
        {
            AddMovie(1, Tmdb, MovieItem, 100, "Kept", 2020);
            AddFile(1, "kept.mp4", "aa");

            var result = Resolve(MovieMetadataType.TMDB);

            result.Kept.Should().Be(1);
            result.Merged.Should().Be(0);
            result.Detached.Should().Be(0);
            Ids("Movies").Should().Equal(1);
            Ids("MovieMetadata").Should().Equal(1);
            Count("MovieFiles").Should().Be(1);
            Count("ImportFiles").Should().Be(0);
            Count("LegacyMovieSeeds").Should().Be(0);
        }

        [Test]
        public void should_choose_tpdb_movie_source_when_tpdb_chosen()
        {
            AddMovie(1, TpdbMovie, MovieItem, 0, "Kept", 2020);
            AddMovie(2, Tmdb, MovieItem, 0, "Other", 2021);

            var result = Resolve(MovieMetadataType.TPDB);

            result.Kept.Should().Be(1);
            Ids("Movies").Should().Equal(1);
        }

        [Test]
        public void should_merge_twin_file_onto_kept_row_without_file()
        {
            AddMovie(1, Tmdb, MovieItem, 100, "Twin", 2020);
            AddMovie(2, TpdbMovie, MovieItem, 100, "Twin", 2020);
            AddFile(2, "twin.mp4", "bb");

            var result = Resolve(MovieMetadataType.TMDB);

            result.Merged.Should().Be(1);
            result.Detached.Should().Be(0);

            Ids("Movies").Should().Equal(1);
            Ids("MovieMetadata").Should().Equal(1);
            Count("Credits").Should().Be(1);
            Count("AlternativeTitles").Should().Be(1);
            Count("ImportFiles").Should().Be(0);
            Count("LegacyMovieSeeds").Should().Be(0);

            var file = _connection.QuerySingle<(int Id, int MovieId)>("SELECT \"Id\", \"MovieId\" FROM \"MovieFiles\"");
            file.MovieId.Should().Be(1);
            _connection.ExecuteScalar<int>("SELECT \"MovieFileId\" FROM \"Movies\" WHERE \"Id\" = 1").Should().Be(file.Id);
        }

        [Test]
        public void should_detach_twin_file_when_kept_row_has_file()
        {
            AddMovie(1, Tmdb, MovieItem, 100, "Twin", 2020);
            AddFile(1, "kept.mp4", "aa");
            AddMovie(2, TpdbMovie, MovieItem, 100, "Twin", 2020);
            AddFile(2, "twin.mp4", "bb");

            var result = Resolve(MovieMetadataType.TMDB);

            result.Merged.Should().Be(0);
            result.Detached.Should().Be(1);

            Ids("Movies").Should().Equal(1);
            _connection.QuerySingle<int>("SELECT \"MovieId\" FROM \"MovieFiles\"").Should().Be(1);

            var import = _connection.QuerySingle<ImportRow>("SELECT * FROM \"ImportFiles\"");
            import.OsHash.Should().Be("bb");
            import.RelativePath.Should().Be("Twin (2020)/twin.mp4");
            Count("LegacyMovieSeeds").Should().Be(0);
        }

        [Test]
        public void should_detach_second_twin_file_when_first_twin_already_moved_onto_kept_row()
        {
            AddMovie(1, Tmdb, MovieItem, 100, "Kept", 2020);
            AddMovie(2, TpdbMovie, MovieItem, 100, "First Twin", 2020);
            AddFile(2, "first.mp4", "f1");
            _connection.Execute("UPDATE \"Movies\" SET \"Path\" = '/library/movies/Kept (2020)' WHERE \"Id\" = 2");
            AddMovie(3, TpdbMovie, MovieItem, 100, "Second Twin", 2020);
            AddFile(3, "second.mp4", "f2");

            var result = Resolve(MovieMetadataType.TMDB);

            result.Merged.Should().Be(1);
            result.Detached.Should().Be(1);
            result.Skipped.Should().Be(0);

            Ids("Movies").Should().Equal(1);
            Ids("MovieMetadata").Should().Equal(1);
            Count("LegacyMovieSeeds").Should().Be(0);

            var file = _connection.QuerySingle<(int Id, int MovieId, string OsHash)>("SELECT \"Id\", \"MovieId\", \"OsHash\" FROM \"MovieFiles\"");
            file.MovieId.Should().Be(1);
            file.OsHash.Should().Be("f1");
            _connection.ExecuteScalar<int>("SELECT \"MovieFileId\" FROM \"Movies\" WHERE \"Id\" = 1").Should().Be(file.Id);

            _connection.QuerySingle<string>("SELECT \"OsHash\" FROM \"ImportFiles\"").Should().Be("f2");
        }

        [Test]
        public void should_skip_movie_outside_any_root_folder()
        {
            AddMovie(1, Tmdb, MovieItem, 100, "Kept", 2020);
            AddFile(1, "kept.mp4", "aa");
            AddMovie(2, TpdbMovie, MovieItem, 555, "Outside", 2019);
            AddFile(2, "outside.mp4", "bb");
            AddMovie(3, TpdbMovie, MovieItem, 100, "Outside Twin", 2020);
            AddFile(3, "twin.mp4", "cc");
            _connection.Execute("UPDATE \"Movies\" SET \"Path\" = '/elsewhere/Outside (2019)' WHERE \"Id\" = 2");
            _connection.Execute("UPDATE \"Movies\" SET \"Path\" = '/elsewhere/Outside Twin (2020)' WHERE \"Id\" = 3");

            var result = Resolve(MovieMetadataType.TMDB);

            result.Kept.Should().Be(1);
            result.Merged.Should().Be(0);
            result.Detached.Should().Be(0);
            result.Skipped.Should().Be(2);
            result.SkippedPaths.Should().BeEquivalentTo("/elsewhere/Outside (2019)", "/elsewhere/Outside Twin (2020)");

            Ids("Movies").Should().Equal(1, 2, 3);
            Ids("MovieMetadata").Should().Equal(1, 2, 3);
            Count("MovieFiles").Should().Be(3);
            Count("Credits").Should().Be(3);
            Count("AlternativeTitles").Should().Be(3);
            Count("ImportFiles").Should().Be(0);
            Count("LegacyMovieSeeds").Should().Be(0);
            _connection.ExecuteScalar<int>("SELECT COUNT(*) FROM \"Movies\" WHERE \"MovieFileId\" > 0").Should().Be(3);
        }

        [Test]
        public void should_detach_and_seed_row_without_twin()
        {
            AddMovie(1, Tmdb, MovieItem, 100, "Kept", 2020);
            AddMovie(2, TpdbMovie, MovieItem, 555, "Lonely", 2019, monitored: true, qualityProfileId: 7, tags: "[3,4]");
            AddFile(2, "lonely.mp4", "cc");

            var result = Resolve(MovieMetadataType.TMDB);

            result.Kept.Should().Be(1);
            result.Merged.Should().Be(0);
            result.Detached.Should().Be(1);

            Ids("Movies").Should().Equal(1);
            Ids("MovieMetadata").Should().Equal(1);
            Count("MovieFiles").Should().Be(0);
            Count("Credits").Should().Be(1);
            Count("AlternativeTitles").Should().Be(1);

            var import = _connection.QuerySingle<ImportRow>("SELECT * FROM \"ImportFiles\"");
            import.RootFolderId.Should().Be(2);
            import.Path.Should().Be("/library/movies/Lonely (2019)/lonely.mp4");
            import.RelativePath.Should().Be("Lonely (2019)/lonely.mp4");
            import.Name.Should().Be("lonely");
            import.OsHash.Should().Be("cc");
            import.PHash.Should().Be("ph-cc");
            import.PHashAttemptedAt.Should().NotBeNull();
            import.HashedFileModifiedAt.Should().NotBeNull();

            var seed = _connection.QuerySingle<SeedRow>("SELECT * FROM \"LegacyMovieSeeds\"");
            seed.LegacyForeignId.Should().Be("fid-2");
            seed.Title.Should().Be("Lonely");
            seed.Year.Should().Be(2019);
            seed.TmdbId.Should().Be(555);
            seed.ImdbId.Should().Be("tt2");
            seed.Monitored.Should().BeTrue();
            seed.QualityProfileId.Should().Be(7);
            seed.RootFolderPath.Should().Be("/library/movies");
            seed.Tags.Should().Be("[3,4]");
            seed.ImportFileId.Should().Be(import.Id);
            seed.Resolved.Should().BeFalse();
        }

        [Test]
        public void should_not_merge_on_title_alone()
        {
            AddMovie(1, Tmdb, MovieItem, 100, "Same Title", 2020);
            AddMovie(2, TpdbMovie, MovieItem, 200, "Same Title", 2020);
            AddFile(2, "same.mp4", "dd");

            var result = Resolve(MovieMetadataType.TMDB);

            result.Merged.Should().Be(0);
            result.Detached.Should().Be(1);
            Ids("Movies").Should().Equal(1);
            Count("ImportFiles").Should().Be(1);
            Count("LegacyMovieSeeds").Should().Be(1);
            _connection.ExecuteScalar<int>("SELECT \"MovieFileId\" FROM \"Movies\" WHERE \"Id\" = 1").Should().Be(0);
        }

        [Test]
        public void should_seed_row_without_file_with_no_import_file()
        {
            AddMovie(1, Tmdb, MovieItem, 100, "Kept", 2020);
            AddMovie(2, TpdbMovie, MovieItem, 200, "Fileless", 2018);

            var result = Resolve(MovieMetadataType.TMDB);

            result.Detached.Should().Be(0);
            result.Seeded.Should().Be(1);
            Count("ImportFiles").Should().Be(0);

            var seed = _connection.QuerySingle<SeedRow>("SELECT * FROM \"LegacyMovieSeeds\"");
            seed.ImportFileId.Should().BeNull();
            seed.Title.Should().Be("Fileless");
        }

        [Test]
        public void should_count_a_seed_with_a_file_as_seeded_and_detached()
        {
            AddMovie(1, Tmdb, MovieItem, 100, "Kept", 2020);
            AddMovie(2, TpdbMovie, MovieItem, 200, "Lonely", 2018);
            AddFile(2, "lonely.mp4", "ff");

            var result = Resolve(MovieMetadataType.TMDB);

            result.Seeded.Should().Be(1);
            result.Detached.Should().Be(1);
        }

        [Test]
        public void should_not_count_a_twin_as_seeded()
        {
            AddMovie(1, Tmdb, MovieItem, 100, "Twin", 2020);
            AddMovie(2, TpdbMovie, MovieItem, 100, "Twin", 2020);

            var result = Resolve(MovieMetadataType.TMDB);

            result.Seeded.Should().Be(0);
            result.Merged.Should().Be(1);
        }

        [Test]
        public void should_detach_a_twin_file_that_lives_outside_the_kept_rows_folder()
        {
            AddMovie(1, Tmdb, MovieItem, 100, "Kept", 2020);
            AddMovie(2, TpdbMovie, MovieItem, 100, "Other", 2020);
            AddFile(2, "other.mp4", "gg");

            var result = Resolve(MovieMetadataType.TMDB);

            result.Merged.Should().Be(0);
            result.Detached.Should().Be(1);
            Count("MovieFiles").Should().Be(0);
            _connection.ExecuteScalar<int>("SELECT \"MovieFileId\" FROM \"Movies\" WHERE \"Id\" = 1").Should().Be(0);

            var import = _connection.QuerySingle<ImportRow>("SELECT * FROM \"ImportFiles\"");
            import.Path.Should().Be("/library/movies/Other (2020)/other.mp4");
            import.OsHash.Should().Be("gg");
            Count("LegacyMovieSeeds").Should().Be(0);
        }

        [Test]
        public void should_rewrite_the_relative_path_of_a_twin_file_inside_the_kept_rows_folder()
        {
            AddMovie(1, Tmdb, MovieItem, 100, "Twin", 2020);
            AddMovie(2, TpdbMovie, MovieItem, 100, "Twin", 2020);
            _connection.Execute("UPDATE \"Movies\" SET \"Path\" = '/library/movies/Twin (2020)/Extras' WHERE \"Id\" = 2");
            AddFile(2, "twin.mp4", "hh");

            var result = Resolve(MovieMetadataType.TMDB);

            result.Merged.Should().Be(1);
            result.Detached.Should().Be(0);
            _connection.QuerySingle<string>("SELECT \"RelativePath\" FROM \"MovieFiles\"").Should().Be("Extras/twin.mp4");
        }

        [Test]
        public void should_not_touch_scenes()
        {
            AddMovie(1, Tmdb, MovieItem, 100, "Kept", 2020);
            AddMovie(2, 1, SceneItem, 0, "Scene", 2021);
            AddFile(2, "scene.mp4", "ee");
            AddMovie(3, TpdbMovie, SceneItem, 100, "Tpdb Scene", 2021);

            var result = Resolve(MovieMetadataType.TMDB);

            result.Kept.Should().Be(1);
            result.Merged.Should().Be(0);
            result.Detached.Should().Be(0);
            Ids("Movies").Should().Equal(1, 2, 3);
            Ids("MovieMetadata").Should().Equal(1, 2, 3);
            _connection.QuerySingle<int>("SELECT \"MovieId\" FROM \"MovieFiles\"").Should().Be(2);
            Count("ImportFiles").Should().Be(0);
            Count("LegacyMovieSeeds").Should().Be(0);
        }

        [Test]
        public void should_be_noop_without_mixed_movies()
        {
            var result = Resolve(MovieMetadataType.TPDB);

            result.Kept.Should().Be(0);
            result.Merged.Should().Be(0);
            result.Detached.Should().Be(0);
            Count("Movies").Should().Be(0);
        }

        [Test]
        public void should_reject_none_as_chosen_source()
        {
            Action action = () => Resolve(MovieMetadataType.NONE);

            action.Should().Throw<ArgumentException>();
        }

        private class ImportRow
        {
            public int Id { get; set; }
            public int RootFolderId { get; set; }
            public string Path { get; set; }
            public string RelativePath { get; set; }
            public string Name { get; set; }
            public string OsHash { get; set; }
            public string PHash { get; set; }
            public DateTime? PHashAttemptedAt { get; set; }
            public DateTime? HashedFileModifiedAt { get; set; }
        }

        private class SeedRow
        {
            public string LegacyForeignId { get; set; }
            public string Title { get; set; }
            public int Year { get; set; }
            public int TmdbId { get; set; }
            public string ImdbId { get; set; }
            public bool Monitored { get; set; }
            public int QualityProfileId { get; set; }
            public string RootFolderPath { get; set; }
            public string Tags { get; set; }
            public int? ImportFileId { get; set; }
            public bool Resolved { get; set; }
        }
    }
}
