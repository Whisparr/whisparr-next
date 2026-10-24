using NzbDrone.Core.Movies;

namespace NzbDrone.Core.Validation.Paths
{
    internal static class ValidatedItemType
    {
        // Movie is the strict default when the instance carries no item type.
        public static bool IsScene(object instance)
        {
            return instance switch
            {
                Movie movie => movie.MovieMetadata?.Value?.ItemType == ItemType.Scene,
                null => false,
                _ => instance.GetType().GetProperty("ItemType")?.GetValue(instance) is ItemType.Scene
            };
        }
    }
}
