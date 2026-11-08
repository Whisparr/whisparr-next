namespace NzbDrone.Core.Movies
{
    public static class ItemTypeExtensions
    {
        // Scenes and JAV share the scene folder layout: a folder may hold several items, and the
        // folder format decides whether it does. Scene metadata checks (studio/date matching) stay on ItemType.Scene.
        public static bool UsesSceneLayout(this ItemType itemType)
        {
            return itemType is ItemType.Scene or ItemType.Jav;
        }
    }
}
