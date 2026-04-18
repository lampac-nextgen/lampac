# Core

**Точка входа** хоста Lampac: ASP.NET Core Web-приложение (`Core.dll` / apphost), порт и пайплайн задаются конфигурацией ([`init.conf`](../config/example.init.conf), секция `listen`).

Связанные проекты: библиотека **[`Shared`](../Shared/README.md)** (конфиг, модули, базовые сервисы), компилируемые модули из каталогов **`module/`** и **`mods/`** при публикации (см. [корневой README](../README.md)).

---

## Содержимое каталога

| Путь | Назначение |
| --- | --- |
| [`Program.cs`](Program.cs) | `Main`: предзагрузка DLL из **`runtimes/references/`**, настройка `AssemblyLoadContext`, сбор ссылок Roslyn для [`CSharpEval`](../Shared/Services/CSharpEval.cs), запуск хоста |
| [`Startup.cs`](Startup.cs) | `ConfigureServices`: `HttpClient` (именованные `proxy`, `base`, …), регистрация MVC, **загрузка и компиляция модулей**, вызов `IModuleConfigure` у модулей. `Configure`: цепочка middleware, `MapControllers`, RCH-эндпоинты |
| [`Controllers/`](Controllers/) | [`ApiController`](Controllers/ApiController.cs) (версия, geo, stats, Chromium ping, …), [`RchApiEndpoints`](Controllers/RchApiEndpoints.cs), [`OpenStatController`](Controllers/OpenStatController.cs) |
| [`Middlewares/`](Middlewares/) | WAF, accsdb, `BaseMod`, прокси изображений/медиа (`ProxyImg`, M3U8/MPD/DASH), лимиты запросов, статика и кеши |
| [`Services/`](Services/) | [`NativeWebSocket`](Services/NativeWebSocket.cs), провайдер смены дескрипторов действий MVC для динамических сборок |
| [`plugins/`](plugins/) | JS для RCH/NWS и др. (копируются в вывод publish) |
| [`data/`](data/) | GeoIP и прочие данные хоста |
| [`wwwroot/`](wwwroot/) | Статические страницы и ассеты, отдаваемые хостом (SISI UI, stats и т.д.) |

---

## Загрузка модулей (кратко)

Логика в **`Startup.ConfigureServices`**, [`ModuleRepository.UpdateModules()`](../Shared/Services/Module/ModuleRepository.cs):

1. Каталоги обрабатываются в порядке **`mods`**, затем **`module`**.
2. Из подкаталога **`references/`** подгружаются готовые **`.dll`** как части приложения MVC.
3. Собственные **`.dll`** в корне `mods/` / `module/` подключаются как сборки модулей.
4. Папки с **`manifest.json`** (на любой вложенности): при необходимости компиляция **Roslyn** ([`CSharpEval.Compilation`](../Shared/Services/CSharpEval.cs)); учитываются **`BaseModule.SkipModules`**, **`LoadModules`**, флаг **`enable`** в манифесте. Для `LoadModules` поддержаны: точное имя модуля (`"KinoUkr"`), имя группы/папки верхнего уровня (`"OnlineUKR"` или имя репозитория вроде `"lampac-ukraine"`), regex (`"LME.*"`), а также `".*"` для загрузки всего.
5. После сборки для каждого модуля вызывается **`IModuleConfigure.Configure`**.

В **`Configure`** после старта приложения для каждого модуля вызывается **`IModuleLoaded.Loaded`**. Для модулей с **`dynamic: true`** в манифесте включается пересборка при изменении **`.cs`** (см. `WatchersDynamicModule` в `Startup.cs`).

Исходники в репозитории копируются в выходную папку по правилам [`Core.csproj`](Core.csproj) (`Modules/**` → `module/…`, `Online/`, `SISI/`, `TestModules/` → `mods/`).

---

## Цепочка middleware (упрощённо)

Порядок задаётся в **`Startup.Configure`** (фрагменты зависят от `init.conf` / `BaseModule.Middlewares`):

`UseForwardedHeaders` → **`UseBaseMod`** → **`UseModHeaders`** → **`UseRequestInfo`** → при необходимости карта **`/nws`** (WAF + WebSocket) → **`UseRouting`** → сжатие, Staticache, анонимные запросы, ранний **`UseModule`** (подписка `EventListener.Middleware`) → прокси `/proxy/`, `/proxyimg` → статические файлы → при включении **`UseWAF`** → **`UseAuthorization`** → **`UseAccsdb`** → поздний **`UseModule`** → лимиты, статистика, эндпоинты контроллеров и RCH.

Точную последовательность и условия см. в [`Startup.cs`](Startup.cs).

---

## Сборка и запуск

```bash
# из корня репозитория
./build.sh
# или
dotnet publish Core/Core.csproj -c Release -o publish
cd publish && dotnet Core.dll
```

Решение: [`NextGen.slnx`](../NextGen.slnx).
