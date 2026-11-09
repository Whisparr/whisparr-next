using NzbDrone.Core.Organizer;

namespace Whisparr.Api.V5.Config
{
    public class NamingExampleResource
    {
        public string MovieExample { get; set; }
        public string MovieFolderExample { get; set; }
        public string SceneExample { get; set; }
        public string SceneFolderExample { get; set; }
        public string JavExample { get; set; }
        public string JavFolderExample { get; set; }
        public string SceneImportFolderExample { get; set; }
        public string MainSceneFolderExample { get; internal set; }
        public int MaxFolderPathLengthExample { get; set; }
        public int MaxFilePathLengthExample { get; set; }
    }

    public static class NamingConfigResourceMapper
    {
        public static NamingConfigResource ToResource(this NamingConfig model)
        {
            return new NamingConfigResource
            {
                Id = model.Id,

                RenameMovies = model.RenameMovies,
                RenameScenes = model.RenameScenes,
                ReplaceIllegalCharacters = model.ReplaceIllegalCharacters,
                ColonReplacementFormat = model.ColonReplacementFormat,
                StandardMovieFormat = string.IsNullOrEmpty(model.StandardMovieFormat) ? "" : model.StandardMovieFormat,
                MovieFolderFormat = string.IsNullOrEmpty(model.MovieFolderFormat) ? "" : model.MovieFolderFormat,
                StandardSceneFormat = string.IsNullOrEmpty(model.StandardSceneFormat) ? "" : model.StandardSceneFormat,
                SceneFolderFormat = string.IsNullOrEmpty(model.SceneFolderFormat) ? "" : model.SceneFolderFormat,
                RenameJav = model.RenameJav,
                StandardJavFormat = string.IsNullOrEmpty(model.StandardJavFormat) ? "" : model.StandardJavFormat,
                JavFolderFormat = string.IsNullOrEmpty(model.JavFolderFormat) ? "" : model.JavFolderFormat,
                SceneImportFolderFormat = string.IsNullOrEmpty(model.SceneImportFolderFormat) ? "" : model.SceneImportFolderFormat,
                MaxFolderPathLength = model.MaxFolderPathLength,
                MaxFilePathLength = model.MaxFilePathLength,
            };
        }

        public static NamingConfig ToModel(this NamingConfigResource resource)
        {
            return new NamingConfig
            {
                Id = resource.Id,

                RenameMovies = resource.RenameMovies,
                RenameScenes = resource.RenameScenes,
                ReplaceIllegalCharacters = resource.ReplaceIllegalCharacters,
                ColonReplacementFormat = resource.ColonReplacementFormat,
                StandardMovieFormat = resource.StandardMovieFormat,
                MovieFolderFormat = resource.MovieFolderFormat,
                StandardSceneFormat = resource.StandardSceneFormat,
                SceneFolderFormat = resource.SceneFolderFormat,
                RenameJav = resource.RenameJav,
                StandardJavFormat = resource.StandardJavFormat,
                JavFolderFormat = resource.JavFolderFormat,
                SceneImportFolderFormat = resource.SceneImportFolderFormat,
                MaxFilePathLength = resource.MaxFilePathLength,
                MaxFolderPathLength = resource.MaxFolderPathLength
            };
        }
    }
}
