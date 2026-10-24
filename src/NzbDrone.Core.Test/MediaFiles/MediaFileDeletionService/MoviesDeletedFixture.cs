using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.Extras.Subtitles;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.MediaFileDeletionService
{
    [TestFixture]
    public class MoviesDeletedFixture : CoreTest<Core.MediaFiles.MediaFileDeletionService>
    {
        private const string SharedPath = "/lib/scenes/Shady Spa";

        private Movie _movie;

        [SetUp]
        public void Setup()
        {
            _movie = Builder<Movie>.CreateNew()
                                   .With(m => m.Id = 1)
                                   .With(m => m.Path = SharedPath)
                                   .Build();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(SharedPath))
                  .Returns(true);
        }

        private void GivenAllMoviePaths(string otherPath)
        {
            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.AllMoviePaths())
                  .Returns(new Dictionary<int, string>
                  {
                      { 1, SharedPath },
                      { 2, otherPath }
                  });
        }

        private void Delete()
        {
            Subject.HandleAsync(new MoviesDeletedEvent(new List<Movie> { _movie }, true, false));
        }

        [Test]
        public void should_not_recycle_folder_when_another_item_not_in_batch_shares_the_path()
        {
            GivenAllMoviePaths(SharedPath);

            Delete();

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFolder(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_not_recycle_folder_when_it_is_parent_of_another_item_not_in_batch()
        {
            GivenAllMoviePaths(SharedPath + "/old scene");

            Delete();

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFolder(It.IsAny<string>()), Times.Never());
        }

        private void GivenOwnedFiles(params string[] paths)
        {
            foreach (var path in paths)
            {
                Mocker.GetMock<IDiskProvider>()
                      .Setup(s => s.FileExists(path))
                      .Returns(true);
            }
        }

        private void DeleteWithOwned(params string[] ownedPaths)
        {
            var owned = new Dictionary<int, List<string>> { { _movie.Id, ownedPaths.ToList() } };

            Subject.HandleAsync(new MoviesDeletedEvent(new List<Movie> { _movie }, true, false, owned));
        }

        [Test]
        public void deleting_scene_in_shared_folder_recycles_only_its_owned_files()
        {
            var video = SharedPath + "/scene one.mp4";
            var subtitle = SharedPath + "/scene one.en.srt";

            GivenAllMoviePaths(SharedPath);
            GivenOwnedFiles(video, subtitle, SharedPath + "/scene two.mp4");

            DeleteWithOwned(video, subtitle);

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(2));
            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFile(video, It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFile(subtitle, It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFolder(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void deleting_last_scene_in_flat_folder_with_unknown_files_keeps_folder()
        {
            var video = SharedPath + "/scene one.mp4";

            GivenAllMoviePaths("/lib/scenes/Other Studio");
            GivenOwnedFiles(video);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderEmpty(SharedPath))
                  .Returns(false);

            DeleteWithOwned(video);

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFile(video, It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFolder(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.DeleteFolder(SharedPath, It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void deleting_last_scene_in_flat_folder_removes_folder_when_empty()
        {
            var video = SharedPath + "/scene one.mp4";

            GivenAllMoviePaths("/lib/scenes/Other Studio");
            GivenOwnedFiles(video);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderEmpty(SharedPath))
                  .Returns(true);

            DeleteWithOwned(video);

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.DeleteFolder(SharedPath, true), Times.Once());
            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFolder(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void deleting_scene_keeps_nested_movie_folder()
        {
            var video = SharedPath + "/scene one.mp4";
            var nestedMovie = SharedPath + "/Some Movie";

            GivenAllMoviePaths(nestedMovie);
            GivenOwnedFiles(video, nestedMovie + "/movie.mp4");
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(nestedMovie))
                  .Returns(true);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderEmpty(SharedPath))
                  .Returns(false);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderEmpty(nestedMovie))
                  .Returns(true);

            DeleteWithOwned(video);

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFile(video, It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFile(nestedMovie + "/movie.mp4", It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFolder(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.RemoveEmptySubfolders(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_capture_owned_paths_only_for_scenes_in_shared_folders()
        {
            var other = Builder<Movie>.CreateNew().With(m => m.Id = 2).With(m => m.Path = "/lib/movies/Solo").Build();

            Mocker.GetMock<ISceneFolderLayout>().Setup(s => s.IsShared(_movie)).Returns(true);
            Mocker.GetMock<ISceneFolderLayout>().Setup(s => s.IsShared(other)).Returns(false);
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesByMovie(1))
                  .Returns(new List<MovieFile> { new MovieFile { RelativePath = "scene one.mp4" } });
            Mocker.GetMock<ISubtitleFileService>()
                  .Setup(s => s.GetFilesByMovie(1))
                  .Returns(new List<SubtitleFile> { new SubtitleFile { RelativePath = "scene one.en.srt" } });
            Mocker.GetMock<IOtherExtraFileService>()
                  .Setup(s => s.GetFilesByMovie(1))
                  .Returns(new List<OtherExtraFile> { new OtherExtraFile { RelativePath = "scene one.jpg" } });
            Mocker.GetMock<IMetadataFileService>()
                  .Setup(s => s.GetFilesByMovie(1))
                  .Returns(new List<MetadataFile> { new MetadataFile { RelativePath = "scene one.nfo" } });

            var deleting = new MoviesDeletingEvent(new List<Movie> { _movie, other });

            Subject.Handle(deleting);

            deleting.OwnedFilePaths.Keys.Should().BeEquivalentTo(new[] { 1 });
            deleting.OwnedFilePaths[1].Should().BeEquivalentTo(new[]
            {
                Path.Combine(SharedPath, "scene one.mp4"),
                Path.Combine(SharedPath, "scene one.en.srt"),
                Path.Combine(SharedPath, "scene one.jpg"),
                Path.Combine(SharedPath, "scene one.nfo")
            });
        }

        private static List<Movie> GivenStudioScenes()
        {
            return Enumerable.Range(1, 3)
                             .Select(i =>
                             {
                                 var scene = Builder<Movie>.CreateNew().With(m => m.Id = i).With(m => m.Path = SharedPath).Build();
                                 scene.MovieMetadata.Value.ItemType = ItemType.Scene;

                                 return scene;
                             })
                             .ToList();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void bulk_delete_of_whole_studio_folder_never_recycles_the_folder(bool captureThrows)
        {
            var scenes = GivenStudioScenes();
            var owned = new Dictionary<int, List<string>>();

            // The batch's own rows are gone by now
            Mocker.GetMock<IMovieService>().Setup(s => s.AllMoviePaths()).Returns(new Dictionary<int, string>());
            Mocker.GetMock<ISceneFolderLayout>().Setup(s => s.IsFlat()).Returns(true);
            Mocker.GetMock<ISceneFolderLayout>().Setup(s => s.IsShared(It.IsAny<Movie>())).Returns(true);

            if (captureThrows)
            {
                Mocker.GetMock<IMediaFileService>()
                      .Setup(s => s.GetFilesByMovie(2))
                      .Throws(new System.InvalidOperationException("db"));

                var deleting = new MoviesDeletingEvent(scenes);

                Subject.Handle(deleting);
                owned = deleting.OwnedFilePaths;
            }

            Subject.HandleAsync(new MoviesDeletedEvent(scenes, true, false, owned));

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFolder(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void batch_scenes_sharing_a_path_never_recycle_it_in_a_per_scene_format()
        {
            var scenes = GivenStudioScenes();

            Mocker.GetMock<IMovieService>().Setup(s => s.AllMoviePaths()).Returns(new Dictionary<int, string>());
            Mocker.GetMock<ISceneFolderLayout>().Setup(s => s.IsFlat()).Returns(false);

            Subject.HandleAsync(new MoviesDeletedEvent(scenes, true, false));

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFolder(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_recycle_folder_when_no_other_item_shares_it()
        {
            GivenAllMoviePaths("/lib/scenes/Other Studio");

            Delete();

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFolder(SharedPath), Times.Once());
        }
    }
}
