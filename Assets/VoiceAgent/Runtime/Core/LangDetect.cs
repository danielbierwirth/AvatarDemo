namespace VoiceAgent
{
    /// <summary>The script a text is written in: Hangul → Korean, Arabic letters → Arabic, anything else → English.</summary>
    public static class LangDetect
    {
        public static Lang Of(string text)
        {
            foreach (var c in text ?? string.Empty)
            {
                if (c >= '가' && c <= '힣') return Lang.Ko;
                if (ArabicText.IsArabicLetter(c)) return Lang.Ar;
            }
            return Lang.En;
        }
    }
}
