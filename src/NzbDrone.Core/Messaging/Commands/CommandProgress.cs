using System;

namespace NzbDrone.Core.Messaging.Commands
{
    public class CommandProgress
    {
        public string Phase { get; set; }
        public long Done { get; set; }
        public long Total { get; set; }

        // Overall progress, 0-100
        public double Percent { get; set; }

        // What's left of the current phase alone, when the command knows it; Done and Total cover the whole command
        public long? PhaseRemaining { get; set; }

        // The one place percent is normalized: NaN becomes 0 and the rest is clamped to 0-100,
        // so a bad value can never reach the JSON serializer or the UI.
        public static CommandProgress Create(string phase, long done, long total, double percent, long? phaseRemaining = null)
        {
            return new CommandProgress
            {
                Phase = phase,
                Done = done,
                Total = total,
                Percent = double.IsNaN(percent) ? 0 : Math.Clamp(percent, 0, 100),
                PhaseRemaining = phaseRemaining
            };
        }
    }
}
