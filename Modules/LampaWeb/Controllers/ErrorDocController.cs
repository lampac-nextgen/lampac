using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Services.Utilities;

namespace LampaWeb;

public class ErrorDocController : BaseController
{
    [HttpGet]
    [AllowAnonymous]
    [Route("/e/acb")]
    public ActionResult Accsdb()
    {
        string shared_passwd = UnicTo.Code(8).ToLowerInvariant();
        string pw1 = UnicTo.Code(6).ToLowerInvariant();
        string pw2 = UnicTo.Code(8).ToLowerInvariant();

        return ContentTo($@"<!DOCTYPE html>
<html lang='ru'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Настройка AccsDB</title>
    <link href='/control/npm/bootstrap.min.css' rel='stylesheet'>
</head>
<body>
    <div class='container mt-5'>
        <div class='card mt-4'>
            <div class='card-body'>
                <p class='card-text'>Добавьте в init.conf заменив email/unic_id на свои:</p>
                <pre style='background: #e9ecef; padding: 1em;'><code>""accsdb"": {{
  ""accounts"": {{
    ""{pw1}@mail.ru"": ""2040-10-17T00:00:00"", // email cub.best
    ""{pw2}"": ""2040-10-17T00:00:00"", // unic_id
  }}
}}</code></pre>
				<br>
                <p class='card-text'>Или через <a href='/admin' target='_blank'>{host}/admin</a> > Пользователи > Добавить пользователя > В ID указать email/unic_id</p>
            </div>
        </div>
    </div>
    <div class='container mt-5'>
        <div class='card mt-4'>
            <div class='card-body'>
                <p class='card-text'>Если нужно разрешить внешний доступ без добавления каждого устройства, создайте пароль доступа:</p>
                <pre style='background: #e9ecef; padding: 1em;'><code>""accsdb"": {{
  ""shared_passwd"": ""{shared_passwd}""
}}</code></pre>
				<br>
                <p class='card-text'>Так все кому вы сообщили пароль <b>{shared_passwd}</b> cмогут самостоятельно авторизоваться</p>
            </div>
        </div>
    </div>
    <div class='container mt-5' style='margin-bottom: 2em;'>
        <div class='card mt-4'>
            <div class='card-body'>
                <p class='card-text'>Персональные пароли для плагинов, пример пароля <b>kitty</b>:</p>
<pre style='background: #e9ecef; padding: 1em;'><code>""accsdb"": {{
  ""accounts"": {{
    ""kitty"": ""2040-10-17T00:00:00"",
  }}
}}</code></pre>
				<br>
                <p class='card-text'>Или через <a href='/admin' target='_blank'>{host}/admin</a> > Пользователи > Добавить пользователя > В ID указать <b>kitty</b></p>

				<br>
                    <p class='card-text'>Все плагины сразу
                    <br>
                    http://IP:9118/on/js/kitty
                    </p>

                    <p class='card-text'>Онлайн
                    <br>
                    http://IP:9118/online/js/kitty
                    </p>

                    <p class='card-text'>Клубничка
                    <br>
                    http://IP:9118/sisi/js/kitty
                    </p>

                    <p class='card-text'>DLNA
                    <br>
                    http://IP:9118/dlna/js/kitty
                    </p>

                    <p class='card-text'>Таймкоды
                    <br>
                    http://IP:9118/timecode/js/kitty
                    </p>

                    <p class='card-text'>Tracks и Транскодинг
                    <br>
                    http://IP:9118/tracks/js/kitty
                    </p>

                    <p class='card-text'>TorrServer
                    <br>
                    http://IP:9118/ts/js/kitty
                    </p>
            </div>
        </div>
    </div>
</body>
</html>");
    }
}
