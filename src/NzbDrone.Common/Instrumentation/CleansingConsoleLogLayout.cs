using NLog;
using NLog.Layouts;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Common.Instrumentation;

public class CleansingConsoleLogLayout : Layout
{
    private readonly string _format;

    public CleansingConsoleLogLayout(string format)
    {
        _format = format;
    }

    protected override string GetFormattedMessage(LogEventInfo logEvent)
    {
        var simpleLayout = new SimpleLayout(_format);
        if (!RuntimeInfo.IsProduction)
        {
            return simpleLayout.Render(logEvent);
        }

        return CleanseLogMessage.Cleanse(logEvent, simpleLayout.Render);
    }
}
