using System;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Common.Instrumentation
{
    public static class CleanseLogMessage
    {
        private const string TimedOutMessage = "(removed, cleansing the log message timed out)";
        private const int TruncatedMessageLength = 4096;

        private static readonly Regex[] CleansingRules =
        {
            // Url
            Rule(@"(?<=\?|&|: )(apikey|(?:access[-_]?)?token|passkey|auth|authkey|user|uid|api|[a-z_]*apikey|account|passwd)=(?<secret>[^&=]+?)(?=[ ""&=]|$)"),
            Rule(@"(?<=\?|&)[^=&?]*?(username|password)=(?<secret>[^&=]+?)(?= |&|$)"),
            Rule(@"rss(24h)?\.torrentleech\.org/(?!rss)(?<secret>[0-9a-z]+)"),
            Rule(@"torrentleech\.org/rss/download/[0-9]+/(?<secret>[0-9a-z]+)"),
            Rule(@"iptorrents\.com/[/a-z0-9?&;]*?(?:[?&;](u|tp)=(?<secret>[^&=;]+?))+(?= |;|&|$)"),
            Rule(@"/fetch/[a-z0-9]{32}/(?<secret>[a-z0-9]{32})", RegexOptions.None),
            Rule(@"(getnzb|rss)(?:(?!getnzb|rss).)*?(?<=\?|&)(r)=(?<secret>[^&=]+?)(?= |&|$)"),
            Rule(@"\b(\w*)?(_?(?<!use|get_)token|username|passwo?rd)=(?<secret>[^&=]+?)(?= |&|$|;)"),
            Rule(@"-hd.me/torrent/[a-z0-9-]\.[0-9]+\.(?<secret>[0-9a-z]+)"),

            // Trackers Announce Keys; Designed for Qbit Json; should work for all in theory
            Rule(@"announce(\.php)?(/|%2f|%3fpasskey%3d)(?<secret>[a-z0-9]{16,})|(?:(?<![a-z0-9])|(?<=%2f))(?<secret>[a-z0-9]{16,})(/|%2f)announce"),

            // Path
            Rule(@"C:\\Users\\(?<secret>[^\""]+?)(\\|$)"),
            Rule(@"/(home|Users)/(?<secret>[^/""]+?)(/|$)"),

            // Email
            Rule(@"(?<![a-z0-9._%+-])(?<secret>[a-z0-9._%+-]+)@[a-z0-9.-]+\.[a-z]{2,}\b"),

            // NzbGet
            Rule(@"""Name""\s*:\s*""[^""]*(username|password)""\s*,\s*""Value""\s*:\s*""(?<secret>[^""]+?)"""),

            // Sabnzbd
            Rule(@"""[^""]*(username|password|api_?key|nzb_key)""\s*:\s*""(?<secret>[^""]+?)"""),
            Rule(@"""email_(account|to|from|pwd)""\s*:\s*""(?<secret>[^""]+?)"""),

            // uTorrent
            Rule(@"\[""[a-z._]*(username|password)"",\d,""(?<secret>[^""]+?)"""),
            Rule(@"\[""(boss_key|boss_key_salt|proxy\.proxy)"",\d,""(?<secret>[^""]+?)"""),

            // Deluge
            Rule(@"auth.login\(""(?<secret>[^""]+?)"""),

            // BroadcastheNet
            Rule(@"""?method""?\s*:\s*""(getTorrents)"",\s*""?params""?\s*:\s*\[\s*""(?<secret>[^""]+?)"""),
            Rule(@"getTorrents\(""(?<secret>[^""]+?)"""),
            Rule(@"(?<=\?|&)(authkey|torrent_pass)=(?<secret>[^&=]+?)(?=""|&|$)"),

            // Plex
            Rule(@"(?<=\?|&)(X-Plex-Client-Identifier|X-Plex-Token)=(?<secret>[^&=]+?)(?= |&|$)"),

            // Notifiarr
            Rule(@"api/v[0-9]/notification/whisparr/(?<secret>[\w-]+)"),

            // Discord
            Rule(@"discord.com/api/webhooks/((?<secret>[\w-]+)/)?(?<secret>[\w-]+)"),

            // Telegram
            Rule(@"api.telegram.org/bot(?<id>[\d]+):(?<secret>[\w-]+)/")
        };

        private static readonly Regex CleanseRemoteIPRegex = Rule(@"(?:Auth-\w+(?<!Failure|Unauthorized) ip|from) (\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})", RegexOptions.None);

        public static string Cleanse(string message)
        {
            return TryCleanse(message, out var cleansed) ? cleansed : TimedOutMessage;
        }

        // A rule that times out leaves the line uncleansed, so it is never written as-is: the
        // event is re-rendered with its message truncated and the timeout noted.
        public static string Cleanse(LogEventInfo logEvent, Func<LogEventInfo, string> render)
        {
            if (TryCleanse(render(logEvent), out var cleansed))
            {
                return cleansed;
            }

            var message = logEvent.FormattedMessage ?? string.Empty;

            if (message.Length > TruncatedMessageLength)
            {
                message = message[..TruncatedMessageLength];
            }

            var truncatedEvent = new LogEventInfo(logEvent.Level, logEvent.LoggerName, $"{message}... {TimedOutMessage}")
            {
                TimeStamp = logEvent.TimeStamp
            };

            return Cleanse(render(truncatedEvent));
        }

        private static bool TryCleanse(string message, out string cleansed)
        {
            cleansed = message;

            if (message.IsNullOrWhiteSpace())
            {
                return true;
            }

            try
            {
                foreach (var regex in CleansingRules)
                {
                    cleansed = regex.Replace(cleansed, m =>
                    {
                        var value = m.Value;

                        foreach (var capture in m.Groups["secret"].Captures.OfType<Capture>().Reverse())
                        {
                            value = value.Replace(capture.Index - m.Index, capture.Length, "(removed)");
                        }

                        return value;
                    });
                }

                cleansed = CleanseRemoteIPRegex.Replace(cleansed, CleanseRemoteIP);
            }
            catch (RegexMatchTimeoutException)
            {
                cleansed = null;

                return false;
            }

            return true;
        }

        private static string CleanseRemoteIP(Match match)
        {
            var group = match.Groups[1];
            var valueIP = group.Value;

            if (IPAddress.TryParse(valueIP, out var address) && !address.IsLocalAddress())
            {
                var prefix = match.Value.Substring(0, group.Index - match.Index);
                var postfix = match.Value.Substring(group.Index + group.Length - match.Index);
                var items = valueIP.Split('.');

                return $"{prefix}{items[0]}.*.*.{items[3]}{postfix}";
            }

            return match.Value;
        }

        // Every rule is compiled and timeout-guarded, and this is the only place that says so - a new
        // rule cannot be added without both. RegexDefaults.Timeout is the codebase-wide circuit
        // breaker against catastrophic backtracking; the two rules that pass RegexOptions.None are
        // case-sensitive by design.
        private static Regex Rule(string pattern, RegexOptions options = RegexOptions.IgnoreCase)
        {
            return new Regex(pattern, options | RegexOptions.Compiled, RegexDefaults.Timeout);
        }
    }
}
