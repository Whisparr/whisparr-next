using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.OrganizerTests.FileNameBuilderTests
{
    [TestFixture]
    public class JavFileNameBuilderFixture : CoreTest<FileNameBuilder>
    {
        private Movie _jav;
        private Movie _scene;
        private MovieFile _movieFile;
        private NamingConfig _namingConfig;

        [SetUp]
        public void Setup()
        {
            _jav = Builder<Movie>
                .CreateNew()
                .With(m => m.Title = "Perfect Satisfaction")
                .With(m => m.MovieMetadata.Value.Title = "Perfect Satisfaction")
                .With(m => m.MovieMetadata.Value.Code = "ABP-123")
                .With(m => m.MovieMetadata.Value.StudioTitle = "Absolutely Perfect")
                .With(m => m.MovieMetadata.Value.ItemType = ItemType.Jav)
                .Build();

            _scene = Builder<Movie>
                .CreateNew()
                .With(m => m.Title = "The Last Train Home")
                .With(m => m.MovieMetadata.Value.Title = "The Last Train Home")
                .With(m => m.MovieMetadata.Value.ReleaseDate = "2025-11-25")
                .With(m => m.MovieMetadata.Value.StudioTitle = "Pure Taboo")
                .With(m => m.MovieMetadata.Value.ItemType = ItemType.Scene)
                .Build();

            _namingConfig = NamingConfig.Default;
            _namingConfig.RenameScenes = true;
            _namingConfig.RenameJav = true;

            Mocker.GetMock<INamingConfigService>().Setup(c => c.GetConfig()).Returns(_namingConfig);

            _movieFile = new MovieFile { Quality = new QualityModel(Quality.Bluray1080p), RelativePath = "original.name.mkv" };

            Mocker.GetMock<IQualityDefinitionService>()
                .Setup(v => v.Get(Moq.It.IsAny<Quality>()))
                .Returns<Quality>(v => Quality.DefaultQualityDefinitions.First(c => c.Quality == v));

            Mocker.GetMock<ICustomFormatService>().Setup(v => v.All()).Returns(new List<CustomFormat>());
        }

        [Test]
        public void jav_default_file_name()
        {
            Subject.BuildFileName(_jav, _movieFile, _namingConfig)
                .Should().Be("ABP-123 - Perfect Satisfaction [Bluray-1080p]");
        }

        [Test]
        public void jav_default_folder()
        {
            Subject.GetMovieFolder(_jav, _namingConfig)
                .Should().Be(Path.Combine("jav", "Absolutely Perfect", "ABP-123"));
        }

        [Test]
        public void rename_jav_false_keeps_original()
        {
            _namingConfig.RenameJav = false;

            Subject.BuildFileName(_jav, _movieFile, _namingConfig)
                .Should().Be("original.name");
        }

        [Test]
        public void scene_format_unchanged_by_jav()
        {
            _namingConfig.StandardSceneFormat = "{Studio Title} - {Release-Date} - {Scene Title}";
            _namingConfig.StandardJavFormat = "JAV {Scene Code}";

            Subject.BuildFileName(_scene, _movieFile, _namingConfig)
                .Should().Be("Pure Taboo - 2025-11-25 - The Last Train Home");
        }
    }
}
