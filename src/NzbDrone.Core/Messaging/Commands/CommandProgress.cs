namespace NzbDrone.Core.Messaging.Commands
{
    public class CommandProgress
    {
        public string Phase { get; set; }
        public long Done { get; set; }
        public long Total { get; set; }

        // Overall progress, 0-100
        public double Percent { get; set; }
    }
}
