using NzbDrone.Core.Movies;

namespace NzbDrone.Core.Validation.Paths
{
    internal static class ValidatedItemType
    {
        // Movie is the strict default when the instance carries no item type.
        public static bool UsesSceneLayout(object instance)
        {
            return instance switch
            {
                Movie movie => movie.MovieMetadata?.Value?.ItemType.UsesSceneLayout() is true,
                null => false,
                _ => instance.GetType().GetProperty("ItemType")?.GetValue(instance) is ItemType itemType && itemType.UsesSceneLayout()
            };
        }
    }
}
