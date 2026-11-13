using Shared.Models.Module;

namespace NativeAppleTV.Models;

/// <summary>Секция NativeAppleTV в init.conf.</summary>
public class NativeAppleTVConf : ModuleBaseConf
{
    /// <summary>Идентификатор и имя источника в приложении.</summary>
    public string id { get; set; } = "lampac";
    public string title { get; set; } = "Lampac";

    /// <summary>Внешний адрес сервера для ссылки в QR (https://host:port). Пусто — берётся из запроса.</summary>
    public string public_url { get; set; }

    /// <summary>Ссылка Telegram-бота, {code} подставит приложение: https://t.me/bot?start={code}. Пусто — страница подтверждения.</summary>
    public string bot_link { get; set; }

    /// <summary>Общий секрет бота для POST /nativeappletv/bot/confirm (заголовок X-Bot-Secret). Пусто — маршрут закрыт.</summary>
    public string bot_secret { get; set; }

    /// <summary>Способы входа в порядке предпочтения: code_push, token.</summary>
    public string[] methods { get; set; } = ["code_push"];

    /// <summary>Сколько секунд живёт код и как часто приложение его опрашивает.</summary>
    public int code_ttl { get; set; } = 120;
    public int poll_interval { get; set; } = 3;

    /// <summary>Срок действия выданного токена, дней.</summary>
    public int token_days { get; set; } = 365;

    /// <summary>Группа accsdb для выданных токенов.</summary>
    public int group { get; set; }
}
