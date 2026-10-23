namespace NzbDrone.Core.MediaFiles
{
    public enum MatchTier
    {
        Unknown = 0,
        Name = 1,
        Confirmed = 2,
        Hash = 3,
        Manual = 4
    }
}
