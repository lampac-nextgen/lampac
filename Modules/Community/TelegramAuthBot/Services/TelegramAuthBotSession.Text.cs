namespace TelegramAuthBot.Services
{
    sealed partial class TelegramAuthBotSession
    {
        static string TruncateForTelegram(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= maxLen)
                return s ?? "";
            return s.Substring(0, maxLen) + "…";
        }

        static string StripJsonError(string json)
        {
            if (string.IsNullOrEmpty(json))
                return "Ошибка API";
            try
            {
                var jo = JObject.Parse(json);
                var err = jo.Value<string>("error");
                var det = jo.Value<string>("detail");
                if (!string.IsNullOrEmpty(det))
                    return det;
                return err ?? json;
            }
            catch
            {
                return json;
            }
        }

        static string EscapeHtml(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            return s
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal);
        }
    }
}
