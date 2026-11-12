using System;
using System.Globalization;

namespace VoiceAgent
{
    /// <summary>A clock time as Nova says it in each language (mini-agent-unity's SmartHome.FormatTime).</summary>
    public static class ClockText
    {
        public static string Format(DateTime time, Lang lang)
        {
            if (lang == Lang.En) return time.ToString("h:mm tt", CultureInfo.InvariantCulture);
            var hour = time.Hour % 12 == 0 ? 12 : time.Hour % 12;
            if (lang == Lang.Ar) return $"{hour}:{time.Minute:00} {(time.Hour < 12 ? "صباحًا" : "مساءً")}";
            return $"{(time.Hour < 12 ? "오전" : "오후")} {hour}시 {time.Minute}분";
        }
    }
}
