using System.Text;
using System.Text.Json;
using QRAuth.Models;
using QRAuth.Services;

namespace QRAuth
{
    public static class DenyPageGenerator
    {
        // telegram-icon-transparent.png, downscaled to 128x128 and base64-encoded —
        // see the comment where it's used in Build() for why it's inlined instead of
        // shipped as a separate file.
        // #dpc-shade's gradients (the tile fallback adds a .45 dim layer under them). Also
        // repeated on the glass buttons in wall mode, sized to the screen, so the fake glass
        // shows the same shading the real backdrop would have seen.
        const string Shade = "radial-gradient(130% 100% at 62% 50%,rgba(10,10,11,0) 55%,rgba(10,10,11,.75) 100%),linear-gradient(0deg,rgba(10,10,11,.92) 0%,rgba(10,10,11,0) 30%,rgba(10,10,11,0) 75%,rgba(10,10,11,.6) 100%),linear-gradient(90deg,rgba(10,10,11,.93) 0%,rgba(10,10,11,.88) 32%,rgba(10,10,11,.62) 56%,rgba(10,10,11,.5) 100%)";

        public static string Build(DenyPageConf conf)
        {
            string tgUrl = NormalizeTgUrl(conf.tg_target);
            bool hasTg = !string.IsNullOrWhiteSpace(tgUrl);
            string qrSize = "480";

            // Pre-rendered wall (PosterWall.BuildWall) known at generation time: deny.js is
            // regenerated whenever this changes (ModInit re-runs Build every 3s and only
            // writes on a content change), so the page can request wall.jpg right away and
            // paint the ~1KB inline preview instantly — no /posters manifest round-trip first.
            bool hasWall = conf.poster_wall && PosterWall.WallPath() != null;
            string wallLqip = hasWall ? PosterWall.WallLqipBase64() : null;
            string wallGlass = hasWall ? PosterWall.WallGlassBase64() : null;
            string wallLqipPortrait = hasWall ? PosterWall.PortraitLqipBase64() : null;
            string posterLayers = conf.poster_wall ? "<div id=\\\"dpc-posters\\\"></div><div id=\\\"dpc-shade\\\"></div>" : "";

            string jsTgUrl = Js(tgUrl);
            string jsTitle = Js(string.IsNullOrWhiteSpace(conf.page_title) ? "Вход в Lampa" : conf.page_title);
            // Short defaults: the buttons already say "log in by password / via Telegram",
            // so the copy must not re-narrate them. The subtitle never mentions the QR — on
            // phones there is none (see the no-QR @media rule).
            string jsSub = Js(string.IsNullOrWhiteSpace(conf.page_subtitle)
                ? (hasTg ? "Войдите через Telegram или по паролю." : "Доступ ограничен. Пароль можно получить у администратора.")
                : conf.page_subtitle);
            // Steps: an empty line is simply not rendered (no default for step1 — "press the
            // password button" only repeated the button); both empty → no #dpc-steps block.
            string step1 = (conf.step1_text ?? "").Trim();
            string step2 = !string.IsNullOrWhiteSpace(conf.step2_text) ? conf.step2_text.Trim()
                            : hasTg ? "Нет пароля? Бот в Telegram поможет получить доступ." : "";
            // The QR is a login method now (startQrAuth → bot confirm → doLogin), not a
            // "get a password from the bot" link, so the default copy says what it does.
            string jsQrSub = Js(string.IsNullOrWhiteSpace(conf.qr_subcaption) ? "Наведите камеру телефона и подтвердите вход в Telegram" : conf.qr_subcaption);
            string jsTgBtn = Js(string.IsNullOrWhiteSpace(conf.tg_button_text) ? "Войти через Telegram" : conf.tg_button_text);

            var sb = new StringBuilder();
            sb.AppendLine("// QRAuth deny-page v5.2-preview - auto-generated from init.conf[DenyPage]");
            sb.AppendLine("// DO NOT EDIT - overwritten on config reload.");
            sb.AppendLine();
            sb.AppendLine("var network = new Lampa.Reguest();");
            sb.AppendLine();

            // ── CSS ──────────────────────────────────────────────────────────
            sb.AppendLine("(function(){");
            sb.AppendLine("  var s = document.createElement('style');");
            sb.AppendLine("  s.textContent = [");

            // Palette tokens (var(--dpc-*)) are written below as if they were CSS custom
            // properties, but Build() substitutes them with literals (see Palette) — old TV
            // engines (Tizen 3 = Chromium 47) have no custom-property support.
            sb.AppendLine("    ':root{--dpc-ease-out:cubic-bezier(0.23,1,0.32,1)}',");
            // "SegoeUI" (no space) is the @font-face Lampa's own app.css bundles and has
            // already loaded by the time deny.js runs — same face on every TV/phone, no
            // extra request. It only ships 300/400/600/700, so never use weight 800+.
            // "Segoe UI" (system, Windows) and system-ui are just fallbacks.
            sb.AppendLine("    '#dpc{color-scheme:dark;position:fixed;top:0;right:0;bottom:0;left:0;z-index:99999;display:flex;align-items:center;justify-content:center;font-family:\"SegoeUI\",\"Segoe UI\",system-ui,sans-serif;color:var(--dpc-ink);padding:0;box-sizing:border-box;overflow:auto;background:#050308}',");
            sb.AppendLine("    '@keyframes dpcIn{from{opacity:0}to{opacity:1}}',");
            sb.AppendLine("    '@keyframes dpcStagger{from{opacity:0;transform:translateY(8px)}to{opacity:1;transform:translateY(0)}}',");
            // QR skeleton sweep — opacity or transform only, so they
            // stay on the compositor even on weak TV GPUs.
            sb.AppendLine("    '@keyframes dpcSweep{from{transform:translateX(-100%)}to{transform:translateX(100%)}}',");

            // Card
            sb.AppendLine("    '#dpc-w{position:relative;overflow:hidden;width:100%;height:100%;background:var(--dpc-base);animation:dpcIn .3s linear}',");

            // Neutral "cinema" backdrop: no hue of its own — when the poster wall is on, the
            // posters are the only colour on screen (a tinted wash over them just turns them
            // muddy). Without posters: a barely-there light lift top-right so the flat black
            // doesn't look like a failed load. No filter:blur — cheapest possible layer.
            sb.AppendLine("    '#dpc-bg{position:absolute;top:0;left:0;width:100%;height:100%;z-index:0;pointer-events:none;background:radial-gradient(60% 55% at 78% 30%,rgba(255,255,255,.07) 0%,rgba(255,255,255,0) 70%),radial-gradient(80% 60% at 50% 120%,rgba(255,255,255,.04) 0%,rgba(255,255,255,0) 70%)}',");

            // Poster wall: one server-rendered JPEG (PosterWall.BuildWall → /tgbot/qr/wall),
            // already tilted and dimmed ×.55 — a flat full-size background, no transform, no
            // children: a single request, decode and layer, which is what weak TVs needed.
            // Landscape wall on TVs/desktops, portrait wall on phones (≤700px). Explicit
            // top/left/width/height instead of `inset` for old TV engines. #dpc-shade: dark
            // zone under the left column (text never sits on a busy poster), fading to a
            // uniform .5 dim toward the right, plus top/bottom fade and an edge vignette.
            // Text there also keeps a soft text-shadow (.dpc-has-posters rule below).
            if (conf.poster_wall)
            {
                sb.AppendLine("    '#dpc-posters{display:none;position:absolute;top:0;left:0;width:100%;height:100%;z-index:0;pointer-events:none;background-color:#0a0a0b;background-position:center;background-size:cover;background-repeat:no-repeat}',");
                sb.AppendLine("    '#dpc-shade{display:none;position:absolute;top:0;left:0;width:100%;height:100%;z-index:0;pointer-events:none;background:" + Shade + "}',");
                // Fake glass (TV/desktop): the buttons paint the server's pre-blurred wall + the
                // shade as their own background (dpcGlassFit), so the live backdrop-filter is
                // off. It showed no blur in the Android TV WebView, and it cost a re-blur per
                // frame. Press feedback: brighter hairline (the fill is an image now).
                sb.AppendLine("    '.dpc-glass .dpc-b{-webkit-backdrop-filter:none;backdrop-filter:none;background-repeat:no-repeat}.dpc-glass .dpc-b:not(:disabled):active{border-color:rgba(255,255,255,.7)}',");
                sb.AppendLine("    '.dpc-has-posters #dpc-title,.dpc-has-posters #dpc-subtitle,.dpc-has-posters #dpc-steps,.dpc-has-posters #dpc-logo,.dpc-has-posters #dpc-err{text-shadow:0 1px 3px rgba(0,0,0,.65)}',");
                sb.AppendLine("    '.dpc-has-posters #dpc-posters,.dpc-has-posters #dpc-shade{display:block}.dpc-has-posters #dpc-bg{display:none}',");
                // Phones: flat heavier shade since the stacked layout has text across the full
                // width (.78 over the ×.55 wall = the old .88 over undimmed posters).
                sb.AppendLine("    '@media(max-width:700px){#dpc-shade{background:rgba(10,10,11,.78)}}',");
            }

            // Content grid
            // Full width, no max-width cap: since 1em tracks innerWidth (Lampa's body
            // font-size formula), side paddings in em are already a fixed viewport fraction
            // on every screen. The old 72em cap only added ~7% dead space on each side.
            // Side padding 4.5em (~5% of width) doubles as TV overscan safe area.
            sb.AppendLine("    '#dpc-content{position:relative;z-index:1;display:flex;gap:0;height:100%;min-height:460px}',");
            // iPhone notch / rounded corners: with viewport-fit=cover (dpcViewportCover) the
            // page reaches under them, so keep the content out of the unsafe side strips. Old
            // TV engines don't know env() — the declaration is simply dropped there.
            sb.AppendLine("    '#dpc-content{box-sizing:border-box;padding-left:env(safe-area-inset-left);padding-right:env(safe-area-inset-right)}',");

            // Left column. Sizes are in `em`, not `px`/`vw`/media-query breakpoints — `#dpc` is
            // appended straight onto <body>, and Lampa itself already sets body's font-size to
            // `max(innerWidth/84.17 * interface_size_multiplier, 10.6px)` (Modules/LampaWeb/
            // widgets/{samsung,lg}/app.js, function size()), recalculated on resize and on the
            // user's Settings → Interface size change. That's Lampa's own answer to "how big
            // should UI be on this screen" — TV, desktop browser, phone, whatever the user picked
            // in Settings — so inheriting it via `em` means this page always matches the scale of
            // the rest of the app on that exact device, with zero platform-detection of our own.
            // Do not set an explicit font-size anywhere above #dpc-title, or the em chain breaks.
            // font-size:1.25em scales every text/button size in the left column by 1.25 (TV
            // feedback: too small from the sofa). Padding/gap are divided by 1.25 so the
            // column's outer spacing stays where it was. Phones reset it (their base is
            // already 1.5em, see the max-width:700px rule).
            sb.AppendLine("    '#dpc-l{flex:1;font-size:1.25em;padding:2.52em 2.34em 2.52em 3.6em;display:flex;flex-direction:column;justify-content:center;gap:1.08em;overflow-y:auto;min-width:0}',");
            sb.AppendLine("    '#dpc-logo{display:flex;align-items:center;gap:0.68em}',");
            sb.AppendLine("    '#dpc-logo-mark{width:1.73em;height:1.73em;flex-shrink:0}',");
            sb.AppendLine("    '#dpc-logo-mark svg{display:block;width:100%;height:100%}',");
            sb.AppendLine("    '#dpc-logo-text{font-weight:700;font-size:0.99em;letter-spacing:1.5px;color:var(--dpc-ink);text-transform:uppercase}',");
            sb.AppendLine("    '#dpc-logo-next{font-weight:400;color:var(--dpc-muted);letter-spacing:1.5px}',");
            sb.AppendLine("    '#dpc-title{font-size:2.25em;font-weight:700;color:var(--dpc-ink);line-height:1.25;margin:0;letter-spacing:-.4px}',");
            sb.AppendLine("    '#dpc-subtitle{font-size:0.99em;color:var(--dpc-body);line-height:1.6;margin:0;max-width:40ch}',");
            // No per-element entry animations anywhere on the page (TV performance): the old
            // staggered opacity+transform fades on logo/title/subtitle/buttons/steps/QR ran all
            // at once with the wall reveal and the QR build, and their `forwards` fill kept
            // every block a permanent composited layer. Only #dpc-w fades in (.3s opacity).
            // Also never animate a wrapper of the glass buttons: a running opacity/transform
            // animation makes it a backdrop root and the glass renders as a flat dark pill.
            sb.AppendLine("    '#dpc-actions{display:flex;flex-direction:column;gap:0.83em;margin-top:0.38em}',");
            // #dpc-btns shrinks to the widest button and stretches the others to it, so the
            // password and Telegram buttons are equal width (label left, arrow right). Its
            // own wrapper — not #dpc-actions — so a long #dpc-err line can't widen them.
            // No opacity/transform/filter here (backdrop root trap, see above).
            sb.AppendLine("    '#dpc-btns{display:flex;flex-direction:column;gap:0.83em;align-self:flex-start}#dpc-btns .dpc-b{align-self:stretch;justify-content:flex-start}#dpc-btns .dpc-b-arr{margin-left:auto}',");

            // Dark frosted glass (both the password button and the mobile Telegram button):
            // a dark translucent fill rgba(22,22,25,.38) over a strong backdrop-filter
            // blur(.73em ≈ 27px on a 1920 desktop; em so it scales with Lampa's size on TVs) of the posters behind (saturate/brightness lift them a bit, since
            // the left column sits under the dark #dpc-shade), a faint .1 white hairline border and a dim top
            // specular ::before line (no extra inset rims — they doubled the edge) — the "dark glass panel"
            // look, not a milky white card (a light .23 fill read as a grey slab on the
            // dark page). Proportions follow Lampa's .simple-button (2.8em tall). Trailing
            // arrow sits in its own small circle ("button-in-button"). The source design's
            // left-edge 1px ::after line is dropped: a pill's border-radius clips it into a
            // short mid-height tick that reads exactly like a render artifact.
            //
            // Focus styles are scoped to body:not(.mouse--controll). Lampa itself sets
            // body.mouse--controll (src/core/platform.js) when navigation is mouse/touch —
            // the default in a desktop browser and on phones — and leaves it off for remote
            // navigation (TV). Lampa.Controller puts .focus on the password button right on
            // load in every mode; without this scope the browser showed it pre-highlighted.
            // Mouse users get the same look via :hover instead. (This is input mode, not a
            // sizing branch — sizing stays purely em-based, see CLAUDE.md.)
            // Focus/hover = same glass fill as rest (on TV the password button is focused on
            // load, so a lighter focus fill meant TVs never showed the real glass), only a
            // brighter hairline (no glow); lock/plane icon animates. The arrow circle
            // stays as is — no white fill, no nudge.
            //
            // NEVER transform-scale these buttons (focus, hover or :active): Chrome leaves
            // thin stale slivers at the pill's former left/right edges after a scaled
            // element shrinks back, and backdrop-filter makes it worse. Press feedback is a
            // brighter fill instead of scale(.97).
            sb.AppendLine("    '.dpc-b{-webkit-appearance:none;appearance:none;display:inline-flex;align-items:center;justify-content:center;gap:.7em;width:auto;align-self:flex-start;height:2.9em;padding:0 .45em 0 1.3em;box-sizing:border-box;position:relative;overflow:hidden;border:1px solid rgba(255,255,255,.1);border-radius:999px;background:rgba(22,22,25,.38);-webkit-backdrop-filter:blur(.73em) saturate(1.6) brightness(1.3);backdrop-filter:blur(.73em) saturate(1.6) brightness(1.3);color:var(--dpc-ink);box-shadow:0 .35em 1.4em rgba(0,0,0,.25);font-family:inherit;font-size:1.3em;font-weight:600;white-space:nowrap;cursor:pointer;text-decoration:none;transition:background-color 160ms ease,border-color 160ms ease,color 160ms ease}',");
            // Specular top edge (the glass ::before highlight). Inset from the ends so it
            // stays on the straight part of the pill instead of cutting the rounded caps.
            sb.AppendLine("    '.dpc-b::before{content:\\'\\';position:absolute;top:0;left:14%;right:14%;height:1px;pointer-events:none;background:linear-gradient(90deg,rgba(255,255,255,0),rgba(255,255,255,.3),rgba(255,255,255,0))}',");
            sb.AppendLine("    '.dpc-b > svg{width:1.15em;height:1.15em;flex-shrink:0}.dpc-b > img{width:1.3em;height:1.3em;flex-shrink:0}',");
            // No backdrop-filter (Chromium < 76: Tizen ≤5.5, webOS ≤5): the .38 fill alone is
            // a see-through smear over the posters — use a near-opaque dark fill instead.
            sb.AppendLine("    '@supports not ((backdrop-filter:blur(1px)) or (-webkit-backdrop-filter:blur(1px))){.dpc-b{background:rgba(24,24,28,.86)}}',");
            // Hairline border: 1 CSS px is 2 device px on a dpr-2 screen (the TV WebView), so
            // go to .5px there — one physical pixel, same as on a dpr-1 desktop.
            sb.AppendLine("    '@media(-webkit-min-device-pixel-ratio:2),(min-resolution:2dppx){.dpc-b{border-width:.5px}}',");
            sb.AppendLine("    '.dpc-b-arr{display:inline-flex;align-items:center;justify-content:center;width:2em;height:2em;flex-shrink:0;border-radius:50%;background:rgba(255,255,255,.12)}',");
            sb.AppendLine("    '.dpc-b-arr svg{width:.95em;height:.95em}',");
            sb.AppendLine("    '#dpc-btn:disabled{opacity:.45;cursor:default}',");
            // Lock "opens" on hover/focus: the shackle pivots on its left leg (8,11 in the
            // 24-unit viewBox) so the right leg swings up out of the body — the standard
            // unlocked-padlock silhouette. transform only; SVG user units == CSS px here.
            sb.AppendLine("    '.dpc-shackle{transform-origin:8px 11px;transition:transform 220ms var(--dpc-ease-out)}',");
            sb.AppendLine("    'body:not(.mouse--controll) .dpc-b.focus .dpc-shackle{transform:translateY(-1px) rotate(-18deg)}',");
            sb.AppendLine("    '@media(hover:hover) and (pointer:fine){.dpc-b:not(:disabled):hover .dpc-shackle{transform:translateY(-1px) rotate(-18deg)}}',");
            // Telegram counterpart of the opening lock: the paper plane "takes off" up and to
            // the right on hover/focus. Transform on the icon only — never the button itself.
            sb.AppendLine("    '.dpc-plane{transition:transform 220ms var(--dpc-ease-out)}',");
            sb.AppendLine("    'body:not(.mouse--controll) .dpc-b.focus .dpc-plane{transform:translate(2px,-2px)}',");
            sb.AppendLine("    '@media(hover:hover) and (pointer:fine){.dpc-b:not(:disabled):hover .dpc-plane{transform:translate(2px,-2px)}}',");
            sb.AppendLine("    'body:not(.mouse--controll) .dpc-b.focus{border-color:rgba(255,255,255,.45);outline:none}',");
            sb.AppendLine("    '@media(hover:hover) and (pointer:fine){.dpc-b:not(:disabled):hover{border-color:rgba(255,255,255,.45)}}',");
            sb.AppendLine("    '.dpc-b:focus{outline:none}',");
            sb.AppendLine("    '.dpc-b:not(:disabled):active{background:rgba(78,78,86,.6)}',");
            // Mobile-only "log in via Telegram" button — hidden unless the QR block is (see
            // the no-QR @media rule below).
            sb.AppendLine("    '#dpc-tgbtn{display:none}.dpc-web #dpc-tgbtn{display:inline-flex}',");
            // 0.9em, not smaller: this line carries "wrong password"/"connection error" and has
            // to be readable from a couch on TV.
            sb.AppendLine("    '#dpc-err{font-size:0.9em;min-height:1.5em;line-height:1.5;padding-left:.2em;color:var(--dpc-err);transition:color 160ms ease}',");
            // New-account password: shown once and never recoverable, so it gets its own
            // block with the value in monospace instead of being squeezed into #dpc-err.
            sb.AppendLine("    '#dpc-newpass{display:none;flex-direction:column;gap:.35em;align-self:flex-start;padding:.9em 1.2em;border-radius:1em;background:rgba(var(--dpc-acc-rgb),.1);box-shadow:inset 0 0 0 1px rgba(var(--dpc-acc-rgb),.35)}',");
            sb.AppendLine("    '#dpc-newpass.show{display:flex;animation:dpcStagger .35s var(--dpc-ease-out)}',");
            sb.AppendLine("    '#dpc-newpass-l{font-size:.8em;color:var(--dpc-body);letter-spacing:.02em}',");
            sb.AppendLine("    '#dpc-newpass-v{font-family:ui-monospace,\"JetBrains Mono\",Consolas,monospace;font-size:1.6em;font-weight:700;color:var(--dpc-ink);letter-spacing:.08em}',");

            // Step list — plain text lines, no numbered badge (the number added nothing;
            // two short lines already read in order without it).
            sb.AppendLine("    '#dpc-steps{display:flex;flex-direction:column;gap:0.7em;margin-top:0.68em;padding-top:1.35em;border-top:1px solid rgba(255,255,255,.08)}',");
            sb.AppendLine("    '#dpc-steps .dpc-step-t{font-size:0.99em;color:var(--dpc-body);line-height:1.6;max-width:42ch}',");

            // Right column (QR)
            sb.AppendLine("    '#dpc-r{width:30em;box-sizing:border-box;flex-shrink:0;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:1em;padding:3em 4.5em 3em 3em;text-align:center}',");
            // No backdrop-filter here — it's the single most common cause of jank on
            // weak TV WebKit (Tizen/webOS): every frame, the browser has to re-sample
            // and blur whatever sits behind this element (the animated/blurred #dpc-bg
            // wash right underneath it) in real time, often falling back to slow
            // software compositing where GPU support is spotty. Plain translucent
            // rgba() gives the same "not flashy white" result at essentially zero cost
            // — no blur-of-what's-behind, just a flat semi-transparent fill.
            // "Double bezel": a dark tray (#dpc-qr-wrap) with a hairline, holding an opaque
            // white plate (#dpc-qr-plate) with a concentric smaller radius (tray radius
            // minus tray padding). The code stays max-contrast black-on-white, but reads
            // as a machined part of the UI rather than a sticker slapped on the posters.
            sb.AppendLine("    '#dpc-qr-wrap{width:22em;height:22em;flex-shrink:0;padding:.55em;box-sizing:border-box;border-radius:1.9em;background:rgba(10,10,11,.55);box-shadow:inset 0 0 0 1px rgba(255,255,255,.12)}',");
            sb.AppendLine("    '#dpc-qr-plate{position:relative;overflow:hidden;width:100%;height:100%;padding:.8em;box-sizing:border-box;border-radius:1.35em;background:var(--dpc-paper);box-shadow:inset 0 1px 0 rgba(255,255,255,.9)}',");
            // Skeleton sweep while the QR library loads — replaces the old empty white box.
            sb.AppendLine("    '#dpc-qr-plate.loading::after{content:\\'\\';position:absolute;top:0;right:0;bottom:0;left:0;background:linear-gradient(100deg,transparent 30%,rgba(0,0,0,.07) 50%,transparent 70%);animation:dpcSweep 1.3s ease-in-out infinite}',");
            sb.AppendLine("    '#dpc-qr-box{position:relative;width:100%;height:100%}',");
            // qr-code-styling рисует SVG с фиксированным пиксельным width/height, снятым один раз
            // при построении (container.clientWidth), и НЕ проставляет viewBox вообще (проверено
            // в живом DOM: svg.getAttribute('viewBox') === null). Без viewBox форсирование
            // width/height:100% через CSS не масштабирует уже нарисованные пути — оно просто меняет
            // видимый viewport, а координаты точек остаются в исходных пиксельных юнитах со сборки,
            // из-за чего QR "съезжает"/не совпадает с белой подложкой после изменения окна (сама
            // разметка не перестраивается, а слушателя на resize нет и не должно быть). Раньше тут
            // был комментарий про "сохранение viewBox" — но сохранять было нечего, его никогда не
            // было. Реальный фикс: renderQr() сам проставляет viewBox сразу после rendering (см.
            // ниже) — тогда браузер honestly масштабирует контент под текущий размер контейнера.
            sb.AppendLine("    '#dpc-qr-box svg,#dpc-qr-box canvas{display:block!important;width:100%!important;height:100%!important}',");
            sb.AppendLine("    '#dpc-qr-box img{display:block;width:100%;height:auto;border-radius:4px;mix-blend-mode:multiply}',");
            // Same body-text treatment as #dpc-subtitle on the left — regular
            // weight, same muted color, same line-height — so descriptive text reads as
            // one consistent style across both columns instead of the right side being
            // bolder/brighter/differently-aligned. The shadow is only for legibility over
            // the bright gradient backdrop here, not a weight/emphasis choice.
            // Caption only, centered under the QR (the round Telegram link next to it was
            // removed: on TV it was a useless focus stop, in a browser it duplicated
            // #dpc-tgbtn). Same width as the QR tray (22em) so the default caption wraps into
            // 2 lines; text-wrap:balance (no-op on old engines) evens them out.
            sb.AppendLine("    '#dpc-qr-cta{width:100%;max-width:22em}',");
            sb.AppendLine("    '#dpc-qrsub{font-size:0.92em;font-weight:400;color:var(--dpc-body);line-height:1.5;text-align:center;text-wrap:balance;text-shadow:0 1px 3px rgba(0,0,0,.4)}',");

            // Responsive — layout reflow only (two columns → stacked), never sizing: sizing is
            // already fluid via em/Lampa's body font-size above, so there's nothing left to guess
            // per breakpoint. This is the ordinary "content needs a different layout below N px"
            // case (web.dev's own recommended reason to add a breakpoint at all), not a device
            // detection — see docs/auth-ux-guidelines.md §9.
            // The ONE sizing exception: Lampa's size() clamps body to its 10.6px floor on
            // every viewport under ~892px, so on a phone the em chain bottoms out at 10.6px
            // (Lampa's own UI compensates with its own mobile CSS; this page has no such
            // layer and rendered tiny — 31px-tall buttons, 10px body text). Below 700px the
            // clamp is always active, so 1.5em here is a fixed ~16px base, not a device guess.
            // Content is vertically centered: #dpc-w is a flex column with min-height:100% and
            // #dpc-content grows into it (a plain min-height:100% on #dpc-content didn't resolve
            // against #dpc-w's auto height, so short content — the ban screen — sat at the
            // top). A tall form still grows and scrolls in #dpc instead of being clipped.
            sb.AppendLine("    '@media(max-width:700px){#dpc{align-items:flex-start;font-size:1.5em}#dpc-w{height:auto;min-height:100%;display:flex;flex-direction:column}#dpc-content{flex:1 0 auto;flex-direction:column;justify-content:center;height:auto}#dpc-l{flex:0 0 auto;overflow:visible;font-size:1em;padding:2.5em 1.5em}#dpc-r{flex:0 0 auto;width:100%}#dpc-btns{align-self:stretch}.dpc-b{width:100%;font-size:1.05em}}',");
            // No-QR mode: a QR on the very phone that would have to scan it is useless, so on
            // phones the whole QR column is swapped for a plain "log in via Telegram" button
            // in the action list — same session deep link, same polling, the user just taps
            // instead of scanning. Two triggers: a narrow viewport (portrait phone), or a
            // short touch viewport (landscape phone — wide enough for two columns, but still
            // a phone). This is a content choice, not sizing — sizing stays em-based.
            // TVs/desktops never match: their CSS viewport is ≥540px tall.
            if (hasTg && conf.show_qr)
                sb.AppendLine("    '@media(max-width:700px),(max-height:480px) and (pointer:coarse){#dpc-r{display:none}#dpc-tgbtn{display:inline-flex;order:-1}}',");

            // Reduced motion
            sb.AppendLine("    '@media(prefers-reduced-motion:reduce){.dpc-shackle,.dpc-plane{transition:none}#dpc-w,#dpc-logo,#dpc-title,#dpc-subtitle,#dpc-steps,#dpc-qr-wrap,#dpc-qrsub,#dpc-newpass{animation:none!important;opacity:1!important;transform:none!important}#dpc-qr-wrap.loading::after{animation:none!important}.dpc-b{animation:none!important}}',");

            // Focus for .dpc-b lives with its base styles above (white-fill swap). The round
            // Telegram icon in the QR column can't swap colours, so it gets a ring instead.

            sb.AppendLine("    '.settings-input{z-index:100000!important}',");
            sb.AppendLine("    '.selectbox{z-index:100001!important}',");


            sb.AppendLine("  ].join('');");
            sb.AppendLine("  document.head.appendChild(s);");
            sb.AppendLine("})();");
            sb.AppendLine();

            // ── dpcWallMount ─────────────────────────────────────────────────
            // Poster wall mounted into the #dpc-posters/#dpc-shade layers. Shared by
            // addDevice() and showBlocked(); call it right after #dpc is in the DOM.
            if (conf.poster_wall)
            {
                sb.AppendLine("function dpcWallMount() {");
                sb.AppendLine("  var box = document.getElementById('dpc-posters'), w = document.getElementById('dpc-w');");
                sb.AppendLine("  if (!box || !w) return;");
                // Portrait wall on phones; on wide screens the 4K file when the screen is wider
                // than ~2000 device px (2K/4K monitors, 4K TVs whose WebView reports a high
                // devicePixelRatio). The server falls back to 1080p if the requested one is
                // missing. ?v= is the set version, so a repeat visit is a cache hit.
                sb.AppendLine("  var portrait = window.innerWidth <= 700;");
                sb.AppendLine("  function wallUrl(v) { return '{localhost}/tgbot/qr/wall?v=' + v + (portrait ? '&s=m' : window.innerWidth * (window.devicePixelRatio || 1) > 2000 ? '&s=4k' : ''); }");
                // No wall known when deny.js was generated (first fetch not done yet), or the
                // inline one failed (stale deny.js after a refresh): ask the manifest and show
                // the wall once it has loaded. wall=false → the plain gradient stays.
                sb.AppendLine("  function dpcPosters() {");
                sb.AppendLine("    (new Lampa.Reguest()).silent('{localhost}/tgbot/qr/posters', function(res) {");
                sb.AppendLine("      if (!res || !res.wall) return;");
                sb.AppendLine("      var src = wallUrl(res.v), img = new Image();");
                sb.AppendLine("      img.onload = function() {");
                sb.AppendLine("        box.style.backgroundImage = 'url(\"' + src + '\")';");
                sb.AppendLine("        if (w.className.indexOf('dpc-has-posters') < 0) w.className += ' dpc-has-posters';");
                sb.AppendLine("      };");
                sb.AppendLine("      img.src = src;");
                sb.AppendLine("    }, function() {});");
                sb.AppendLine("  }");
                if (hasWall)
                {
                    // Direct wall mode: the preview (inline, ~1KB) sits under the wall in the same
                    // background, so the wall's colours are on screen from the first paint and the
                    // full image paints over it when decoded. Shade on immediately, no fade.
                    sb.AppendLine("  var dpcWall = wallUrl(" + PosterWall.Version + ");");
                    sb.AppendLine("  var dpcWallImg = new Image();");
                    sb.AppendLine("  dpcWallImg.src = dpcWall;");
                    sb.AppendLine("  var dpcLqip = portrait ? " + Js(wallLqipPortrait ?? "") + " : " + Js(wallLqip ?? "") + ";");
                    sb.AppendLine("  box.style.backgroundImage = 'url(\"' + dpcWall + '\")' + (dpcLqip ? ', url(data:image/jpeg;base64,' + dpcLqip + ')' : '');");
                    sb.AppendLine("  w.className += ' dpc-has-posters';");
                    if (wallGlass != null)
                    {
                        // TV/desktop only — the glass image is cut from the landscape wall; phones
                        // keep the live backdrop-filter over the portrait wall.
                        // Wall is drawn "cover" into #dpc-w: scale max(W/1920, H/1080), centred. Each
                        // visible button gets: tint, the shade gradients at screen size, the glass
                        // image at wall-cover size, all offset by the button's padding-box
                        // position, so its background is exactly what sits behind it, blurred.
                        sb.AppendLine("  if (!portrait) {");
                        sb.AppendLine("    var dpcGlassImg = 'linear-gradient(rgba(22,22,25,.38),rgba(22,22,25,.38)),' + " + Js(Shade) + " + ',url(data:image/jpeg;base64," + wallGlass + ")';");
                        sb.AppendLine("    var dpcGlassFit = function() {");
                        sb.AppendLine("      if (w.className.indexOf('dpc-glass') < 0) return;");
                        sb.AppendLine("      var W = w.clientWidth, H = w.clientHeight, wr = w.getBoundingClientRect();");
                        sb.AppendLine("      var k = Math.max(W / 1920, H / 1080), cw = 1920 * k, ch = 1080 * k;");
                        sb.AppendLine("      var bs = w.querySelectorAll('.dpc-b');");
                        sb.AppendLine("      for (var i = 0; i < bs.length; i++) {");
                        sb.AppendLine("        var b = bs[i];");
                        sb.AppendLine("        if (!b.offsetWidth) continue;");
                        sb.AppendLine("        var r = b.getBoundingClientRect(), x = r.left - wr.left + b.clientLeft, y = r.top - wr.top + b.clientTop;");
                        sb.AppendLine("        var scr = W + 'px ' + H + 'px', at = (-x) + 'px ' + (-y) + 'px';");
                        sb.AppendLine("        if (!b._dpcGlass) { b.style.backgroundImage = dpcGlassImg; b._dpcGlass = 1; }");
                        sb.AppendLine("        b.style.backgroundSize = '100% 100%,' + scr + ',' + scr + ',' + scr + ',' + cw + 'px ' + ch + 'px';");
                        sb.AppendLine("        b.style.backgroundPosition = '0 0,' + at + ',' + at + ',' + at + ',' + ((W - cw) / 2 - x) + 'px ' + ((H - ch) / 2 - y) + 'px';");
                        sb.AppendLine("      }");
                        sb.AppendLine("    };");
                        sb.AppendLine("    w.className += ' dpc-glass';");
                        sb.AppendLine("    dpcGlassFit();");
                        // Re-fit when the layout moves: resize, the error/new-password block
                        // appearing (re-centres the column), late font swap.
                        sb.AppendLine("    window.addEventListener('resize', dpcGlassFit);");
                        sb.AppendLine("    setTimeout(dpcGlassFit, 300);");
                        sb.AppendLine("    var dpcActs = document.getElementById('dpc-actions'); if (dpcActs && window.ResizeObserver) { var dpcRo = new ResizeObserver(function() { dpcGlassFit(); }); dpcRo.observe(dpcActs); }");
                        sb.AppendLine("  }");
                    }
                    sb.AppendLine("  dpcWallImg.onerror = function() {");
                    sb.AppendLine("    box.style.backgroundImage = '';");
                    sb.AppendLine("    w.className = w.className.replace(' dpc-has-posters', '').replace(' dpc-glass', '');");
                    sb.AppendLine("    var bs = w.querySelectorAll('.dpc-b');");
                    sb.AppendLine("    for (var i = 0; i < bs.length; i++) { bs[i].style.backgroundImage = bs[i].style.backgroundSize = bs[i].style.backgroundPosition = ''; bs[i]._dpcGlass = 0; }");
                    sb.AppendLine("    dpcPosters();");
                    sb.AppendLine("  };");
                }
                else
                {
                    sb.AppendLine("  dpcPosters();");
                }
                sb.AppendLine("}");
                sb.AppendLine();
            }

            // ── addDevice ────────────────────────────────────────────────────
            // ── dpcViewportCover ─────────────────────────────────────────────
            // iOS Safari in landscape lays the page out only between the safe areas (notch,
            // rounded corners) and paints the strips left and right in a flat colour — the
            // poster wall ended short of the screen edges. viewport-fit=cover lets #dpc (fixed,
            // full-screen) run edge to edge. Added to the existing viewport meta only for as
            // long as the login page is up (a successful login reloads the page); no meta is
            // created where there is none, so TV/desktop scaling is left alone.
            sb.AppendLine("function dpcViewportCover() {");
            sb.AppendLine("  var m = document.querySelector('meta[name=viewport]');");
            sb.AppendLine("  if (m && (m.getAttribute('content') || '').indexOf('viewport-fit') < 0) m.setAttribute('content', m.getAttribute('content') + ', viewport-fit=cover');");
            sb.AppendLine("}");
            sb.AppendLine();

            sb.AppendLine("function addDevice(message) {");
            sb.AppendLine("  if (document.getElementById('dpc')) return;");
            sb.AppendLine("  dpcViewportCover();");
            sb.AppendLine();

            sb.AppendLine("  var svgLock = '<svg width=\"17\" height=\"17\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><rect x=\"5\" y=\"11\" width=\"14\" height=\"9\" rx=\"2\"/><path class=\"dpc-shackle\" d=\"M8 11V7a4 4 0 0 1 8 0v4\"/></svg>';");
            // Telegram paper plane: Lucide "send" (ISC license), same monochrome outline style
            // as the lock (currentColor, 1.5 stroke) — the brand-blue PNG looked out of place
            // on the glass.
            sb.AppendLine("  var svgTg = '<svg class=\"dpc-plane\" width=\"17\" height=\"17\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path d=\"M14.536 21.686a.5.5 0 0 0 .937-.024l6.5-19a.496.496 0 0 0-.635-.635l-19 6.5a.5.5 0 0 0-.024.937l7.93 3.18a2 2 0 0 1 1.112 1.11z\"/><path d=\"m21.854 2.147-10.94 10.939\"/></svg>';");
            // Настоящий значок Lampa (концентрические кольца) — взят из иконки Tizen/webOS
            // виджета (lampac/Modules/LampaWeb/widgets/samsung|lg/app/img/logo-icon.svg),
            // это официальный app-icon клиента — используется только в лого шапки, где
            // бренд должен быть узнаваем.
            sb.AppendLine("  var svgLampaIcon = '<path d=\"M81.6744 103.11C98.5682 93.7234 110 75.6967 110 55C110 24.6243 85.3757 0 55 0C24.6243 0 0 24.6243 0 55C0 75.6967 11.4318 93.7234 28.3255 103.11C14.8869 94.3724 6 79.224 6 62C6 34.938 27.938 13 55 13C82.062 13 104 34.938 104 62C104 79.224 95.1131 94.3725 81.6744 103.11Z\" fill=\"#fff\"/><path d=\"M92.9546 80.0076C95.5485 74.5501 97 68.4446 97 62C97 38.804 78.196 20 55 20C31.804 20 13 38.804 13 62C13 68.4446 14.4515 74.5501 17.0454 80.0076C16.3618 77.1161 16 74.1003 16 71C16 49.4609 33.4609 32 55 32C76.5391 32 94 49.4609 94 71C94 74.1003 93.6382 77.1161 92.9546 80.0076Z\" fill=\"#fff\"/><path d=\"M55 89C69.3594 89 81 77.3594 81 63C81 57.9297 79.5486 53.1983 77.0387 49.1987C82.579 54.7989 86 62.5 86 71C86 88.1208 72.1208 102 55 102C37.8792 102 24 88.1208 24 71C24 62.5 27.421 54.7989 32.9613 49.1987C30.4514 53.1983 29 57.9297 29 63C29 77.3594 40.6406 89 55 89Z\" fill=\"#fff\"/><path d=\"M73 63C73 72.9411 64.9411 81 55 81C45.0589 81 37 72.9411 37 63C37 53.0589 45.0589 45 55 45C64.9411 45 73 53.0589 73 63Z\" fill=\"#fff\"/>';");
            sb.AppendLine("  var svgArrow = '<span class=\"dpc-b-arr\"><svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.75\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path d=\"M5 12h14\"/><path d=\"M13 6l6 6-6 6\"/></svg></span>';");
            sb.AppendLine("  var svgLogoMark = '<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 110 104\">' + svgLampaIcon + '</svg>';");
            sb.AppendLine();

            // Скруглённые точки/логотип по центру QR даёт только клиентская отрисовка —
            // api.qrserver.com отдаёт только плоскую растровую картинку без стилизации.
            // Библиотека грузится лениво (лежит на CDN, не бандлится в плагин).
            // renderQr() is called twice for the same container: once immediately with the
            // static tgUrl, then again from startQrAuth() once the session dynUrl is back.
            // Both paths are async (CDN script load / fetch round-trip), so without a guard
            // whichever settles second wins the write but doesn't cancel the other's in-flight
            // load — two <script> tags, two onloads, two qr.append(container) calls with no
            // clear between them, producing two overlapping/offset QR codes. A per-container
            // generation counter makes a stale call's build()/fallback() a no-op, and a single
            // shared CDN-load promise (instead of one <script> tag per call) means only the
            // winning generation's build() ever runs.
            sb.AppendLine("  function renderQr(container, url) {");
            sb.AppendLine("    var gen = (container._dpcQrGen = (container._dpcQrGen || 0) + 1);");
            sb.AppendLine("    var wrap = container.parentNode;");
            sb.AppendLine("    if (wrap && !container.firstChild) wrap.classList.add('loading');");
            sb.AppendLine("    function done() { if (wrap) wrap.classList.remove('loading'); }");
            sb.AppendLine("    function fallback() {");
            sb.AppendLine("      if (container._dpcQrGen !== gen) return;");
            sb.AppendLine("      container.innerHTML = '';");
            sb.AppendLine("      container.insertAdjacentHTML('beforeend',");
            sb.AppendLine("        '<img src=\"https://api.qrserver.com/v1/create-qr-code/?size=" + qrSize + "x" + qrSize + "&ecc=M&margin=4&data=' + encodeURIComponent(url) + '\" loading=\"lazy\" />');");
            sb.AppendLine("      done();");
            sb.AppendLine("    }");
            sb.AppendLine("    function build() {");
            sb.AppendLine("      if (container._dpcQrGen !== gen) return;");
            sb.AppendLine("      try {");
            sb.AppendLine("        container.innerHTML = '';");
            sb.AppendLine("        var size = Math.round((container.clientWidth || 162) * (window.devicePixelRatio || 1));");
            sb.AppendLine("        var qr = new QRCodeStyling({");
            sb.AppendLine("          width: size,");
            sb.AppendLine("          height: size,");
            // canvas, not svg: the rounded-dot SVG is ~1000 path nodes the TV re-rasterizes
            // whenever its layer is touched; a canvas is one bitmap drawn once. Backing store
            // in device pixels so it stays crisp; CSS scales it to the box.
            sb.AppendLine("          type: 'canvas',");
            sb.AppendLine("          data: url,");
            sb.AppendLine("          margin: 2,");
            // Минимализм в QR — это ОДИН плоский фирменный цвет, не двухцветный градиент:
            // на мелких модулях смена оттенка через паттерн читается пёстро/шумно, а не
            // премиально. Один глубокий фиолетовый тон — тот же принцип, что "тёмные модули
            // на светлом" (максимальный контраст/сканируемость), но в фирменном оттенке
            // вместо нейтрального угольного.
            // EC level H (~30% redundancy) was needed only to survive the center-logo
            // overlay this QR used to have — that logo is gone now (see history), so the
            // extra redundancy has no logo to protect against. M (~15%, the library's own
            // default) is still fine for a clean, unobstructed code, and needs fewer
            // modules for the same data — cheaper for the JS engine to build the SVG path
            // set on weak TV hardware, and less visually dense for the same physical box.
            sb.AppendLine("          qrOptions: { errorCorrectionLevel: 'M' },");
            sb.AppendLine("          dotsOptions: { type: 'rounded', color: '#1e1f21' },");
            sb.AppendLine("          cornersSquareOptions: { type: 'extra-rounded', color: '#1e1f21' },");
            sb.AppendLine("          cornersDotOptions: { type: 'dot', color: '#1e1f21' },");
            // Transparent SVG background lets #dpc-qr-box's frosted-glass CSS be the actual
            // paper the dots sit on, instead of a flat opaque white square baked into the code.
            sb.AppendLine("          backgroundOptions: { color: 'transparent' }");
            sb.AppendLine("        });");
            sb.AppendLine("        qr.append(container);");
            // qr-code-styling's <svg> has no viewBox (see #dpc-qr-box svg CSS comment above) —
            // add one ourselves so the width/height:100% CSS actually rescales the drawn QR
            // instead of just resizing an unscaled viewport around static-coordinate paths.
            sb.AppendLine("        var builtSvg = container.querySelector('svg');");
            sb.AppendLine("        if (builtSvg && !builtSvg.getAttribute('viewBox')) builtSvg.setAttribute('viewBox', '0 0 ' + size + ' ' + size);");
            sb.AppendLine("        done();");
            sb.AppendLine("      } catch (e) { fallback(); }");
            sb.AppendLine("    }");
            sb.AppendLine("    if (window.QRCodeStyling) { build(); return; }");
            sb.AppendLine("    if (!window._dpcQrLoad) {");
            sb.AppendLine("      window._dpcQrLoad = new Promise(function(resolve, reject) {");
            sb.AppendLine("        var sc = document.createElement('script');");
            sb.AppendLine("        sc.src = 'https://cdn.jsdelivr.net/npm/qr-code-styling@1.6.0-rc.1/lib/qr-code-styling.js';");
            sb.AppendLine("        sc.onload = resolve;");
            sb.AppendLine("        sc.onerror = reject;");
            sb.AppendLine("        document.head.appendChild(sc);");
            sb.AppendLine("      });");
            sb.AppendLine("    }");
            sb.AppendLine("    window._dpcQrLoad.then(build, fallback);");
            sb.AppendLine("  }");
            sb.AppendLine();

            // Left column HTML
            // Текстовые узлы оставлены пустыми и заполняются через textContent ниже —
            // insertAdjacentHTML не должен получать значения из init.conf напрямую (XSS).
            sb.AppendLine("  var leftHtml = ''");
            sb.AppendLine("    + '<div id=\"dpc-l\">'");
            sb.AppendLine("    + '<div id=\"dpc-logo\"><span id=\"dpc-logo-mark\">' + svgLogoMark + '</span><span id=\"dpc-logo-text\">Lampac<span id=\"dpc-logo-next\"></span></span></div>'");
            sb.AppendLine("    + '<h1 id=\"dpc-title\"></h1>'");
            sb.AppendLine("    + '<p id=\"dpc-subtitle\"></p>'");
            sb.AppendLine("    + '<div id=\"dpc-actions\">'");
            sb.AppendLine("    + '<div id=\"dpc-btns\">'");
            sb.AppendLine("    + '<button id=\"dpc-btn\" type=\"button\" class=\"dpc-b selector\">' + svgLock + '<span id=\"dpc-btn-text\">Войти по паролю</span>' + svgArrow + '</button>'");
            // Mobile stand-in for the QR column — hidden by CSS unless the no-QR @media
            // rule matches, or the page runs in a plain browser (.dpc-web, see below).
            // After #dpc-btn in DOM order so the TV's default focus stays
            // on the password button; on phones CSS `order:-1` shows it first instead, since
            // there Telegram is the primary method and the password the fallback.
            if (hasTg && conf.show_qr)
            {
                sb.AppendLine("    + '<a id=\"dpc-tgbtn\" class=\"dpc-b selector\" target=\"_blank\" rel=\"noopener\">' + svgTg + '<span id=\"dpc-tgbtn-text\"></span>' + svgArrow + '</a>'");
            }
            sb.AppendLine("    + '</div>'");
            sb.AppendLine("    + '<div id=\"dpc-newpass\"><span id=\"dpc-newpass-l\">Ваш пароль — запишите, он больше не будет показан</span><span id=\"dpc-newpass-v\"></span></div>'");
            sb.AppendLine("    + '<div id=\"dpc-err\"></div>'");
            sb.AppendLine("    + '</div>'");
            if (step1.Length > 0 || step2.Length > 0)
            {
                sb.AppendLine("    + '<div id=\"dpc-steps\">'");
                if (step1.Length > 0) sb.AppendLine("    + '<div class=\"dpc-step-t\" id=\"dpc-step1\"></div>'");
                if (step2.Length > 0) sb.AppendLine("    + '<div class=\"dpc-step-t\" id=\"dpc-step2\"></div>'");
                sb.AppendLine("    + '</div>'");
            }
            sb.AppendLine("    + '</div>';");
            sb.AppendLine();

            // Right column HTML (only if tg + show_qr) — QR image, then the caption under it.
            if (hasTg && conf.show_qr)
            {
                sb.AppendLine("  var tgUrl  = " + jsTgUrl + ";");
                sb.AppendLine("  var rightHtml = ''");
                sb.AppendLine("    + '<div id=\"dpc-r\">'");
                sb.AppendLine("    + '<div id=\"dpc-qr-wrap\"><div id=\"dpc-qr-plate\"><div id=\"dpc-qr-box\"></div></div></div>'");
                sb.AppendLine("    + '<div id=\"dpc-qr-cta\">'");
                sb.AppendLine("    + '<div id=\"dpc-qrsub\"></div>'");
                sb.AppendLine("    + '</div>'");
                sb.AppendLine("    + '</div>';");
            }
            else
            {
                sb.AppendLine("  var rightHtml = '';");
            }

            sb.AppendLine();
            sb.AppendLine("  var html = '<div id=\"dpc\"><div id=\"dpc-w\">" + posterLayers + "<div id=\"dpc-bg\"></div><div id=\"dpc-content\">' + leftHtml + rightHtml + '</div></div></div>';");
            sb.AppendLine("  document.body.insertAdjacentHTML('beforeend', html);");
            sb.AppendLine();

            if (conf.poster_wall)
                sb.AppendLine("  dpcWallMount();");
            sb.AppendLine();

            // Текст из init.conf проставляется через textContent/href, а не в разметку —
            // так браузер сам экранирует HTML-спецсимволы вместо ручного экранирования.
            sb.AppendLine("  document.getElementById('dpc-logo-next').textContent = ' NextGen';");
            sb.AppendLine("  document.getElementById('dpc-title').textContent = " + jsTitle + ";");
            sb.AppendLine("  document.getElementById('dpc-subtitle').textContent = " + jsSub + ";");
            if (step1.Length > 0) sb.AppendLine("  document.getElementById('dpc-step1').textContent = " + Js(step1) + ";");
            if (step2.Length > 0) sb.AppendLine("  document.getElementById('dpc-step2').textContent = " + Js(step2) + ";");
            sb.AppendLine();
            if (hasTg && conf.show_qr)
            {
                sb.AppendLine("  document.getElementById('dpc-qrsub').textContent = " + jsQrSub + ";");
                sb.AppendLine("  document.getElementById('dpc-tgbtn-text').textContent = " + jsTgBtn + ";");
                sb.AppendLine("  document.getElementById('dpc-tgbtn').href = tgUrl;");
                // Plain desktop browser (not tizen/webos/android): Telegram Desktop can open
                // the deep link right here, so the button shows under the password one, next
                // to the QR. On TVs there is no Telegram — the QR stays the only way. This is
                // a content choice (like the no-QR phone rule), not a sizing branch.
                sb.AppendLine("  if (!(window.Lampa && Lampa.Platform && Lampa.Platform.any())) { var _dw = document.getElementById('dpc-w'); if (_dw) _dw.className += ' dpc-web'; }");
                sb.AppendLine("  renderQr(document.getElementById('dpc-qr-box'), tgUrl);");
                sb.AppendLine();

                // ── QR login handshake ────────────────────────────────────────
                // Static tgUrl above is only the pre-session fallback (shown instantly,
                // and kept as the link if the bot module is disabled/unreachable — same
                // behavior as before this block existed). startQrAuth() asks the bot
                // module for a fresh pairing session (services/qrauthsessions.cs), swaps
                // the QR/pill href for the session-bound deep link (?start=qr_<id>), then
                // polls for confirmation. Once BotSession's "✅ Подтвердить вход" button
                // confirms it, the poll gets back the user's existing Lampac token and
                // logs in with it through the exact same doLogin() path as a typed
                // password — the bot token IS the password (see usersrepository.cs).
                sb.AppendLine("  var qrSessionId = null;");
                sb.AppendLine("  var qrPollTimer = null;");
                sb.AppendLine("  var qrStartPending = false;");
                sb.AppendLine();
                sb.AppendLine("  function stopQrPoll() {");
                sb.AppendLine("    if (qrPollTimer) { clearInterval(qrPollTimer); qrPollTimer = null; }");
                sb.AppendLine("  }");
                sb.AppendLine();
                sb.AppendLine("  function pollQrSession() {");
                sb.AppendLine("    stopQrPoll();");
                sb.AppendLine("    qrPollTimer = setInterval(function() {");
                sb.AppendLine("      if (!qrSessionId) return;");
                sb.AppendLine("      var qrNet = new Lampa.Reguest();");
                sb.AppendLine("      qrNet.silent('{localhost}/tgbot/qr/status?session=' + encodeURIComponent(qrSessionId), function(res) {");
                sb.AppendLine("        if (res && res.status === 'confirmed' && res.token) {");
                sb.AppendLine("          stopQrPoll();");
                sb.AppendLine("          if (_btn && !_btn.disabled) doLogin(res.token, true);");
                sb.AppendLine("        } else if (res && res.status === 'expired') {");
                sb.AppendLine("          stopQrPoll();");
                sb.AppendLine("          startQrAuth();");
                sb.AppendLine("        }");
                sb.AppendLine("      }, function() {});");
                sb.AppendLine("    }, 2000);");
                sb.AppendLine("  }");
                sb.AppendLine();
                sb.AppendLine("  function startQrAuth() {");
                sb.AppendLine("    if (qrStartPending) return;");
                sb.AppendLine("    qrStartPending = true;");
                sb.AppendLine("    var qrNet = new Lampa.Reguest();");
                sb.AppendLine("    qrNet.silent('{localhost}/tgbot/qr/start', function(res) {");
                sb.AppendLine("      qrStartPending = false;");
                sb.AppendLine("      if (!res || !res.session) return;");
                sb.AppendLine("      qrSessionId = res.session;");
                // tg_ vs qr_ only changes the bot's wording (button tap vs scan)
                sb.AppendLine("      var baseUrl = tgUrl.split('?')[0];");
                sb.AppendLine("      var dynUrl = baseUrl + '?start=qr_' + qrSessionId;");
                sb.AppendLine("      if (_tgbtn) _tgbtn.href = baseUrl + '?start=tg_' + qrSessionId;");
                sb.AppendLine("      renderQr(document.getElementById('dpc-qr-box'), dynUrl);");
                sb.AppendLine("      pollQrSession();");
                // Bot module off/unreachable: the static tgUrl QR still opens the bot, so no
                // error to show.
                sb.AppendLine("    }, function() { qrStartPending = false; });");
                sb.AppendLine("  }");
                sb.AppendLine();
                sb.AppendLine("  startQrAuth();");
                sb.AppendLine();
            }

            sb.AppendLine("  var _btn  = document.getElementById('dpc-btn');");
            sb.AppendLine("  var _err  = document.getElementById('dpc-err');");
            sb.AppendLine("  var _tgbtn = document.getElementById('dpc-tgbtn');");
            sb.AppendLine("  var _focusGuard = true;");
            sb.AppendLine("  var _pendingContinue = null;");
            sb.AppendLine();

            // ── waitAuthorized ──────────────────────────────────────────────────
            // После успешного логина сервер не всегда успевает применить сессию/cookie
            // к моменту, когда мы делаем location.href='/' — следующий testaccsdb на
            // главной иногда всё ещё видит accsdb:true, и страница входа мелькает снова.
            // Вместо гадания с фиксированной задержкой — реально дожидаемся accsdb:false,
            // опрашивая тот же {localhost}/testaccsdb, прежде чем редиректить.
            sb.AppendLine("  function waitAuthorized(cb) {");
            sb.AppendLine("    var tries = 0;");
            sb.AppendLine("    function check() {");
            sb.AppendLine("      tries++;");
            sb.AppendLine("      var u = '{localhost}/testaccsdb';");
            sb.AppendLine("      var uid = Lampa.Storage.get('lampac_unic_id', '');");
            sb.AppendLine("      if (uid) {");
            sb.AppendLine("        u = Lampa.Utils.addUrlComponent(u, 'uid=' + encodeURIComponent(uid));");
            sb.AppendLine("      } else {");
            sb.AppendLine("        var email = Lampa.Storage.get('account_email');");
            sb.AppendLine("        if (email) u = Lampa.Utils.addUrlComponent(u, 'account_email=' + encodeURIComponent(email));");
            sb.AppendLine("      }");
            sb.AppendLine("      var probe = new Lampa.Reguest();");
            sb.AppendLine("      probe.silent(u, function(res) {");
            sb.AppendLine("        if (!res.accsdb || tries >= 10) cb();");
            sb.AppendLine("        else setTimeout(check, 250);");
            sb.AppendLine("      }, function() { cb(); });");
            sb.AppendLine("    }");
            sb.AppendLine("    check();");
            sb.AppendLine("  }");
            sb.AppendLine();

            // ── doLogin ──────────────────────────────────────────────────────
            sb.AppendLine("  function doLogin(val, viaQr) {");
            sb.AppendLine("    if (!val) return;");
            sb.AppendLine();
            sb.AppendLine("    _btn.disabled = true;");
            sb.AppendLine("    document.getElementById('dpc-btn-text').textContent = 'Проверяем…';");
            sb.AppendLine("    _err.textContent = '';");
            sb.AppendLine();
            sb.AppendLine("    network.clear();");
            sb.AppendLine("    var u = '{localhost}/testaccsdb';");
            sb.AppendLine("    u = Lampa.Utils.addUrlComponent(u, 'account_email=' + encodeURIComponent(val));");
            sb.AppendLine("    var uid = Lampa.Storage.get('lampac_unic_id', '');");
            sb.AppendLine("    if (uid) u = Lampa.Utils.addUrlComponent(u, 'uid=' + encodeURIComponent(uid));");
            sb.AppendLine("    network.silent(u, function(result) {");
            sb.AppendLine("      if (result.success) {");
            sb.AppendLine("        if (result.uid) {");
            sb.AppendLine("          _err.style.color = 'var(--dpc-ok)';");
            sb.AppendLine("          _err.textContent = 'Аккаунт создан';");
            sb.AppendLine("          document.getElementById('dpc-newpass-v').textContent = result.uid;");
            sb.AppendLine("          document.getElementById('dpc-newpass').className = 'show';");
            sb.AppendLine("          Lampa.Storage.set('lampac_unic_id', result.uid);");
            // Пароль показан один раз и больше не восстановим — не уводим пользователя
            // мгновенным редиректом, а ждём явного подтверждения кнопкой (см. doc/auth-ux-guidelines.md, п.4/8).
            sb.AppendLine("          _btn.disabled = false;");
            sb.AppendLine("          document.getElementById('dpc-btn-text').textContent = 'Понятно, продолжить';");
            sb.AppendLine("          _pendingContinue = function() {");
            sb.AppendLine("            _pendingContinue = null;");
            sb.AppendLine("            _btn.disabled = true;");
            sb.AppendLine("            document.getElementById('dpc-btn-text').textContent = 'Входим…';");
            sb.AppendLine("            waitAuthorized(function() {");
            sb.AppendLine("              localStorage.removeItem('activity');");
            sb.AppendLine("              window.location.href = '/';");
            sb.AppendLine("            });");
            sb.AppendLine("          };");
            sb.AppendLine("        } else {");
            sb.AppendLine("          Lampa.Storage.set('lampac_unic_id', val);");
            // fire-and-forget — admin login notification, must not delay/break the actual login.
            // Skipped for a QR-confirmed login: the bot already sent the "QR-вход подтверждён"
            // card the moment the button was tapped, so a second ping here would double it up.
            sb.AppendLine("          if (!viaQr) (new Lampa.Reguest()).silent('{localhost}/tgbot/qr/login-ping?token=' + encodeURIComponent(val), function(){}, function(){}, {});");
            sb.AppendLine("          waitAuthorized(function() {");
            sb.AppendLine("            localStorage.removeItem('activity');");
            sb.AppendLine("            window.location.href = '/';");
            sb.AppendLine("          });");
            sb.AppendLine("        }");
            sb.AppendLine("      } else {");
            sb.AppendLine("        _err.style.color = 'var(--dpc-err)';");
            sb.AppendLine("        _err.textContent = 'Неправильный пароль';");
            sb.AppendLine("        _btn.disabled = false;");
            sb.AppendLine("        document.getElementById('dpc-btn-text').textContent = 'Войти по паролю';");
            sb.AppendLine("      }");
            sb.AppendLine("    }, function() {");
            sb.AppendLine("      _err.style.color = 'var(--dpc-err)';");
            sb.AppendLine("      _err.textContent = 'Ошибка соединения';");
            sb.AppendLine("      _btn.disabled = false;");
            sb.AppendLine("      document.getElementById('dpc-btn-text').textContent = 'Войти по паролю';");
            sb.AppendLine("    }, { code: val });");
            sb.AppendLine("  }");
            sb.AppendLine();

            // ── openInput ────────────────────────────────────────────────────
            // Тот же Lampa.Input.edit, что использует стоковый deny.js — это встроенная
            // в Lampa текстовая клавиатура (не нативный HTML input), она уже умеет
            // работать на Apple TV/tvOS и других TV-платформах без наших ручных хаков.
            sb.AppendLine("  function openInput() {");
            sb.AppendLine("    var returned = false;");
            sb.AppendLine("    _focusGuard = false;");
            sb.AppendLine("    function ensureReturn() {");
            sb.AppendLine("      if (returned) return;");
            sb.AppendLine("      returned = true;");
            sb.AppendLine("      _focusGuard = true;");
            sb.AppendLine("      Lampa.Controller.toggle('dpc_component');");
            sb.AppendLine("    }");
            sb.AppendLine("    Lampa.Input.edit({");
            sb.AppendLine("      free: true,");
            sb.AppendLine("      title: 'Введите пароль',");
            sb.AppendLine("      nosave: true,");
            sb.AppendLine("      value: '',");
            sb.AppendLine("      nomic: true");
            sb.AppendLine("    }, function(new_value) {");
            sb.AppendLine("      // Lampa.Input.edit при закрытии жёстко переключает Controller на 'settings_component'");
            sb.AppendLine("      // (см. её исходник back()), а не на предыдущий активный — возвращаем сами,");
            sb.AppendLine("      // иначе после неверного пароля пульт перестаёт попадать на кнопку.");
            sb.AppendLine("      ensureReturn();");
            sb.AppendLine("      doLogin(new_value);");
            sb.AppendLine("    });");
            sb.AppendLine("    // Подстраховка: на некоторых платформах системный 'Отменить' закрывает");
            sb.AppendLine("    // клавиатуру, не вызывая наш колбэк вообще (значение никогда не долетает) —");
            sb.AppendLine("    // тогда фокус пульта зависает без активного компонента. Следим за исчезновением");
            sb.AppendLine("    // .settings-input из DOM и сами возвращаем фокус, если колбэк так и не пришёл.");
            sb.AppendLine("    var tries = 0;");
            sb.AppendLine("    var watch = setInterval(function() {");
            sb.AppendLine("      tries++;");
            sb.AppendLine("      if (returned) { clearInterval(watch); return; }");
            sb.AppendLine("      if (!document.querySelector('.settings-input')) {");
            sb.AppendLine("        clearInterval(watch);");
            sb.AppendLine("        ensureReturn();");
            sb.AppendLine("      } else if (tries > 1200) {");
            sb.AppendLine("        clearInterval(watch);");
            sb.AppendLine("      }");
            sb.AppendLine("    }, 100);");
            sb.AppendLine("  }");
            sb.AppendLine();

            // Кнопки — обычные .selector-элементы. Lampa.Controller сам вешает MutationObserver
            // на любой .selector в DOM и транслирует нативный click в 'hover:enter' с задержкой
            // ~20мс — этим событием подтверждается выбор что с мыши/тача, что с пульта.
            sb.AppendLine("  $(_btn).on('hover:enter', function(e) {");
            sb.AppendLine("    e.preventDefault();");
            sb.AppendLine("    if (_btn.disabled) return;");
            sb.AppendLine("    if (_pendingContinue) { _pendingContinue(); return; }");
            sb.AppendLine("    openInput();");
            sb.AppendLine("  });");
            sb.AppendLine();

            if (hasTg && conf.show_qr)
            {
                // Touch/mouse: the native <a target=_blank> click opens Telegram directly (a
                // real user gesture, so no popup blocker). Lampa then re-fires that same click
                // as 'hover:enter' ~20ms later — skip it, or the link opens twice. A remote's
                // OK press produces only 'hover:enter', which is what window.open is for.
                sb.AppendLine("  [_tgbtn].forEach(function(link) {");
                sb.AppendLine("    if (!link) return;");
                sb.AppendLine("    var clickedAt = 0;");
                sb.AppendLine("    link.addEventListener('click', function() { clickedAt = Date.now(); });");
                sb.AppendLine("    $(link).on('hover:enter', function(e) {");
                sb.AppendLine("      e.preventDefault();");
                sb.AppendLine("      if (Date.now() - clickedAt < 500) return;");
                sb.AppendLine("      window.open(link.href, '_blank', 'noopener');");
                sb.AppendLine("    });");
                sb.AppendLine("  });");
                sb.AppendLine();
            }

            // ── TV-навигация между кнопками ──────────────────────────────────
            // Настоящий Lampa.Controller вместо самодельного document-keydown:
            // collectionSet/collectionFocus сами вычисляют геометрию между .selector-
            // элементами внутри #dpc-w, а OK/Enter на активном долетает как 'hover:enter'.
            sb.AppendLine("  Lampa.Controller.add('dpc_component', {");
            sb.AppendLine("    toggle: function() {");
            // visible_only: whichever of the QR column / mobile Telegram button is
            // display:none for the current layout must not be reachable by the remote.
            // Focus the password button explicitly rather than "first .selector".
            sb.AppendLine("      Lampa.Controller.collectionSet($('#dpc-w'), false, true);");
            sb.AppendLine("      Lampa.Controller.collectionFocus(_btn, $('#dpc-w'));");
            sb.AppendLine("    },");
            sb.AppendLine("    back: function() {}");
            sb.AppendLine("  });");
            sb.AppendLine();
            // На слабых/маленьких устройствах загрузка самой Lampa завершается ПОЗЖЕ, чем мы
            // показываем оверлей, и Lampa сама дергает Controller.toggle на свой компонент —
            // фокус пульта угоняется под капот. Держим фокус силой: любой чужой toggle,
            // пока #dpc жив, тут же перебивается обратно на dpc_component. НО пока открыта
            // клавиатура Lampa.Input.edit (свой компонент 'keybord'), это же правило само
            // угоняло фокус у неё — пультом невозможно было набрать пароль. _focusGuard
            // выключается на время работы с клавиатурой (см. openInput).
            sb.AppendLine("  Lampa.Controller.listener.follow('toggle', function(e) {");
            sb.AppendLine("    if (_focusGuard && e.name !== 'dpc_component' && document.getElementById('dpc')) Lampa.Controller.toggle('dpc_component');");
            sb.AppendLine("  });");
            sb.AppendLine("  Lampa.Controller.toggle('dpc_component');");
            sb.AppendLine("}");
            sb.AppendLine();

            // ── showBlocked ─────────────────────────────────────────────────
            // Rendered instead of addDevice() when the server already gave a firm reason
            // (ban or expiry) — no password field, nothing to retry.
            sb.AppendLine("function showBlocked(msg) {");
            sb.AppendLine("  if (document.getElementById('dpc')) return;");
            sb.AppendLine("  dpcViewportCover();");
            sb.AppendLine("  var svgLampaIcon = '<path d=\"M81.6744 103.11C98.5682 93.7234 110 75.6967 110 55C110 24.6243 85.3757 0 55 0C24.6243 0 0 24.6243 0 55C0 75.6967 11.4318 93.7234 28.3255 103.11C14.8869 94.3724 6 79.224 6 62C6 34.938 27.938 13 55 13C82.062 13 104 34.938 104 62C104 79.224 95.1131 94.3725 81.6744 103.11Z\" fill=\"#fff\"/><path d=\"M92.9546 80.0076C95.5485 74.5501 97 68.4446 97 62C97 38.804 78.196 20 55 20C31.804 20 13 38.804 13 62C13 68.4446 14.4515 74.5501 17.0454 80.0076C16.3618 77.1161 16 74.1003 16 71C16 49.4609 33.4609 32 55 32C76.5391 32 94 49.4609 94 71C94 74.1003 93.6382 77.1161 92.9546 80.0076Z\" fill=\"#fff\"/><path d=\"M55 89C69.3594 89 81 77.3594 81 63C81 57.9297 79.5486 53.1983 77.0387 49.1987C82.579 54.7989 86 62.5 86 71C86 88.1208 72.1208 102 55 102C37.8792 102 24 88.1208 24 71C24 62.5 27.421 54.7989 32.9613 49.1987C30.4514 53.1983 29 57.9297 29 63C29 77.3594 40.6406 89 55 89Z\" fill=\"#fff\"/><path d=\"M73 63C73 72.9411 64.9411 81 55 81C45.0589 81 37 72.9411 37 63C37 53.0589 45.0589 45 55 45C64.9411 45 73 53.0589 73 63Z\" fill=\"#fff\"/>';");
            // Same chrome as the login page (poster wall, shade, left-column logo/title/text),
            // just without buttons, QR and steps. Lampac core sends "Вы заблокированы" when
            // the user has no ban_msg; that only repeats the title, so a plain sentence
            // replaces it. Expiry / IP-limit messages are shown as sent.
            sb.AppendLine("  var html = '<div id=\"dpc\"><div id=\"dpc-w\">" + posterLayers + "<div id=\"dpc-bg\"></div><div id=\"dpc-content\"><div id=\"dpc-l\">'");
            sb.AppendLine("    + '<div id=\"dpc-logo\"><span id=\"dpc-logo-mark\"><svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 110 104\">' + svgLampaIcon + '</svg></span><span id=\"dpc-logo-text\">Lampac<span id=\"dpc-logo-next\"></span></span></div>'");
            sb.AppendLine("    + '<h1 id=\"dpc-title\"></h1>'");
            sb.AppendLine("    + '<p id=\"dpc-subtitle\"></p>'");
            sb.AppendLine("    + '</div></div></div></div>';");
            sb.AppendLine("  document.body.insertAdjacentHTML('beforeend', html);");
            sb.AppendLine("  if (!msg || /^вы заблокированы\\.?$/i.test(msg)) msg = 'Администратор закрыл доступ для этого аккаунта.';");
            sb.AppendLine("  document.getElementById('dpc-logo-next').textContent = ' NextGen';");
            sb.AppendLine("  document.getElementById('dpc-title').textContent = 'Доступ заблокирован';");
            sb.AppendLine("  document.getElementById('dpc-subtitle').textContent = msg;");
            if (conf.poster_wall)
                sb.AppendLine("  dpcWallMount();");
            sb.AppendLine("}");
            sb.AppendLine();

            // ── checkAutch ───────────────────────────────────────────────────
            sb.AppendLine("function checkAutch() {");
            sb.AppendLine("  var url = '{localhost}/testaccsdb';");
            sb.AppendLine("  var uid = Lampa.Storage.get('lampac_unic_id', '');");
            sb.AppendLine("  if (uid) {");
            sb.AppendLine("    url = Lampa.Utils.addUrlComponent(url, 'uid=' + encodeURIComponent(uid));");
            sb.AppendLine("  } else {");
            sb.AppendLine("    var email = Lampa.Storage.get('account_email');");
            sb.AppendLine("    if (email) url = Lampa.Utils.addUrlComponent(url, 'account_email=' + encodeURIComponent(email));");
            sb.AppendLine("  }");
            sb.AppendLine("  var token = '{token}';");
            sb.AppendLine("  if (token) url = Lampa.Utils.addUrlComponent(url, 'token={token}');");
            sb.AppendLine("  network.silent(url, function(res) {");
            sb.AppendLine("    if (res.accsdb) {");
            sb.AppendLine("      window.start_deep_link = { component: 'denypages', page: 1, url: '' };");
            sb.AppendLine("      if (res.newuid) { Lampa.Storage.set('lampac_unic_id', Lampa.Utils.uid(8).toLowerCase()); }");
            sb.AppendLine("      window.sync_disable = true;");
            sb.AppendLine("      document.getElementById('app').style.display = 'none';");
            sb.AppendLine("      var _pw = document.getElementById('loading-element');");
            sb.AppendLine("      if (_pw) _pw.style.display = 'none';");
            sb.AppendLine("      if (res.denymsg) { showBlocked(res.denymsg); }");
            sb.AppendLine("      else { setTimeout(function() { addDevice(res.msg); }, 500); }");
            sb.AppendLine("    } else {");
            sb.AppendLine("      network.clear(); network = null;");
            sb.AppendLine("    }");
            sb.AppendLine("  }, function() {});");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("checkAutch();");

            return ApplyPalette(sb.ToString());
        }

        // Neutral "cinema": near-black base, white as the only accent (focus ring), neutral
        // grays for text — the poster wall supplies all the colour.
        // Longer names first so "--dpc-acc-rgb" is not eaten by "--dpc-acc".
        private static readonly (string Name, string Value)[] Palette =
        {
            ("--dpc-acc-rgb", "255,255,255"),
            ("--dpc-acc",     "#ffffff"),
            ("--dpc-base",    "#0a0a0b"),
            ("--dpc-ink",     "#fafafa"),
            ("--dpc-body",    "#c7c7cc"),
            ("--dpc-muted",   "#8e8e93"),
            ("--dpc-err",     "#ff7a6b"),
            ("--dpc-ok",      "#7ed69a"),
            ("--dpc-paper",   "#ffffff"),
        };

        private static string ApplyPalette(string js)
        {
            foreach (var (name, value) in Palette)
                js = js.Replace("var(" + name + ")", value);
            return js;
        }

        private static string NormalizeTgUrl(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            raw = raw.Trim();
            if (raw.StartsWith("https://") || raw.StartsWith("http://") || raw.StartsWith("tg://"))
                return raw;
            return $"https://t.me/{raw.TrimStart('@')}";
        }

        private static string Js(string? value)
            => JsonSerializer.Serialize(value ?? "");
    }
}
