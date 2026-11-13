// lostfilm.js — custom Lampa source (stages 1-3, v2.3)
// stage 1: skeleton, helpers, ttl-cache, net, normalizeCard, category/list lines
// stage 2: api.full via TMDB resolve + registry, follow/watched buttons on full screen
// stage 3: bind modal with captcha, settings section, auth-driven status + empty "my" line
// v2.1   : (A) poster fix, (B) LostFilm icon in menu + settings, (C) "Новые релизы"
//          row on the main screen via Lampa.ContentRows.add, (D) schedule split
//          into per-day lines ("Сегодня"/"Завтра"/.../"Позже")
// v2.2   : (E) HQ posters — "Posters/icon.jpg" upgraded to "Posters/image.jpg"
//          behind a one-time session preflight, (F) "Послезавтра" schedule line
//          built from the real 'DD.MM.YYYY' dates instead of the server groups.
// v2.3   : (K) POSTERS ARE NOW MAXIMUM: catalogue image.jpg (240x144) and the
//          /my + /schedule icon.jpg (40x40) are walked up to poster.jpg
//          (715x330) — the biggest variant the CDN has (full name-dictionary
//          probe in the (E)(K) block). image_s<N>.jpg season art is left as is;
//          (L) FILTER SUB-SCREENS: sort/year/genre/status of each catalogue moved
//          out of the flat LostFilm folder into two nested settings components
//          ("Сериалы LostFilm" / "Фильмы LostFilm") opened from two entry rows,
//          hidden from the main settings list; Storage keys unchanged.
// v2.5   : (N) FILTERS MOVED ONTO THE SCREEN. Two user reports said the same
//          thing: nobody looks for "фильтры" inside plugin settings. Sort, year,
//          genre and status of a catalogue belong on the catalogue screen, right
//          where the cards are. The two nested settings components
//          (the removed serial / movies sub-screens), their two entry rows
//          (two removed entry rows), the child-screen hiding and the branch of
//          the Settings.listener "open" hook are ALL GONE. In their place a panel
//          of four Lampa selectors is rendered into the category_full activity,
//          ABOVE the card grid, and every choice writes the SAME Storage keys as
//          before (lostfilm_v2_filter_<kind>_<field>) — nobody loses a choice.
//          The (L) note below is kept as history; the live design is (N).
// v2.6   : (O) THE 0-RESULT FILTER TRAP. A saved catalogue filter (Storage keys
//          lostfilm_v2_filter_<kind>_<field>, see (N)) was applied in
//          catalogCards() for EVERY caller, the category LINES included. A filter
//          such as "год 1990" that matches 0 of the 805 serials therefore made
//          makeCatalogLine() emit([]) -> finish(false), and partNext() in the core
//          DROPS envelopes with no results — the whole "Все сериалы" line vanished
//          from the LostFilm page with no way back: the reset lived only in the
//          settings and the filter in Storage. Four changes:
//            (O1) catalogCards(kind, cb, applyFilters) — filters/sort run ONLY
//                 when applyFilters === true. The category lines and the metadata
//                 warm-up pass false (raw server order, always non-empty); list()
//                 (the category_full screen) passes true, so filtering still
//                 works exactly where it belongs.
//            (O2) a fifth panel row "Сбросить фильтры" writes CAT_SORT / 'all'
//                 into all four fields through the SAME filterSet() and Storage
//                 keys, then reloads the screen — a one-press escape.
//            (O3) panelReload() no longer calls invalidate() (which also dropped
//                 the registry and /my+/new+/schedule). It drops ONLY cache keys
//                 containing '/catalog' via cacheInvalidateMatch(), leaving the
//                 subscription caches warm.
//            (O4) list() answers a 0-result filter with the usual empty envelope
//                 PLUS a noty() explaining that the panel's reset row is the way
//                 out. No DOM is patched into core markup anywhere here.
// v2.2b  : (G) SESSION OWNERSHIP — the plugin owns the LostFilm session: after a
//          successful bind it reads the FULL lf_session from GET /status (field
//          "session", returned only while auth:true), keeps it in Lampa.Storage
//          under 'lostfilm_v2_session' and sends it back as the X-Lf-Session
//          header on EVERY call to /lite/lostfilm/*. If the server answers
//          auth:false / cookie_valid:false while our session was sent, the stored
//          session is dropped and the user is told to re-bind. The value itself is
//          never logged and never rendered — only the masked uid reaches the UI.
//          (H) CATALOG SCREENS — two stock lines "Все сериалы" / "Все фильмы"
//          (after "Мои подписки"/"Новинки", before the schedule lines) fed by
//          GET /lite/lostfilm/catalog?act=serial|movies (805 / 257 items, server
//          cache 12 h). Lines + the paginated "Все сериалы" screen share one
//          client cache; filtering and sorting live ON THE CATALOG SCREEN ITSELF
//          (v2.5: a four-row panel at the top of category_full), NEVER in the
//          line DOM — no DOM hooks are installed into core markup.
//
// Server API (Lampac mods/lostfilm) — verified live:
//   GET  /lite/lostfilm/status             -> {auth, user, cookie_valid, session}
//           session = ПОЛНЫЙ lf_session '<32hex>.<число>', отдаётся ТОЛЬКО при auth:true
//           (осознанная передача владения сессией плагину); плагин возвращает его
//           заголовком X-Lf-Session — приоритет 0 в LostfilmAuth.GetCookie
//   GET  /lite/lostfilm/my                 -> {data:[{id,link,title,title_en,poster,...}]}
//   GET  /lite/lostfilm/new?limit=N        -> {data:[...]}
//   GET  /lite/lostfilm/schedule           -> {data:[{group, items:[...]}]}
//          groups (live): "сегодня", "завтра", "на этой неделе",
//                         "на следующей неделе", "позже"
//   GET  /lite/lostfilm/catalog?act=serial|movies
//                                          -> {data:[{id,title,title_orig,date,link,img,
//                                             genres,channels,status,status_auto,not_aired,
//                                             favorited,not_favorited,rating,ismovie}], total}
//          serial: 805 items, movies: 257 items; img = '/Static/Images/<id>/Posters/image.jpg'
//   GET  /lite/lostfilm/follow/{serialId}  -> {ok:true, result:'on'|'off'}
//   GET  /lite/lostfilm/watched?episodeID=<id>&mode=on -> {ok:true}
//   GET  /bind/lostfilm                    -> HTML form (hidden phpssid + r, mail/pass/captcha)
//   POST /bind/lostfilm (phpssid,r,mail,pass,captcha) -> HTML 'msg ok' | 'msg err'
//   GET  /bind/lostfilm/captcha?r=<r>&phpssid=<sid>   -> GIF (captcha tied to PHP session)
//
// Only public Lampa hooks are used: Api.sources, Api.partNext, Reguest, TMDB,
// Listener.follow('full'), ContentRows.add, SettingsApi.addComponent/addParam,
// Settings.listener.follow, Params.select/listener, Modal.open,
// Controller.add/toggle/collectionSet/collectionFocus, Noty, Storage.
//
// ---------------------------------------------------------------------------
// (A) WHY CARD POSTERS WERE BROKEN — root cause, quoted from app.min.js:
//
//     var Card$2 = { onInit: ... this.getPosterPath = function () {
//       if (params.style.name == 'wide' && data.backdrop_path) src = TMDB.img(data.backdrop_path,'w780');
//       else if (params.style.name == 'collection' && data.backdrop_path) src = TMDB.img(data.backdrop_path,'w500');
//       else if (data.poster_path) src = TMDB.img(data.poster_path);
//       else if (data.profile_path) src = TMDB.img(data.profile_path);
//       else if (data.poster) src = data.poster;
//       else if (data.img) src = data.img;
//       else src = './img/img_broken.svg';
//
//     function img$1(src, size) {                       // == Lampa.Api.img == Lampa.TMDB.img
//       var poster_size = Storage.field('poster_size');
//       var baseimg = 't/p/' + poster_size + '/';
//       var path = baseimg;
//       if (size) path = path.replace(new RegExp(poster_size,'g'), size);
//       return src ? TMDB$1.image(path + src) : '';      // -> <protocol>image.tmdb.org/t/p/w300/<src>
//     }
//
//     poster_path is ALWAYS fed through TMDB.img(); there is no /^http/ passthrough.
//     v2 put the ABSOLUTE lostfilm URL (https://static.lostfilm.top/.../image_s2.jpg)
//     into card.poster_path, so the core requested
//       https://image.tmdb.org/t/p/w300/https://static.lostfilm.top/... -> no image.
//     card.poster / card.img are the only fields used verbatim -> absolute URLs belong there.
// ---------------------------------------------------------------------------
// (E) v2.2/v2.3 — POSTER QUALITY, measured against the live API:
//
//     /lite/lostfilm/new?limit=3 -> poster = .../Posters/image_s2.jpg   (16-33 KB, season poster)
//     /lite/lostfilm/my          -> poster = .../Posters/icon.jpg       ( 3-14 KB, tiny 40/40)
//     /lite/lostfilm/schedule    -> poster = .../Posters/icon.jpg       ( 3-14 KB, tiny)
//     /lite/lostfilm/catalog     -> img    = /Static/Images/<id>/Posters/image.jpg
//
//     (K) v2.3 — THE BIG ONE. Direct probe of the CDN
//     (https://static.lostfilm.top/Images/<id>/Posters/<name>, see the header
//     table below) showed that the host serves exactly THREE poster variants:
//
//       name          size       weight        where the server uses it
//       ------------  ---------  ------------  ----------------------------------
//       icon.jpg      40x40      3-13 KB       /my, /schedule (badge)
//       image.jpg     240x144    22-49 KB      /catalog, image_s<N> season poster
//       poster.jpg    715x330    44-187 KB     the site's OWN big banner
//
//     Everything else 404s, verified with a dictionary probe on ids 127/1163:
//     image_original.jpg, image_orig.jpg, image_hq.jpg, image_big.jpg,
//     image_large.jpg, image_full.jpg, poster_original.jpg, poster_hq.jpg,
//     poster_big.jpg, poster_large.jpg, poster_full.jpg, original.jpg, big.jpg,
//     large.jpg, hq.jpg, full.jpg, cover.jpg, photo.jpg, poster_2x.jpg,
//     image_2x.jpg, image.webp, poster.webp, image.png, poster.png,
//     portrait.jpg, vertical.jpg, fanart.jpg, background.jpg, backdrop.jpg,
//     image_s2_original.jpg, image_s2_big.jpg, poster_s1..s3.jpg -> all 404.
//
//     So **poster.jpg (715x330) is the maximum**. It is verified 200 on 40/40
//     ids of the serial catalogue (newest batch), 8/8 ids of the movies
//     catalogue and 25/25 sampled older ids (159..1011) — 73/73 = 100%.
//     Cross-check: the site itself publishes poster.jpg as the og:image of a
//     series page (<meta property='og:image' content=".../1163/Posters/poster.jpg">),
//     i.e. it is the largest asset LostFilm exposes for an id.
//
//     Why this matters for the Lampa card: the core renders every source card
//     through `.card__view { padding-bottom: 150% }` + `.card__img { object-fit:
//     cover }`, i.e. a 2:3 PORTRAIT box. Both image.jpg (5:3) and poster.jpg
//     (2.17:1) are wide, so both get side-cropped — but poster.jpg carries ~3x
//     the horizontal pixels, and in the same crop window the blur is gone (the
//     240x144 asset had to be upscaled ~2.5x, 715x330 only ~1.1x). The card
//     markup/CSS is NEVER touched: only the URL we put into card.poster/card.img.
//
//     Rule: a poster URL is walked down the chain
//       icon.jpg  -> [poster.jpg, image.jpg, icon.jpg]
//       image.jpg -> [poster.jpg, image.jpg]
//       anything else (image_s<N>.jpg, an already-upgraded poster.jpg, a foreign
//       host) -> returned untouched.
//     The first candidate that is not ALREADY known to 404 for that <id>/<name>
//     is served, and is preflighted in the background a single time per variant
//     with a throwaway Image(); a 404 is remembered in posterBad and the NEXT
//     render (or the next line rebuild) steps one tier down. The map is
//     session-only on purpose — Lampa.Storage is NOT touched.
//
// (N) v2.5 — THE FILTER PANEL MOVED ONTO THE CATALOG SCREEN (supersedes (L)).
//
//     USER FEEDBACK (verbatim): «Зачем ты оставил фильтр фильмов и сериалов в
//     настройках плагина? Если фильтр сериалов должен находиться при нажатии
//     кнопки "Ещё" в сериалах на экране LostFilm, то же самое и для фильмов».
//     Correct. A catalogue filter is a property of the catalogue SCREEN, not of
//     the plugin. v2.3 had pushed the four selects into two nested settings
//     components the user had to find in Настройки -> LostFilm. Removed.
//
//     WHAT SURVIVES: only the Storage keys. filterKey(kind, field) still yields
//     'lostfilm_v2_filter_<kind>_<field>', so an existing user choice is carried
//     over untouched — only the UI that writes them changed.
//
//     WHERE THE PANEL LIVES. In the category_full activity, as a SIBLING placed
//     BEFORE the activity's root scroll container:
//
//       .activity--active > .activity__body > div          <- activity root
//         ├─ div.lostfilm-v2-filters                       <- OUR PANEL
//         └─ div.scroll.scroll--mask.scroll--over...       <- core (Base ctor)
//              .scroll__content > .scroll__body > .mapping--grid.cols--6
//
//     WHY NOT INSIDE THE SCROLL (measured live with a probe panel on the running
//     screen): Items.onScroll rebuilds the focus collection from `this.items`
//     ONLY — `Navigator.setCollection(this.items.slice(...).map(c =>
//     c.render(true)))` — and a node appended into .scroll__body is not in
//     this.items, so it is reachable while the collection is full but is DROPPED
//     the moment the user scrolls (measured: 34 elements incl. the panel -> 38,
//     panel absent, node still present in the DOM). A SIBLING of the scroll is
//     never in this.items, so Items can never prune it.
//
//     WHY THE PANEL IS RE-APPENDED. The screen's controller does
//     `Controller.collectionSet(_this3.scroll.render(true))`, i.e. collectionSet
//     REPLACES the whole collection and a sibling is wiped out with it (measured:
//     22 elements incl. the panel -> 20 after Controller.toggle('content'), panel
//     gone). The panel therefore listens to the PUBLIC Controller.listener 'toggle'
//     event and re-appends its own rows with Controller.collectionAppend() when
//     the event targets 'content' AND Controller.own(component) is true (i.e. OUR
//     screen is the live one). Measured after the fix: 22 -> 22, no duplication,
//     and Navigator.navigate(firstCard, 'up') lands on a panel row.
//
//     HEIGHT. The scroll is `.layer--wheight`; Layer.update() computes
//     `innerHeight − head − navi − scroll.mheight`, and scroll.mheight is
//     undefined on this screen, so a sibling panel would push the grid straight
//     out of the layer (measured: panel 214px + scroll 588px on an 861px window).
//     The panel therefore calls scroll.minus(panel) ONCE after insertion:
//     .layer--wheight is idempotent (classList.add) and mheight = <panel node>,
//     which is exactly the documented meaning of the field
//     (`attr.getBoundingClientRect().height` is what Layer.update subtracts).
//     Re-measured after the call: panel 88px + scroll 723px = 811 <= 861 — no
//     overflow (the 50px head is the remaining gap).
//
//     RELOAD AFTER A CHOICE. app.min.js was checked for a stock refresh:
//       * Activity.refresh(all) -> clearTimeout + setTimeout(..., 1000), then
//         activity.refresh() -> runRefresh() -> Activity.replace() after ANOTHER
//         400 ms, plus loader(true). Correct but it is a 1.4 s dance that blanks
//         the screen, and it exists for a refresh OTHER code requested
//         (Pagination.onRight does `object.page = a.page; activity.refresh()`),
//         not for a synchronous user choice.
//       * Activity.replace(replace, clear) -> the direct one:
//             var object = extractObject(active());
//             for (var i in replace) object[i] = replace[i];
//             var made = active(); made.activity.destroy(); activites.pop();
//             push$3(clear ? replace : object);
//         With clear = false it takes the CURRENT activity, overlays only the
//         fields passed, and re-pushes it: url/component/source/title and the
//         whole activity stack survive, the grid restarts from page 1.
//         Verified live on this very screen: 4 activities before and after,
//         url 'catalog:serial', component 'category_full', source 'lostfilm',
//         page 2 -> 1, a NEW activity object, 20 cards rendered.
//         This is the variant used; the backward()+push() fallback is kept only
//         for a build that lacks Activity.replace.
//
//     DUPLICATE GUARD. The activity handler runs on every event of this screen
//     ('start', 'archive', ...), so the panel carries the marker class
//     'lostfilm-v2-filters' and is built only while the activity root does not
//     already contain it; 'destroy' clears the remembered screen.
//
//     FOCUS/KEYS. The panel rows are plain `.selector` nodes — exactly what
//     Controller.collectionSet() harvests (`querySelectorAll('.selector')`) and
//     Controller.focus() highlights. No DOM hooks into core markup, no
//     DOM observers, no monkey-patching: the panel is OUR node in OUR screen,
//     wired through the PUBLIC Lampa.Listener and Lampa.Select.
//
//     VALUE LISTS. Year/genre options come from catalogMeta, built from the live
//     /catalog answer. On a first-ever open meta may still be empty (request in
//     flight): the panel then offers only 'Все годы' / 'Все жанры' and kicks off
//     catalogCards() for this kind so the next open (and the next Select) has the
//     full lists. Sort and status are static and always complete.
// ---------------------------------------------------------------------------
// (F) v2.2 — "ПОСЛЕЗАВТРА" LINE. The server only knows five day groups, so a
//     +2-day episode lands in "на этой неделе". Schedule lines are therefore
//     re-bucketed from the real item.date ('DD.MM.YYYY') in local time:
//       diff 0 -> Сегодня | 1 -> Завтра | 2 -> Послезавтра | 3..7 -> На этой неделе
//       diff > 7 (or group "на следующей неделе"/"позже") -> Позже
//     Unparseable date -> fall back to the server group (previous behaviour).
// ---------------------------------------------------------------------------
// (I) v2.3 — "ЕЩЁ" ON LINES + "НОВЫЕ РЕЛИЗЫ" TERMINOLOGY. Root cause quoted from
//     app.min.js (a main/category line is built as LineMap base =
//     Helper$b.except('Icon','MoreFirst'), so the 'More' module is always on):
//
//       var More = {
//         onVisible: function onVisible() {
//           var pages = this.data.total_pages || 1;
//           if (pages <= 1) return;
//           var button = document.createElement('div');
//           button.classList.add('items-line__more');
//           button.classList.add('selector');
//           button.text(Lang.translate('more'));
//           button.on('hover:enter', this.emit.bind(this, 'more', this.data));
//           this.html.find('.items-line__head').append(button);
//         },
//         onScroll: function onScroll() {
//           if (!this.more && this.data.results.length == this.items.length &&
//               this.data.total_pages > 1) { ... }
//         }
//       };
//
//     => the line/row "Ещё" button is driven by **total_pages > 1** (line.more is
//        NOT read by this module; that flag only feeds the deprecated
//        InteractionLine: `if ((data.results.length >= 20 || data.more) &&
//        !params.nomore)`). The click is bound to the line's 'more' event, and on
//        the main screen (component main -> onInstance) that becomes
//        router.call('category_full', data): url = line.url, page = line.page || 1,
//        source = line.source. Our list() then serves every following page, since
//        the category_full screen's Items.onLoadNext does `this.object.page++`
//        and calls Api.list(object) again.
//
//     Emitted envelopes therefore now carry
//         total_pages = ceil(total / PAGE_SIZE)   more = total_pages > 1
//     for 'my' | 'catalog'. Schedule lines keep the same formula (it is
//     harmless: only "Позже" with >20 items reaches 2 pages). 'new' is the
//     exception since (J): its envelope carries the SERVER depth (100 pages).
//
//     TERMINOLOGY: the category line 'new' was called "Новинки" while the
//     main-screen ContentRows row was "Новые релизы LostFilm" — one user looked
//     for the latter on the LostFilm screen and reported "нет новых релизов".
//     Both are now "Новые релизы", and the 'new' line also emits an EMPTY
//     envelope (emptyEnvelope = true) instead of disappearing when /new answers
//     an empty/errored payload.
//
//     (J) SERVER-SIDE PAGINATION of /new: "Ещё" on the "Новые релизы" line AND
//     on the main-screen row now opens the NEXT SERVER PAGE (/new?page=N).
//         * server behaviour (verified live): 15 episodes per page, page clamped
//           to 1..100, every page cached separately for 15 min server-side;
//           /new?limit=60 and /new?limit=200 both answer 15 items.
//         * endpoint('new', limit, page) appends '&page=N' for N > 1, so page 1
//           keeps the legacy URL/cache key and each page gets its own cache entry
//           (server 15 min, our netGet 3 h).
//         * list('new', page) passes the core's page STRAIGHT to the server and
//           returns that server page AS IS (no PAGE_SIZE slicing — slicing a
//           15-item page at offset 20 would render an EMPTY screen).
//         * line/row envelopes for 'new' advertise the SERVER depth
//           (total_pages = NEW_PAGES = 100, more = true) instead of a local
//           ceil(len / PAGE_SIZE): 15 local cards would otherwise mean one page
//           and "Ещё" would never appear.
//         * the main-screen row aggregates server pages 1+2 in parallel (30
//           episodes) and renders ROW_LIMIT = 20 of them.
// ---------------------------------------------------------------------------
// (M) v2.4 — HYBRID A: LAZY TMDB POSTERS FOR THE CATALOGUE (background resolve).
//
//     The catalogue lines ("Все сериалы" / "Все фильмы") keep their instant
//     LostFilm posters (poster.jpg 715x330, see the (E)(K) note) on the FIRST
//     render, and a BACKGROUND queue silently resolves each card to a TMDB id
//     and remembers it. The SECOND render (re-open / re-scroll / restart) already
//     shows TMDB posters — no DOM hooks, no observers, no timers, no waiting.
//
//     WHY NOT: swapping <img> src of already-rendered cards. That would require
//     a DOM observer / polling / a monkey-patch of Card.use — ALL forbidden.
//     The core renders the cards itself; the ONLY legal place to change a poster
//     is normalizeCatalogItem(), i.e. BEFORE the card reaches the core.
//
//     THE MECHANISM (four pieces):
//       1. MAP + STORAGE. 'lostfilm_v2_tmdb_map' keeps
//          { '<lostfilm id>': {id:<tmdb id>, p:'<poster_path>', t:'movie|tv',
//          ts:<ms>} }. It survives restarts (that is what makes visit 2 fast) and
//          is capped by TMDB_MAP_MAX with the OLDEST ts dropped first.
//       2. QUEUE. catalogCards() answers IMMEDIATELY with LostFilm posters and
//          only then enqueues the cards that have no mapping yet (max
//          TMDB_QUEUE_MAX per catalogue load). The queue is processed SERIALLY
//          through a single recursive setTimeout, TMDB_RATE_MS (250 ms) apart,
//          and re-checks the map right before each request so a card resolved by
//          a previous batch is never searched twice.
//       3. RESOLVE. Lampa.TMDB.api('search/multi?...') is called DIRECTLY
//          (read-only; TMDB.api is NEVER monkey-patched) exactly like full()
//          already does, then pickTmdbResult() matches on normalizeName(title_orig)
//          with a media_type check (serial->tv / movies->movie) and a fuzzy
//          first-non-person fallback.
//       4. POSTER BRANCH. normalizeCatalogItem() now has two branches:
//            mapped   -> card.poster_path = '<poster_path>', poster/img = ''
//                        (the core pipes poster_path through TMDB.img(), so the
//                        relative path is CORRECT here — this is the v2.2 bug
//                        note in (A) applied the right way round);
//            unmapped -> LostFilm poster in poster/img, poster_path empty (as
//                        before) + enqueue for the background.
//
//     The feature is behind the 'lostfilm_v2_tmdb_posters' trigger (default ON)
//     in the LostFilm settings folder. "Сбросить кеш" clears the map as well.
//     NOTHING here touches init.conf, mods/, the server, or the DOM.
// ---------------------------------------------------------------------------

(function () {
  'use strict';

  if (window.__lostfilm_v2_ready) return;
  window.__lostfilm_v2_ready = true;

  if (typeof Lampa === 'undefined' || !Lampa) return;
  if (!Lampa.Api || !Lampa.Api.sources) return;
  // only register if the source name is still free
  if (Lampa.Api.sources.lostfilm) return;

  var BASE = String(location.origin || '').replace(/\/+$/, '');
  var SOURCE = 'lostfilm';
  var LINE_LIMIT = 40;    // /new + /schedule results per category line
  var LIST_LIMIT = 200;   // generic list fallback (kept for legacy urls)
  // (J) SERVER-SIDE PAGINATION of /new ("Новые релизы").
  // The server is the ONLY pager for this feed: it serves 15 EPISODES per page
  // (its own parser limit — /new?limit=60 still answers 15) and CLAMPS `page`
  // to 1..100, caching every page separately for 15 min on the server side.
  // Verified live: /new?limit=60 -> 15 items, /new?limit=60&page=2 -> 15
  // DIFFERENT items, page=3 -> another 15, page=100 -> 15, page=101 -> 100.
  // The client therefore passes the core's page STRAIGHT to the server and
  // returns the server page AS IS (never slices it by PAGE_SIZE — a 15-item
  // server page sliced at 20+ would make page 2 an EMPTY screen).
  var NEW_LIMIT = 60;     // server fetch size per /new request (server gives 15)
  var NEW_PAGE_SIZE = 15; // episodes the server actually returns per page
  var NEW_PAGES = 100;    // server-side page clamp (1..100) -> paging depth
  var NEW_TOTAL = NEW_PAGES * NEW_PAGE_SIZE; // estimated counter for "Ещё"
  // (I)(J) main-screen "Новые релизы" row: aggregates the first ROW_PAGES
  // server pages of /new (15 each) and renders ROW_LIMIT of those cards; the
  // envelope advertises the SERVER depth so the core's "Ещё" pages the server.
  var ROW_LIMIT = 20;     // cards rendered in the main-screen row
  var ROW_PAGES = 2;      // server pages aggregated into the row (2 x 15 = 30)
  var PAGE_SIZE = 20;
  // catalog lines: 40 cards per line, 4 parts on top of the 7 existing ones.
  // LINE_PARTS_LIMIT below is therefore raised to 11 so that every part still
  // lands inside the FIRST partNext() batch (parts are sliced by parts_limit).
  var CAT_LINE_LIMIT = 40;
  var CAT_TOP = 3000;     // server-side hard cap; the whole catalogue (805/257)
  var CAT_SORT = 'rating';
  // same batch size the core uses for main/category. MUST be >= the number of
  // parts built in category() (2 fixed + 2 catalog + SCHEDULE_LINES.length = 11),
  // because partNext() slices the first `parts_limit` loaders and only re-enters
  // the queue when fewer than 3 lines came back — a part past the limit is lost.
  var LINE_PARTS_LIMIT = 11;
  var CACHE_MAX = 100;
  var CACHE_TTL = 3 * 60 * 60 * 1000; // 3 h
  var API_BASE = '/lite/lostfilm';
  var BIND_URL = BASE + '/bind/lostfilm';
  // (H) catalogue images arrive as '/Static/Images/<id>/Posters/image.jpg'. The
  // '/Static' prefix does NOT exist on the CDN host (verified: 404), while
  // 'https://static.lostfilm.top/Images/<id>/Posters/image.jpg' answers 200.
  // The host matches init.conf Lostfilm.imgHost; /my already sends absolute URLs,
  // so this constant is only used for the catalogue.
  var IMG_HOST = 'https://static.lostfilm.top';

  var COMPONENT = 'lostfilm_settings_v2';
  // (N) the on-screen filter panel's marker class — also its duplicate guard
  // (inserted only while the activity root does not already carry it).
  var FILTERS_CLASS = 'lostfilm-v2-filters';
  var CONTROLLER = 'lostfilm_bind_v2';
  var LS_PREFIX = 'lostfilm_v2_';
  var LS_STATUS = LS_PREFIX + 'status';
  // (G) the plugin's own copy of the FULL lf_session ('<32hex>.<digits>').
  // Written only from /status (auth:true) and never logged / rendered.
  var LS_SESSION = LS_PREFIX + 'session';
  // (H) catalogue filters, one set per catalogue kind: <kind> = serial | movies
  var LS_FILTER = LS_PREFIX + 'filter_';
  // (M) persistent lostfilm id -> TMDB id/poster map (see the (M) header note).
  // One JSON string in Lampa.Storage; visit 2 of a catalogue renders TMDB
  // posters straight away, without a single network request.
  var LS_TMDB_MAP = LS_PREFIX + 'tmdb_map';
  var TMDB_MAP_MAX = 1200;    // whole catalogue ~1062 (805 serial + 257 movies)
  var TMDB_QUEUE_MAX = 40;    // resolves enqueued per catalogue load
  var TMDB_RATE_MS = 250;     // one search/multi request per 250 ms (serial queue)
  var TMDB_BATCH = 20;        // pending resolves written to Storage in one go
  // (M) trigger param (Lampa SettingsApi type 'trigger'); default true.
  var LS_TMDB_POSTERS = LS_PREFIX + 'tmdb_posters';
  // the header the server reads first (LostfilmAuth.GetCookie, priority 0)
  var SESSION_HEADER = 'X-Lf-Session';
  var SESSION_RE = /^[0-9a-f]{32}\.[0-9]{1,10}$/;

  // ------------------------------------------------------------- (B) icons
  // One <path> (two subpaths, drawn as an "LF" monogram), 24x24.
  // fill="currentColor" -> inherits the menu text colour; the core CSS rewrites
  // `.menu__item.focus .menu__ico path[fill] { fill: #000 }`, so focus styling
  // keeps working exactly like for the built-in icons.
  function lfIcon(fill) {
    return '<svg width="24" height="24" viewBox="0 0 24 24" fill="none" ' +
      'xmlns="http://www.w3.org/2000/svg" aria-hidden="true">' +
      '<path fill="' + fill + '" d="M3 4.5h3v12h6v3H3V4.5Zm9 0h9v3h-6v3h5v3h-5v6h-3V4.5Z"/>' +
      '</svg>';
  }

  // ---------------------------------------------------------------- helpers

  function isFn(f) {
    return typeof f === 'function';
  }

  // own-property check without touching any shared object blueprint
  var hasOwnProp = ({}).hasOwnProperty;

  function has(o, k) {
    return hasOwnProp.call(o, k);
  }

  function copyObj(o) {
    var r = {}, k;
    if (!o) return r;
    for (k in o) { if (has(o, k)) r[k] = o[k]; }
    return r;
  }

  // djb2 hash -> unsigned base36 string (stable short id)
  function hashCode(str) {
    str = String(str || '');
    var h = 5381, i;
    for (i = 0; i < str.length; i++) {
      h = ((h << 5) + h) + str.charCodeAt(i);
      h = h & 0xffffffff;
    }
    return (h >>> 0).toString(36);
  }

  function absolutePoster(src) {
    src = String(src || '');
    if (!src) return '';
    if (src.indexOf('//') === 0) return 'https:' + src;
    if (src.charAt(0) === '/') return BASE + src;
    return src;
  }

  function num(v) {
    var n = parseFloat(v);
    return isNaN(n) ? 0 : n;
  }

  function trim(v) {
    return String(v == null ? '' : v).replace(/^\s+/, '').replace(/\s+$/, '');
  }

  // lowercase, single spaces, punctuation stripped — used for title matching
  function normalizeName(v) {
    return trim(String(v == null ? '' : v)
      .toLowerCase()
      .replace(/[.,;:\-_'"()\[\]&]/g, ' ')
      .replace(/\s+/g, ' '));
  }

  // group labels come from the server as plain russian words; normalise them so
  // "На этой неделе" / "на этой неделе " both match (ё is folded to е)
  function normGroup(v) {
    return trim(String(v == null ? '' : v).toLowerCase()
      .replace(/ё/g, 'е')
      .replace(/\s+/g, ' '));
  }

  function esc(s) {
    return String(s == null ? '' : s)
      .replace(/&/g, '&amp;').replace(/</g, '&lt;')
      .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  }

  function noty(text) {
    try {
      if (Lampa.Noty && isFn(Lampa.Noty.show)) Lampa.Noty.show(text);
    } catch (e) {}
  }

  function lsSet(key, val) {
    try {
      if (key.indexOf(LS_PREFIX) !== 0) return;
      if (Lampa.Storage && isFn(Lampa.Storage.set)) Lampa.Storage.set(key, val);
    } catch (e) {}
  }

  function lsGet(key) {
    try {
      if (key.indexOf(LS_PREFIX) !== 0) return null;
      if (Lampa.Storage && isFn(Lampa.Storage.get)) return Lampa.Storage.get(key, null);
      return null;
    } catch (e) { return null; }
  }

  function lsRemove(key) {
    try {
      if (key.indexOf(LS_PREFIX) !== 0) return;
      if (!Lampa.Storage || !isFn(Lampa.Storage.set)) return;
      Lampa.Storage.set(key, '');
    } catch (e) {}
  }

  // ---------------------------------------------- (G) session ownership
  //
  // The session lives in Lampa.Storage['lostfilm_v2_session'] and is attached to
  // every /lite/lostfilm request as the X-Lf-Session header. The value is a
  // credential: it is NEVER logged, NEVER put into the UI, and only its uid part
  // (masked, 8 chars) may be shown. It arrives exclusively from GET /status while
  // the server reports auth:true; no other code path writes it.

  function sessionGet() {
    var v = lsGet(LS_SESSION);
    if (!v || typeof v !== 'string') return '';
    v = trim(v);
    return SESSION_RE.test(v) ? v : '';
  }

  function sessionSet(value) {
    value = trim(value);
    if (!SESSION_RE.test(value)) return false;
    if (sessionGet() === value) return true;
    lsSet(LS_SESSION, value);
    return true;
  }

  function sessionClear() {
    try {
      if (Lampa.Storage && isFn(Lampa.Storage.remove)) {
        // some builds ship Storage.remove(name)
        Lampa.Storage.remove(LS_SESSION);
      }
    } catch (e) {}
    lsRemove(LS_SESSION);
  }

  // uid part only — the one fragment that is safe to display
  function sessionUid() {
    var s = sessionGet();
    return s ? s.split('.')[0].substring(0, 8) : '';
  }

  // params object for Lampa.Reguest (silent/native): header attached only when
  // a well-formed session is stored, so guests and unbound servers keep working.
  //
  // SECURITY: the header is attached ONLY to our OWN /lite/lostfilm endpoints.
  // netRaw() also performs the TMDB search, and the session must never leave the
  // Lampa host — anything not same-origin + under API_BASE gets no headers.
  function isOwnApiUrl(url) {
    url = String(url || '');
    if (url.indexOf('http') === 0 || url.indexOf('//') === 0) {
      if (!BASE) return false;
      if (url.indexOf(BASE + '/') !== 0) return false;
      url = url.substring(BASE.length);
    }
    return url.indexOf(API_BASE) === 0;
  }

  function netParams(url) {
    if (!isOwnApiUrl(url)) return false;
    var s = sessionGet();
    if (!s) return false;
    var h = {};
    h[SESSION_HEADER] = s;
    return { headers: h };
  }

  function sessionExpiredNotice() {
    if (sessionExpiredShown) return;
    sessionExpiredShown = true;
    noty('LostFilm: сессия истекла, привяжите аккаунт');
  }

  var sessionExpiredShown = false;

  // A credential-bearing request just failed. Re-ask /status once (debounced by
  // a timestamp — no timers, no observers) so a dead session is dropped and the
  // user sees why. Guests (no stored session) never trigger this.
  var lastSessionCheck = 0;

  function sessionCheckOnFailure() {
    if (!sessionGet()) return;
    var now = Date.now();
    if (now - lastSessionCheck < 30000) return;
    lastSessionCheck = now;
    apiStatus(function () { renderStatusNodes(); });
  }

  // ------------------------------------------------------------ ttl cache

  var cache = {};

  function cacheGet(key) {
    var e = cache[key];
    if (!e) return null;
    if ((Date.now() - e.t) > CACHE_TTL) { delete cache[key]; return null; }
    return e.d;
  }

  function cacheSet(key, data) {
    var keys = [], k, i;
    for (k in cache) { if (has(cache, k)) keys.push(k); }
    if (keys.length >= CACHE_MAX) {
      keys.sort(function (a, b) { return cache[a].t - cache[b].t; });
      var drop = keys.length - CACHE_MAX + 1;
      for (i = 0; i < drop; i++) delete cache[keys[i]];
    }
    cache[key] = { t: Date.now(), d: data };
  }

  function cacheInvalidate(prefix) {
    prefix = String(prefix || '');
    var k;
    for (k in cache) {
      if (has(cache, k) && k.indexOf(prefix) === 0) delete cache[k];
    }
  }

  // (v2.6) drop every cache entry whose key CONTAINS a marker. Used by the filter
  // panel: a filter change only has to invalidate the CATALOGUE responses, never
  // the registry / my / new / schedule ones. invalidate() keeps its broad,
  // prefix-based drop wherever a full wipe is genuinely required.
  function cacheInvalidateMatch(marker) {
    marker = String(marker || '');
    if (!marker) return;
    var k;
    for (k in cache) {
      if (has(cache, k) && k.indexOf(marker) >= 0) delete cache[k];
    }
  }

  // ------------------------------------------------------------------ net

  var net = null;
  try {
    if (Lampa.Reguest) {
      net = new Lampa.Reguest();
      if (isFn(net.timeout)) net.timeout(15000);
    }
  } catch (e) {
    net = null;
  }

  function parseGuard(data) {
    if (data == null) return null;
    if (typeof data === 'string') {
      var s = data.replace(/^\s+/, '');
      if (!s) return null;
      try { return JSON.parse(s); } catch (e) { return null; }
    }
    if (typeof data === 'object') {
      if (data.status && data.status >= 400) return null;
      if (!data.data) return null;
      return data;
    }
    return null;
  }

  function netGet(url, resolve, reject) {
    url = String(url || '');
    if (url.indexOf('http') !== 0) url = BASE + url;

    var cached = cacheGet(url);
    if (cached) { resolve(cached); return; }

    if (!net) { if (reject) reject(); return; }

    try {
      if (isFn(net.timeout)) net.timeout(15000);
      // (G) silent(url, complite, error, post_data, params) -> params.headers
      net.silent(url, function (data) {
        var parsed = parseGuard(data);
        if (!parsed) { if (reject) reject(); return; }
        cacheSet(url, parsed);
        resolve(parsed);
      }, function () {
        sessionCheckOnFailure();
        if (reject) reject();
      }, false, netParams(url));
    } catch (e) {
      if (reject) reject();
    }
  }

  // raw json GET (no guard, no cache) — for TMDB search + mutation endpoints
  function netRaw(url, resolve, reject) {
    url = String(url || '');
    if (url.indexOf('http') !== 0) url = BASE + url;

    if (!net) { if (reject) reject(); return; }

    try {
      if (isFn(net.timeout)) net.timeout(15000);
      net.silent(url, function (data) {
        var obj = data;
        if (typeof data === 'string') {
          var s = data.replace(/^\s+/, '');
          if (!s) { reject(); return; }
          try { obj = JSON.parse(s); } catch (e) { obj = null; }
        }
        if (!obj || typeof obj !== 'object') { reject(); return; }
        resolve(obj);
      }, function () {
        sessionCheckOnFailure();
        if (reject) reject();
      }, false, netParams(url));
    } catch (e) {
      if (reject) reject();
    }
  }

  // plain-text request (HTML forms) — fetch with timeout, cb always called
  function bindRequest(url, opts, cb) {
    var done = false;
    var ctl = null;
    var timer = null;

    function finish(res) {
      if (done) return;
      done = true;
      if (timer) { try { clearTimeout(timer); } catch (e) {} timer = null; }
      if (isFn(cb)) {
        try { cb(res); } catch (e) { console.warn('lostfilm v2 bind cb fail', e); }
      }
    }

    if (typeof fetch !== 'function') { finish(null); return; }

    try {
      if (typeof AbortController !== 'undefined') {
        ctl = new AbortController();
        timer = setTimeout(function () {
          try { ctl.abort(); } catch (e) {}
          finish(null);
        }, 15000);
      }
    } catch (e) { ctl = null; }

    opts = opts || {};
    var params = { method: opts.method || 'GET', headers: opts.headers || {} };
    if (ctl) params.signal = ctl.signal;
    if (opts.body != null) params.body = opts.body;

    try {
      fetch(url, params).then(function (r) {
        return r.text();
      }).then(function (text) {
        finish(text);
      }, function () {
        finish(null);
      });
    } catch (e) {
      finish(null);
    }
  }

  function bindGet(url, cb) {
    bindRequest(url, { method: 'GET' }, cb);
  }

  function bindPost(url, body, cb) {
    bindRequest(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8' },
      body: body
    }, cb);
  }

  // (J) `page` selects the SERVER page of /new (1..100). page <= 1 is omitted so
  // page 1 keeps the exact legacy URL (and its already-warm client cache key).
  function endpoint(kind, limit, page) {
    if (kind === 'new') {
      page = parseInt(page, 10);
      if (!(page > 1)) page = 1;
      return API_BASE + '/new?limit=' + (limit || LINE_LIMIT) +
        (page > 1 ? '&page=' + page : '');
    }
    if (kind === 'schedule') return API_BASE + '/schedule';
    return API_BASE + '/my';
  }

  // ------------------------------------------------------- auth / api calls

  // null = unknown, true/false = last known
  var authState = null;
  var authUser = '';
  var cookieValid = null;
  var status_nodes = [];

  function apiGet(path, cb) {
    netGet(BASE + path, function (data) { cb(data || null); }, function () { cb(null); });
  }

  // NOTE: /status must never be served from cache — after a bind we need fresh data
  function apiStatus(cb) {
    // (G) was the request made with OUR session? if so an auth:false/cookie_valid:false
    // answer means the stored credential is dead and must be dropped.
    var sent = sessionGet();

    netRaw(API_BASE + '/status', function (d) {
      if (d) {
        authState = !!d.auth;
        authUser = d.user ? String(d.user) : '';
        cookieValid = (typeof d.cookie_valid === 'undefined' ? null : !!d.cookie_valid);

        // (G) the server hands out the FULL lf_session only while auth:true.
        // Empty / invalid values are ignored, never written to Storage.
        if (authState && typeof d.session === 'string' && d.session) {
          if (sessionSet(d.session)) {
            authUser = String(d.session).split('.')[0];
            sessionExpiredShown = false;
          }
        }

        if (sent && (authState === false || cookieValid === false)) {
          sessionClear();
          cookieValid = false;
          sessionExpiredNotice();
          invalidate();
        }

        lsSet(LS_STATUS, JSON.stringify({
          auth: authState, user: authUser, cv: cookieValid, ts: Date.now()
        }));
      }
      if (isFn(cb)) cb(d);
    }, function () {
      if (isFn(cb)) cb(null);
    });
  }

  // when we know there is no session — return an empty envelope, never an error
  function apiMy(cb) {
    if (authState === false) { cb({ data: [] }); return; }

    apiGet(API_BASE + '/my', function (d) {
      if (!d && authState === false) { cb({ data: [] }); return; }
      cb(d);
    });
  }

  // (J) page-aware /new fetch. apiNew(limit, cb) stays valid (page defaults to 1).
  function apiNew(limit, page, cb) {
    if (isFn(page)) { cb = page; page = 1; }
    netGet(endpoint('new', limit || LINE_LIMIT, page), function (d) { cb(d || null); },
      function () { cb(null); });
  }

  function apiSchedule(cb) {
    netGet(endpoint('schedule'), function (d) { cb(d || null); }, function () { cb(null); });
  }

  function statusText() {
    var tail = (cookieValid === false ? ' (сессия истекла)' : '');
    if (authState === true) {
      // (G) only the masked uid is ever rendered — never the session itself
      var uid = authUser ? authUser.substring(0, 8) : sessionUid();
      return 'Авторизован' + (uid ? ': ' + uid : '') + tail;
    }
    return 'Не авторизован' + tail;
  }

  function renderStatusNodes() {
    var i, node, text = statusText();
    for (i = 0; i < status_nodes.length; i++) {
      node = status_nodes[i];
      try {
        if (!node || !node.length) continue;
        var t = node.find('.settings-param__descr');
        if (t.length) t.text(text);
        else node.append('<div class="settings-param__descr">' + esc(text) + '</div>');
      } catch (e) {}
    }
  }

  // called on start and after a successful bind.
  // cb (optional) is invoked once the FRESH /status answer has been processed
  // (used by the bind modal to confirm that the session was taken over).
  function refreshStatus(cb) {
    try {
      var cached = lsGet(LS_STATUS);
      if (cached) {
        try {
          var o = JSON.parse(cached);
          if (o && typeof o.auth !== 'undefined') {
            authState = !!o.auth;
            authUser = o.user || '';
            cookieValid = (typeof o.cv === 'undefined' ? null : o.cv);
            renderStatusNodes();
          }
        } catch (e) {}
      }
    } catch (e) {}

    apiStatus(function (d) {
      renderStatusNodes();
      if (isFn(cb)) { try { cb(d); } catch (e) {} }
    });
  }

  // (G) called after a successful bind: wait for the fresh /status and report the
  // outcome WITHOUT ever exposing the credential (only a boolean leaks out).
  function sessionClaim(cb) {
    refreshStatus(function () {
      if (!isFn(cb)) return;
      try { cb(sessionGet() ? true : false); } catch (e) {}
    });
  }

  // --------------------------------- (E)(K) HQ poster upgrade: poster.jpg
  //
  // v2.2 upgraded the tiny badge ("Posters/icon.jpg", 40x40) to the mid-size
  // "Posters/image.jpg" (240x144). v2.3 goes one step further: the CDN also
  // serves "Posters/poster.jpg" (715x330, 44-187 KB) — the very file LostFilm
  // publishes as og:image on its own series pages — and a full name dictionary
  // probe (image_original/hq/big/large/full, poster_original/hq/big/large/full,
  // original/big/large/hq/full/cover/photo, *_2x, .webp, .png, portrait,
  // vertical, fanart, background, backdrop, poster_s<N>) confirms NOTHING
  // bigger exists: all 404. poster.jpg is 200 on 73/73 ids probed across both
  // catalogues and the old backlog, so it is the maximum.
  //
  // The core renders the cards itself — we cannot attach an onerror handler to
  // its <img>. Each variant is therefore preflighted ONCE per session with a
  // throwaway Image(): a 404 is remembered in posterBad['<id>/<name>'] and the
  // next render steps one tier down the chain. Session-only — nothing is
  // written to Lampa.Storage.

  // (K) the variant chain. The CDN exposes exactly three sizes for an id:
  //     icon.jpg (40x40)  <  image.jpg (240x144)  <  poster.jpg (715x330)
  // Every other name 404s (the full dictionary probe is quoted in the header).
  // A URL is therefore walked DOWN this list, skipping the variants already known
  // to 404 for that id, so the biggest usable one wins.
  var POSTER_CHAIN = {
    'icon.jpg': ['poster.jpg', 'image.jpg', 'icon.jpg'],
    'image.jpg': ['poster.jpg', 'image.jpg']
  };

  var posterKnown = {};      // { '<id>/<name>': true }  true = 200 OK (checked)
  var posterBad = {};        // { '<id>/<name>': true }  true = 404, step down
  var posterBadCount = 0;
  var posterProbing = {};    // { '<id>/<name>': true }  preflight in flight
  var POSTER_BAD_MAX = 800;  // hard cap: the maps are reset instead of growing forever

  var POSTER_RE = /^(.*\/Images\/(\d+)\/Posters\/)([A-Za-z0-9_.-]+)$/i;

  // '<host>/Images/<id>/Posters/<name>' (absolute or relative) -> {head, id, name}
  function posterParts(url) {
    var m = POSTER_RE.exec(String(url || ''));
    if (!m) return null;
    return { head: m[1], id: m[2], name: m[3].toLowerCase() };
  }

  function posterKey(id, name) {
    return id + '/' + name;
  }

  function posterBadReset() {
    posterKnown = {};
    posterBad = {};
    posterBadCount = 0;
    posterProbing = {};
  }

  // Probe ONE variant with a throwaway Image(): the result is remembered for the
  // rest of the session (true = 200, false = 404) so a given pair is fetched at
  // most once. A 404 makes the NEXT render step one tier down the chain.
  // Session-only, never written to Lampa.Storage.
  function preflightPoster(url, key) {
    if (!key) return;
    if (has(posterKnown, key) || has(posterBad, key) || has(posterProbing, key)) return;

    if (posterBadCount >= POSTER_BAD_MAX) posterBadReset();

    // no Image() (very old engine) — remember nothing, just serve the URL
    if (typeof Image !== 'function') return;

    posterProbing[key] = true;

    try {
      var probe = new Image();
      probe.onerror = function () {
        delete posterProbing[key];
        posterBad[key] = true;
        posterBadCount++;
        probe.onerror = null; probe.onload = null;
      };
      probe.onload = function () {
        delete posterProbing[key];
        posterKnown[key] = true;
        probe.onload = null; probe.onerror = null;
      };
      probe.src = url;
    } catch (e) {
      delete posterProbing[key];
    }
  }

  // original (absolute or relative, from the server) -> biggest usable variant.
  // URLs outside /Images/<id>/Posters/ (foreign host, 'image_s<N>.jpg' season
  // poster, an already-upgraded 'poster.jpg') are returned untouched.
  function upgradePosterUrl(url) {
    var p = posterParts(url);
    if (!p) return url;

    // own-property lookup on purpose: a server-supplied name like 'constructor'
    // or '__proto__' must not fall through to Object.prototype members
    if (!has(POSTER_CHAIN, p.name)) return url;
    var chain = POSTER_CHAIN[p.name];
    if (!chain || !chain.length) return url;

    var i, cand, key, full;
    for (i = 0; i < chain.length; i++) {
      cand = chain[i];
      key = posterKey(p.id, cand);
      if (has(posterBad, key)) continue;   // known 404 -> step down a tier

      full = p.head + cand;
      // already verified 200 this session -> serve straight away, no re-probe
      if (has(posterKnown, key)) return full;

      preflightPoster(full, key);          // learn the result in the background
      return full;
    }

    return url;                            // every tier is known bad -> original
  }

  function posterFor(original) {
    return upgradePosterUrl(original);
  }

  // -------------------------------------- (M) TMDB poster map + resolve queue
  //
  // See the (M) header note. Short version: the catalogue keeps its LostFilm
  // posters on the first render, a background queue maps ids to TMDB in the
  // meantime, and the map (persisted in Lampa.Storage) makes every LATER render
  // show TMDB posters with no requests at all.
  //
  // Hard rules honoured here:
  //   * NO DOM patching / observers / intervals — one recursive setTimeout only.
  //   * TMDB.api is NEVER monkey-patched: the search URL is built exactly the way
  //     full() builds it and fetched read-only through the shared Reguest.
  //   * an EMPTY result is never cached (a miss stays unmapped and may be retried
  //     on the next catalogue load).

  var tmdbMap = null;         // loaded lazily from LS_TMDB_MAP, null = not loaded
  var tmdbQueue = [];         // [{id, title}] waiting to be resolved
  var tmdbQueued = {};        // own-property set of ids already in tmdbQueue
  var tmdbPending = {};       // {id: {id,p,t}} resolved since the last flush
  var tmdbPendingN = 0;
  var tmdbTimer = null;       // the single outstanding setTimeout, if any
  var tmdbFlushing = false;

  // '1' | 1 | true -> on; anything else ('' | '0' | false | undefined) -> off.
  // Storage.field() is Params-backed and returns the 'true'/'false' STRINGS of a
  // trigger, so the numeric test is the portable one here. Missing value (the
  // param not registered yet, e.g. an early call) counts as ON — the default.
  function tmdbPostersEnabled() {
    try {
      if (!Lampa.Storage || !isFn(Lampa.Storage.field)) return true;
      var v = Lampa.Storage.field(LS_TMDB_POSTERS);
      if (v == null || v === '') return true;
      if (v === true || v === 1 || v === '1') return true;
      if (v === false || v === 0 || v === '0') return false;
      return String(v) !== 'false';
    } catch (e) { return true; }
  }

  // the persisted map, read once per session. A broken JSON string degrades to
  // an empty map — never to a crash, and never blocks the catalogue.
  function tmdbMapLoad() {
    if (tmdbMap) return tmdbMap;
    tmdbMap = {};
    try {
      var raw = lsGet(LS_TMDB_MAP);
      if (raw && typeof raw === 'string') {
        var obj = JSON.parse(raw);
        if (obj && typeof obj === 'object') {
          var k;
          for (k in obj) { if (has(obj, k)) tmdbMap[k] = obj[k]; }
        }
      }
    } catch (e) { tmdbMap = {}; }
    return tmdbMap;
  }

  function tmdbMapGet(lfId) {
    var m = tmdbMapLoad();
    if (!m) return null;
    lfId = String(lfId == null ? '' : lfId);
    if (!lfId || !has(m, lfId)) return null;
    var e = m[lfId];
    if (!e || typeof e !== 'object' || !e.p) return null;
    return e;
  }

  // Drop the oldest entries (by ts) once TMDB_MAP_MAX is exceeded.
  function tmdbMapCap(map) {
    var keys = [], k, i;
    for (k in map) { if (has(map, k)) keys.push(k); }
    if (keys.length <= TMDB_MAP_MAX) return map;
    keys.sort(function (a, b) {
      return num(map[a] && map[a].ts) - num(map[b] && map[b].ts);
    });
    var drop = keys.length - TMDB_MAP_MAX;
    for (i = 0; i < drop; i++) delete map[keys[i]];
    return map;
  }

  // flush the batch of freshly resolved entries into Storage (one write per
  // batch, not one per resolve — Storage.set serialises the whole map).
  function tmdbMapFlush() {
    if (tmdbFlushing) return;
    if (tmdbPendingN <= 0) return;
    tmdbFlushing = true;

    var map = tmdbMapLoad();
    var k;
    for (k in tmdbPending) {
      if (has(tmdbPending, k)) map[k] = tmdbPending[k];
    }
    tmdbPending = {};
    tmdbPendingN = 0;
    tmdbMapCap(map);

    try { lsSet(LS_TMDB_MAP, JSON.stringify(map)); } catch (e) {}

    tmdbFlushing = false;
  }

  function tmdbMapDrop() {
    tmdbMap = {};
    tmdbPending = {};
    tmdbPendingN = 0;
    tmdbQueue = [];
    tmdbQueued = {};
    if (tmdbTimer) { try { clearTimeout(tmdbTimer); } catch (e) {} tmdbTimer = null; }
    try {
      if (Lampa.Storage && isFn(Lampa.Storage.remove)) Lampa.Storage.remove(LS_TMDB_MAP);
    } catch (e) {}
    lsRemove(LS_TMDB_MAP);
  }

  function tmdbEnqueue(lfId, title, kind) {
    lfId = String(lfId == null ? '' : lfId);
    title = trim(title);
    if (!lfId || !title) return;
    if (has(tmdbQueued, lfId)) return;
    if (tmdbQueue.length >= TMDB_QUEUE_MAX) return;
    tmdbQueued[lfId] = true;
    tmdbQueue.push({ id: lfId, title: title, kind: kind || '' });
  }

  // the media_type the core should use for a catalogue kind
  function tmdbWantType(kind) {
    return kind === 'movies' ? 'movie' : 'tv';
  }

  // ONE queue step: take the head, search TMDB, remember a HIT (a miss is
  // dropped on purpose — it may be retried on the next catalogue load).
  //
  // SERIAL + PACED: the next step is scheduled only from the completion callback
  // of the current request, so two searches are NEVER in flight at once, and the
  // gap is a 250 ms FLOOR measured from the request start (when TMDB itself is
  // slower than 250 ms — it usually is, ~430 ms — the floor is already met and
  // the next request starts immediately after the previous one finishes).
  // The timer exists only while the queue is non-empty: an idle screen is free.
  function tmdbQueueStep() {
    tmdbTimer = null;

    if (!tmdbQueue.length) { tmdbMapFlush(); return; }

    var job = tmdbQueue.shift();
    if (job) delete tmdbQueued[job.id];

    var started = Date.now();

    function next() {
      if (tmdbTimer) return;                  // one timer at a time, ever
      var wait = TMDB_RATE_MS - (Date.now() - started);
      if (wait < 0) wait = 0;
      tmdbTimer = setTimeout(tmdbQueueStep, wait);
    }

    // re-check: a previous batch (or the other line) may have mapped it already
    if (job && !tmdbMapGet(job.id) && tmdbPostersEnabled()) {
      tmdbResolve(job.id, job.title, job.kind, next);
    } else {
      next();
    }
  }

  function tmdbQueueStart() {
    if (tmdbTimer) return;              // the single timer already runs the queue
    if (!tmdbQueue.length) return;
    tmdbTimer = setTimeout(tmdbQueueStep, TMDB_RATE_MS);
  }

  // serialise one resolved entry into the map + the pending batch
  function tmdbRemember(lfId, found, method) {
    if (!found || found.id == null) return;

    // an empty poster_path is a miss: remember NOTHING (the cache must never
    // hold a blank URL — the exact bug the podborki pub/sub cache guards against)
    var path = found.poster_path ? String(found.poster_path) : '';
    if (!path) return;

    var rec = {
      id: found.id,
      p: path,
      t: (method === 'tv' ? 'tv' : 'movie'),
      ts: Date.now()
    };

    tmdbPending[String(lfId)] = rec;
    tmdbPendingN++;

    // the in-memory map is updated right away so the NEXT render in THIS session
    // already sees the TMDB poster (the Storage write happens per batch)
    var map = tmdbMapLoad();
    map[String(lfId)] = rec;

    if (tmdbPendingN >= TMDB_BATCH) tmdbMapFlush();
  }

  // search/multi -> pick -> remember. Read-only; TMDB.api is never patched.
  // kind: 'serial' | 'movies' -> the expected TMDB media_type ('tv' | 'movie').
  // done() is ALWAYS called exactly once (ok and error paths) — it is what keeps
  // the queue serial.
  function tmdbResolve(lfId, query, kind, done) {
    var finished = false;
    function finish() {
      if (finished) return;
      finished = true;
      if (isFn(done)) { try { done(); } catch (e) {} }
    }

    var lang = 'ru';
    try { lang = Lampa.Storage.get('language') || 'ru'; } catch (e) {}

    var url;
    try {
      if (!Lampa.TMDB || !isFn(Lampa.TMDB.api) || !isFn(Lampa.TMDB.key)) { finish(); return; }
      url = tmdbSearchUrl(query, lang);      // the very same URL builder as full()
    } catch (e) { finish(); return; }

    var want = tmdbWantType(kind);           // serial -> tv, movies -> movie

    netRaw(url, function (json) {
      var list = (json && json.results) || [];
      if (!list.length) { finish(); return; }

      var found = pickTmdbResult(list, query, query, want);
      if (!found) {
        // fuzzy: first non-person hit of the MATCHING media_type (the same policy
        // full() uses, with the catalogue's own log tag)
        var i, r, rmt;
        for (i = 0; i < list.length; i++) {
          r = list[i];
          rmt = resultMediaType(r);
          if (r && rmt !== 'person' && (!want || !rmt || rmt === want)) {
            found = r; break;
          }
        }
        if (found) console.log('lostfilm: tmdb fuzzy id match');
      }
      if (!found) { finish(); return; }

      var method = resultMediaType(found);
      if (method !== 'tv' && method !== 'movie') method = want;

      tmdbRemember(lfId, found, method);
      finish();
    }, function () { finish(); });
  }

  // -------------------------------------------------------- normalization

  // kind: 'my' | 'new' | 'schedule'
  function normalizeCard(item, kind) {
    item = item || {};

    var title = item.title || '';
    var iconPoster = absolutePoster(item.poster);   // always absolute (server sends absolute)
    var poster = posterFor(iconPoster);             // (E) HQ upgrade where possible
    var id;

    if (kind === 'my') id = String(item.id == null ? '' : item.id);
    else id = 'lf' + hashCode(item.link);

    var season = num(item.season);
    var episode = num(item.episode);

    // schedule cards carry the airing day/episode in the title so the row is readable
    var cardTitle = title;
    if (kind === 'schedule' && (season > 0 || episode > 0)) {
      cardTitle = title + ' (' + 'S' + season + 'E' + episode + ')';
    }

    var card = {};

    card.id = id;
    card.title = cardTitle;
    card.name = cardTitle;
    card.original_title = item.title_en || title;
    card.original_name = item.title_en || title;

    // (A) POSTER — see the v2.1 header note.
    // poster_path / profile_path MUST stay empty for our source: the core pipes them
    // through Lampa.Api.img() -> 't/p/<size>/' + src -> image.tmdb.org, which destroys
    // an absolute lostfilm URL. card.poster and card.img are used verbatim.
    card.poster = poster;
    card.img = poster;
    card.poster_path = '';   // explicitly empty, never an absolute URL

    // (E) the untouched server value, kept for reference / manual fallback
    card.lostfilm_icon = iconPoster;

    card.overview = item.desc || '';
    card.vote_average = kind === 'new' ? num(item.rating) : 0;
    card.media_type = item.movie ? 'movie' : 'tv';

    card.lostfilm_id = (item.id == null ? null : item.id);
    card.lostfilm_link = item.link || '';
    card.lostfilm_episode_link = item.episodeLink || '';
    card.lostfilm_follow = !!item.follow;
    card.lostfilm_watched = num(item.watched);
    card.lostfilm_episodes = num(item.episodes);
    card.lostfilm_episode_id = item.episodeId || null;
    card.lostfilm_season = (item.season == null ? 0 : item.season);
    card.lostfilm_episode = (item.episode == null ? 0 : item.episode);
    card.lostfilm_code = item.code || '';
    card.lostfilm_group = item.group || '';
    card.lostfilm_date = item.date || '';

    card.source = SOURCE;
    if (Lampa.Utils && isFn(Lampa.Utils.addSource)) {
      try { Lampa.Utils.addSource(card, SOURCE); } catch (e) {}
      // addSource only rewrites arrays / {results:[]}, plain cards need it explicit
    }
    if (!card.source) card.source = SOURCE;

    return card;
  }

  function buildCards(items, kind) {
    var results = [], i;
    items = items || [];
    for (i = 0; i < items.length; i++) results.push(normalizeCard(items[i], kind));
    return results;
  }

  function buildResults(kind, data) {
    var raw = (data && data.data) || [];
    return buildCards(raw, kind);
  }

  // ------------------------------------------------- (H) full catalogue
  //
  // GET /lite/lostfilm/catalog?act=serial|movies -> 805 / 257 items (12 h server
  // cache). The same response feeds the two stock lines AND the paginated
  // "Все сериалы"/"Все фильмы" screens, through the shared ttl cache of netGet().
  //
  // FILTERS: there is deliberately NO DOM patching of the line header (that would
  // be a monkey-patch of core markup). Filtering/sorting is configured in the
  // LostFilm settings folder (SettingsApi select params) and applied here, right
  // before the cards are emitted. Defaults are harmless: any year / any genre /
  // any status, sorted by rating desc (unrated items last).

  var CAT_KINDS = ['serial', 'movies'];

  function isCatKind(v) {
    v = String(v || '');
    return v === 'serial' || v === 'movies';
  }

  // 'catalog:serial' <-> {kind}
  function catalogUrl(kind) {
    return 'catalog:' + kind;
  }

  function parseCatalogUrl(url) {
    var m = /^catalog:(serial|movies)$/.exec(String(url || ''));
    return m ? m[1] : null;
  }

  // the item.img '/Static/Images/...' -> absolute CDN URL without '/Static'.
  // Anything already absolute (or not a /Static path) is only made absolute.
  function catalogPoster(src) {
    src = trim(src);
    if (!src) return '';
    if (src.indexOf('/Static/') === 0) {
      return IMG_HOST + src.replace('/Static', '');
    }
    return absolutePoster(src);
  }

  // catalogue item -> TMDB-like card. Two poster branches (see the (M) note):
  //   * mapped in the TMDB map  -> poster_path = '<tmdb relative path>' and
  //     poster/img EMPTY, so the core's TMDB.img(poster_path) builds a proper
  //     image.tmdb.org URL (the (A) note says why an absolute URL must never
  //     land in poster_path — and why a RELATIVE one is exactly right);
  //   * not mapped -> the LostFilm poster goes to poster/img and poster_path
  //     stays '' (the pre-v2.4 behaviour); the caller enqueues it in the
  //     background.
  function normalizeCatalogItem(item, kind) {
    item = item || {};

    var id = String(item.id == null ? '' : item.id);
    var title = item.title || '';
    var orig = item.title_orig || item.title || '';
    var img = catalogPoster(item.img);

    // (K) the catalogue ships 'Posters/image.jpg' (240x144); upgradePosterUrl()
    // walks it up to 'Posters/poster.jpg' (715x330) — the largest variant the
    // CDN exposes — and remembers a 404 in the background.
    var poster = posterFor(img);

    // (M) is this id already resolved to a TMDB poster?
    var tmapped = tmdbPostersEnabled() ? tmdbMapGet(id) : null;

    var card = {};

    card.id = 'lfc' + id;           // 'lfc' (lostfilm catalog) + server id
    card.title = title;
    card.name = title;
    card.original_title = orig;
    card.original_name = orig;

    if (tmapped) {
      // (M) TMDB branch: the RELATIVE path is what the core expects here
      card.poster_path = tmapped.p;
      card.poster = '';             // must stay empty: the core prefers it over
      card.img = '';                // poster_path and would bypass the TMDB URL
      card.tmdb_id = tmapped.id;
      card.lostfilm_icon = img;     // keep the LostFilm URL for reference
    } else {
      card.poster = poster;
      card.img = poster;
      card.poster_path = '';        // critical: never an absolute URL here
      card.lostfilm_icon = img;
    }

    card.overview = '';
    card.vote_average = num(item.rating);
    card.media_type = (String(item.ismovie || '') === '1' ||
                       kind === 'movies') ? 'movie' : 'tv';

    // release_date / first_air_date carry the year so the core shows the badge
    var year = trim(item.date);
    if (/^\d{4}$/.test(year)) {
      if (card.media_type === 'movie') card.release_date = year + '-01-01';
      else card.first_air_date = year + '-01-01';
    }

    card.lostfilm_id = (item.id == null ? null : item.id);
    card.lostfilm_link = item.link || '';
    card.lostfilm_title_en = orig;
    card.lostfilm_genres = item.genres || '';
    card.lostfilm_channels = item.channels || '';
    card.lostfilm_year = item.date || '';
    card.lostfilm_status = (item.status == null ? '' : String(item.status));
    card.lostfilm_status_auto = (item.status_auto == null ? '' : String(item.status_auto));
    card.lostfilm_status_season = (item.status_season == null ? '' : String(item.status_season));
    card.lostfilm_rating = num(item.rating);
    card.lostfilm_not_aired = !!item.not_aired;
    // R1: the server field flipped polarity — favourited:true is the follow marker
    card.lostfilm_follow = item.favorited === true;
    card.lostfilm_not_favorited = item.not_favorited === true;
    card.lostfilm_catalog = kind;
    card.lostfilm_source = true;

    card.source = SOURCE;
    if (Lampa.Utils && isFn(Lampa.Utils.addSource)) {
      try { Lampa.Utils.addSource(card, SOURCE); } catch (e) {}
    }
    if (!card.source) card.source = SOURCE;

    return card;
  }

  // ---------------------------------------------------- catalogue filters
  //
  // Storage keys: lostfilm_v2_filter_serial_{year,genre,status,sort} and the
  // same set for movies. Values come straight from SettingsApi selects.

  function filterKey(kind, field) {
    return LS_FILTER + kind + '_' + field;
  }

  function filterGet(kind, field, def) {
    var v = lsGet(filterKey(kind, field));
    return (v == null || v === '') ? def : String(v);
  }

  // 'all' | year as string
  function catYear(card) {
    var m = /(\d{4})/.exec(String(card.lostfilm_year || ''));
    return m ? m[1] : '';
  }

  function catGenres(card) {
    return String(card.lostfilm_genres || '');
  }

  // Status buckets — the raw server codes (status: 5|7, status_auto: 1|2|3|5)
  // mean nothing to a user, so four stable buckets are derived from them:
  //   completed = status 5 (завершён)
  //   ongoing   = status 7 and NOT not_aired and status_auto is 1/2/3 (идёт / между сезонами)
  //   announced = not_aired true, or status 7 with no season yet (анонс/премьера)
  //   'all'     = no filtering
  function catStatus(card) {
    var st = String(card.lostfilm_status || '');
    var auto = String(card.lostfilm_status_auto || '');

    if (card.lostfilm_not_aired === true) return 'announced';
    if (st === '5') return 'completed';
    if (st === '7') {
      if (auto === '1' || auto === '2' || auto === '3') return 'ongoing';
      return 'announced';
    }
    return 'ongoing';
  }

  var CAT_STATUS_VALUES = {
    'all': 'Все',
    'ongoing': 'Идёт / онгоинг',
    'completed': 'Завершён',
    'announced': 'Анонс / премьера'
  };

  function catSortValues() {
    return {
      'rating': 'По рейтингу',
      'year_new': 'По году (новые)',
      'year_old': 'По году (старые)',
      'title': 'По названию'
    };
  }

  // 'all' | genre name
  function applyCatalogFilters(cards, kind) {
    var year = filterGet(kind, 'year', 'all');
    var genre = filterGet(kind, 'genre', 'all');
    var status = filterGet(kind, 'status', 'all');
    var sort = filterGet(kind, 'sort', CAT_SORT);
    var out = [], i, c;

    for (i = 0; i < cards.length; i++) {
      c = cards[i];
      if (year !== 'all' && catYear(c) !== year) continue;
      if (genre !== 'all' && catGenres(c).indexOf(genre) < 0) continue;
      if (status !== 'all' && catStatus(c) !== status) continue;
      out.push(c);
    }

    out.sort(function (a, b) {
      return catCompare(a, b, sort);
    });

    return out;
  }

  function catCompare(a, b, sort) {
    if (sort === 'year_new' || sort === 'year_old') {
      var ya = parseInt(catYear(a), 10);
      var yb = parseInt(catYear(b), 10);
      if (!(ya > 0)) ya = sort === 'year_new' ? -1 : 99999;
      if (!(yb > 0)) yb = sort === 'year_new' ? -1 : 99999;
      if (ya !== yb) return sort === 'year_new' ? (yb - ya) : (ya - yb);
      return catRatingCompare(a, b);
    }

    if (sort === 'title') {
      var ta = String(a.title || a.original_title || '');
      var tb = String(b.title || b.original_title || '');
      return ta < tb ? -1 : (ta > tb ? 1 : 0);
    }

    return catRatingCompare(a, b);   // 'rating' (default)
  }

  // rating desc, unrated (0) last, stable by title inside the same rating
  function catRatingCompare(a, b) {
    var ra = num(a.lostfilm_rating);
    var rb = num(b.lostfilm_rating);
    if ((ra > 0) !== (rb > 0)) return ra > 0 ? -1 : 1;
    if (ra !== rb) return rb - ra;
    var ta = String(a.title || '');
    var tb = String(b.title || '');
    return ta < tb ? -1 : (ta > tb ? 1 : 0);
  }

  // one shared catalogue fetch (netGet caches by URL), then filter + cards.
  //
  // (v2.6) applyFilters: the CATEGORY LINES ("Все сериалы" / "Все фильмы") must
  // ALWAYS render. A persistent user filter that matches 0 items used to run
  // through applyCatalogFilters() for the line too -> an empty array -> the core
  // dropped the whole line (partNext() filters empty envelopes, see the (v2.6)
  // header note). The lines therefore pass applyFilters = false and emit the raw
  // server order; ONLY the paginated category_full screen (list()) passes true,
  // so filters/sorting affect the screen after "Ещё" and nothing else.
  function catalogCards(kind, cb, applyFilters) {
    if (!isCatKind(kind)) { cb([]); return; }

    netGet(API_BASE + '/catalog?act=' + kind + '&limit=' + CAT_TOP, function (data) {
      var raw = (data && data.data) || [];
      var cards = [], i;
      for (i = 0; i < raw.length; i++) cards.push(normalizeCatalogItem(raw[i], kind));

      // option lists for the settings selects are derived from the raw response
      catalogMeta[kind] = catalogMetaBuild(raw);

      // (v2.6) filters/sort are applied ONLY when the caller asks for them
      if (applyFilters === true) cards = applyCatalogFilters(cards, kind);

      // (M) answer the line FIRST (never block it on TMDB), then hand the
      // unmapped cards to the background queue. Only the RAW items that carry no
      // mapping yet are enqueued, and only while the feature is on.
      if (tmdbPostersEnabled()) {
        for (i = 0; i < raw.length; i++) {
          var ri = raw[i] || {};
          if (ri.id == null || ri.id === '') continue;
          if (tmdbMapGet(ri.id)) continue;
          tmdbEnqueue(ri.id, ri.title_orig || ri.title, kind);
        }
        tmdbQueueStart();
      }

      cb(cards);
    }, function () {
      cb([]);
    });
  }

  // year / genre option lists, taken from the LAST successful catalogue response
  // (kept session-only: they are rebuild hints, not user data).
  var catalogMeta = { serial: null, movies: null };
  var catalogMetaGranted = false;   // one catalogue prefetch per session

  function catalogMetaGet(kind) {
    return isCatKind(kind) ? catalogMeta[kind] : null;
  }

  function catalogMetaBuild(items) {
    var years = {}, genres = {}, i, j, it, list, g;
    items = items || [];
    for (i = 0; i < items.length; i++) {
      it = items[i] || {};
      var m = /(\d{4})/.exec(String(it.date || ''));
      if (m) years[m[1]] = m[1];
      list = String(it.genres || '').split(',');
      for (j = 0; j < list.length; j++) {
        g = trim(list[j]);
        if (g) genres[g] = g;
      }
    }
    return { years: years, genres: genres };
  }

  // 'all' + sorted real values, shaped for SettingsApi (values object)
  function selectValues(allTitle, map, numericDesc) {
    var out = { 'all': allTitle };
    var keys = [], k;
    for (k in map) { if (has(map, k)) keys.push(k); }
    keys.sort(function (a, b) {
      if (numericDesc) return parseInt(b, 10) - parseInt(a, 10);
      return a < b ? -1 : (a > b ? 1 : 0);
    });
    for (var i = 0; i < keys.length; i++) out[keys[i]] = keys[i];
    return out;
  }

  // SettingsApi select params are read through Params.field()/Storage, so the
  // values object must be (re)registered whenever we learn new options — the
  // public Params.select() is exactly what addParam itself calls for a select.
  //
  // (N) NOTE: no settings SELECT is rendered by this file any more — the panel
  // registers the option lists through Params.select() only so the values are
  // known to Lampa, while filterGet() reads the very SAME
  // 'lostfilm_v2_filter_<kind>_<field>' Storage key the panel writes.
  function catRegisterSelect(kind, field, values, allTitle, numericDesc) {
    if (!isCatKind(kind)) return;
    if (!Lampa.Params || !isFn(Lampa.Params.select)) return;
    try {
      Lampa.Params.select(filterKey(kind, field), selectValues(allTitle, values, numericDesc), 'all');
    } catch (e) {}
  }

  // (N) the ALL-option title of a meta-driven field ('year' / 'genre')
  function filterAllTitle(field) {
    return field === 'year' ? 'Все годы' : 'Все жанры';
  }

  // (N) the human label of the CURRENT value of a filter field, for the panel
  // row. Dead/unknown values degrade to the ALL title instead of an empty row.
  function filterLabel(kind, field) {
    var cur = filterGet(kind, field, 'all');
    if (field === 'sort') {
      var sv = catSortValues();
      return has(sv, cur) ? sv[cur] : sv[CAT_SORT];
    }
    if (field === 'status') {
      return has(CAT_STATUS_VALUES, cur) ? CAT_STATUS_VALUES[cur]
        : CAT_STATUS_VALUES['all'];
    }
    if (cur === 'all') return filterAllTitle(field);
    var meta = catalogMetaGet(kind);
    var map = meta ? (field === 'year' ? meta.years : meta.genres) : null;
    return (map && has(map, cur)) ? cur : filterAllTitle(field);
  }

  // --------------------------------------------- (N) on-screen filter panel
  //
  // See the (N) header note for the four core facts this leans on: the panel must
  // be a SIBLING of the activity scroll (inside it, Items.onScroll prunes it), it
  // must be RE-APPENDED on every Controller 'content' toggle (collectionSet
  // replaces the collection), scroll.minus(panel) keeps the layer from
  // overflowing, and Activity.replace({page:1}, false) is the reload.

  // order of the rows on screen; `title` is the row prefix
  var FILTER_FIELDS = [
    { field: 'sort',   title: 'Сортировка' },
    { field: 'year',   title: 'Год' },
    { field: 'genre',  title: 'Жанр' },
    { field: 'status', title: 'Статус' }
  ];

  // the screen the panel is currently attached to (null when none)
  var panelScreen = null;

  function filterDef(field) {
    var i;
    for (i = 0; i < FILTER_FIELDS.length; i++) {
      if (FILTER_FIELDS[i].field === field) return FILTER_FIELDS[i];
    }
    return null;
  }

  // write a filter value — the SAME Storage key the settings selects used
  function filterSet(kind, field, value) {
    lsSet(filterKey(kind, field), String(value));
  }

  function metaField(field) {
    return field === 'year' || field === 'genre';
  }

  // [{title, value, selected}] for the Select of one field
  function filterOptions(kind, field) {
    var out = [], map, keys = [], k, i;
    // per-field default: sort falls back to CAT_SORT, everything else to 'all'
    var cur = filterGet(kind, field, field === 'sort' ? CAT_SORT : 'all');

    if (field === 'sort') {
      map = catSortValues();
      for (k in map) { if (has(map, k)) keys.push(k); }
      for (i = 0; i < keys.length; i++) {
        out.push({ title: map[keys[i]], value: keys[i], selected: keys[i] === cur });
      }
      return out;
    }

    if (field === 'status') {
      for (k in CAT_STATUS_VALUES) {
        if (!has(CAT_STATUS_VALUES, k)) continue;
        out.push({ title: CAT_STATUS_VALUES[k], value: k, selected: k === cur });
      }
      return out;
    }

    // year / genre: meta-driven. An empty meta (first open, /catalog still in
    // flight) leaves just the ALL entry, which is always present.
    out.push({ title: filterAllTitle(field), value: 'all', selected: cur === 'all' });

    var meta = catalogMetaGet(kind);
    map = meta ? (field === 'year' ? meta.years : meta.genres) : null;
    if (map) { for (k in map) { if (has(map, k)) keys.push(k); } }
    keys.sort(function (a, b) {
      if (field === 'year') return parseInt(b, 10) - parseInt(a, 10);  // newest first
      return a < b ? -1 : (a > b ? 1 : 0);
    });
    for (i = 0; i < keys.length; i++) {
      out.push({ title: keys[i], value: keys[i], selected: keys[i] === cur });
    }
    return out;
  }

  // Reload the category_full screen after a filter change.
  //
  // Activity.replace(replace, clear) with clear = false overlays `replace` onto
  // the CURRENT activity and re-pushes it, so url/component/source/title and the
  // whole activity stack survive; page 1 restarts the grid. The filter itself is
  // already in Storage, so the rebuilt screen re-fetches and re-filters.
  //
  // (v2.6) ONLY THE CATALOGUE CACHE IS DROPPED. invalidate() wipes the registry,
  // /my, /new, /schedule AND /catalog — far more than a filter change needs, and
  // it forces a needless re-resolve of the user's subscriptions. A targeted drop
  // of every cache key that carries '/catalog' is enough: the next list() call
  // re-fetches the catalogue with the new filters while my/new/schedule stay warm.
  function panelReload(screen) {
    cacheInvalidateMatch('/catalog');
    try {
      if (screen && screen.activity && screen.activity.activity &&
          isFn(screen.activity.activity.loader)) {
        screen.activity.activity.loader(true);
      }
    } catch (e) {}
    try {
      if (Lampa.Activity && isFn(Lampa.Activity.replace)) {
        Lampa.Activity.replace({ page: 1 }, false);
        return;
      }
    } catch (e) {
      console.warn('lostfilm v2: activity replace fail', e);
    }
    // fallback for a build without Activity.replace: reopen the same screen
    try {
      if (!Lampa.Activity) return;
      var obj = {
        url: 'catalog:' + ((screen && screen.kind) ? screen.kind : 'serial'),
        page: 1, component: 'category_full', source: SOURCE, title: ''
      };
      Lampa.Activity.backward();
      setTimeout(function () {
        try { Lampa.Activity.push(obj); } catch (e2) {}
      }, 50);
    } catch (e) {}
  }

  // one row: "Сортировка: По рейтингу" — a plain Lampa .selector.
  // Layout uses INLINE STYLES only: this plugin never injects a style element.
  function filterRow(kind, def) {
    var row = $('<div class="selector"></div>');
    row.css({
      'box-sizing': 'border-box',
      'padding': '0.8em 1em',
      'margin': '0.35em 0',
      'border-radius': '0.4em',
      'background': 'rgba(255,255,255,.07)',
      'font-size': '1.1em',
      'white-space': 'nowrap',
      'overflow': 'hidden',
      'text-overflow': 'ellipsis'
    });
    row.append($('<div class="lostfilm-v2-filters__row"></div>')
      .text(def.title + ': ' + filterLabel(kind, def.field)));
    return row;
  }

  function filterRowLabel(row, kind, def) {
    try {
      row.find('.lostfilm-v2-filters__row')
        .text(def.title + ': ' + filterLabel(kind, def.field));
    } catch (e) {}
  }

  // (v2.6) the escape hatch from a filter that trapped the user: writing 'all'
  // (sort -> CAT_SORT) into every field cannot leave the panel in a state the
  // catalogue screen cannot recover from. Uses the SAME Storage keys as the
  // selects and the SAME row styling, so it behaves like any other panel row.
  function resetFilters(kind) {
    var i, def, defVal;
    for (i = 0; i < FILTER_FIELDS.length; i++) {
      def = FILTER_FIELDS[i];
      defVal = (def.field === 'sort') ? CAT_SORT : 'all';
      filterSet(kind, def.field, defVal);
    }

    // repaint every row label so the panel shows the defaults again (the rows
    // sit in FILTER_FIELDS order; panelReload rebuilds the screen anyway, this
    // only keeps the outgoing node consistent)
    try {
      if (panelScreen && panelScreen.panel) {
        var rows = panelScreen.panel.querySelectorAll('.lostfilm-v2-filters__row');
        for (i = 0; i < rows.length && i < FILTER_FIELDS.length; i++) {
          rows[i].textContent = FILTER_FIELDS[i].title + ': ' +
            filterLabel(kind, FILTER_FIELDS[i].field);
        }
      }
    } catch (e) {}

    panelReload(panelScreen);
    noty('LostFilm: фильтры сброшены');
  }

  // (v2.6) the fifth panel row: a plain button, same skin as the selects.
  function resetRow(kind) {
    var row = $('<div class="selector lostfilm-v2-filters__reset"></div>');
    row.css({
      'box-sizing': 'border-box',
      'padding': '0.8em 1em',
      'margin': '0.35em 0',
      'border-radius': '0.4em',
      'background': 'rgba(255,255,255,.07)',
      'font-size': '1.1em',
      'white-space': 'nowrap',
      'overflow': 'hidden',
      'text-overflow': 'ellipsis',
      'text-align': 'center'
    });
    row.text('Сбросить фильтры');
    row.on('hover:enter click', function (e) {
      if (e && isFn(e.preventDefault)) { try { e.preventDefault(); } catch (err) {} }
      resetFilters(kind);
    });
    return row;
  }

  function openFilterSelect(kind, field, def, row) {
    var items = filterOptions(kind, field);
    if (!items.length) return;

    var ctrl = 'content';
    try {
      if (Lampa.Controller && isFn(Lampa.Controller.enabled)) {
        var en = Lampa.Controller.enabled();
        if (en && en.name) ctrl = en.name;
      }
    } catch (e) {}

    Lampa.Select.show({
      title: def.title,
      items: items,
      onSelect: function (a) {
        if (!a) return;

        filterSet(kind, field, a.value);
        if (row) filterRowLabel(row, kind, def);

        // keep the option list known to Lampa (the same public Params.select the
        // old settings selects went through)
        if (metaField(field)) {
          try {
            var meta = catalogMetaGet(kind);
            if (meta) {
              catRegisterSelect(kind, field,
                field === 'year' ? meta.years : meta.genres,
                filterAllTitle(field), field === 'year');
            }
          } catch (e) {}
        }

        try {
          if (Lampa.Controller && isFn(Lampa.Controller.toggle)) {
            Lampa.Controller.toggle(ctrl);
          }
        } catch (e) {}

        panelReload(panelScreen);
      },
      onBack: function () {
        try {
          if (Lampa.Controller && isFn(Lampa.Controller.toggle)) {
            Lampa.Controller.toggle(ctrl);
          }
        } catch (e) {}
      }
    });
  }

  // build the panel and insert it BEFORE the activity's scroll container
  function buildFilterPanel(kind, root) {
    var narrow = window.innerWidth <= 580;

    var panel = $('<div class="' + FILTERS_CLASS + '"></div>');
    panel.css({
      'box-sizing': 'border-box',
      'padding': '1em 1em 0.4em',
      'display': 'flex',
      'flex-wrap': 'wrap',
      'align-items': 'center'
    });

    var head = $('<div>Фильтры</div>');
    head.css({
      'font-size': '1.2em', 'font-weight': '600', 'opacity': '0.65',
      'padding': '0 0.9em 0 0.2em'
    });
    panel.append(head);

    var i, def, row;
    var width = narrow ? '22%' : '23%';
    for (i = 0; i < FILTER_FIELDS.length; i++) {
      def = FILTER_FIELDS[i];
      row = filterRow(kind, def);
      row.css({
        'flex': '1 1 ' + (narrow ? '40%' : '16%'),
        'min-width': narrow ? '0' : '11em',
        'max-width': narrow ? 'none' : '26%'
      });
      (function (d, r) {
        r.on('hover:enter click', function (e) {
          if (e && isFn(e.preventDefault)) { try { e.preventDefault(); } catch (err) {} }
          openFilterSelect(kind, d.field, d, r);
        });
      })(def, row);
      panel.append(row);
    }

    // (v2.6) fifth row: the reset button. Kept visible at ALL times (not only
    // while a filter is active) — a user trapped behind a 0-result filter has no
    // other way out of the panel, and the extra row costs one line of height.
    var rrow = resetRow(kind);
    rrow.css({
      'flex': '1 1 ' + (narrow ? '40%' : '16%'),
      'min-width': narrow ? '0' : '11em',
      'max-width': narrow ? 'none' : '26%'
    });
    panel.append(rrow);

    root.insertBefore(panel[0], root.firstChild);
    return panel;
  }

  // keep the panel rows in the focus collection. The screen's controller does
  // Controller.collectionSet(scroll), which REPLACES the collection and drops a
  // sibling, so the rows are re-appended on every 'content' toggle of OUR screen.
  function panelReappend() {
    try {
      if (!panelScreen || !panelScreen.panel) return;
      if (!panelScreen.panel.isConnected) return;

      var activity = Lampa.Activity && isFn(Lampa.Activity.active)
        ? Lampa.Activity.active() : null;
      if (!activity || activity !== panelScreen.object) return;

      var comp = activity.activity && activity.activity.component;
      if (!comp || !Lampa.Controller || !isFn(Lampa.Controller.own)) return;
      if (!Lampa.Controller.own(comp)) return;

      var sel = panelScreen.panel.querySelectorAll('.selector');
      if (sel.length && isFn(Lampa.Controller.collectionAppend)) {
        Lampa.Controller.collectionAppend(sel);
      }
    } catch (e) {}
  }

  var controllerBound = false;

  function addControllerHandler() {
    if (controllerBound) return;
    if (!Lampa.Controller || !Lampa.Controller.listener ||
        !isFn(Lampa.Controller.listener.follow)) return;
    controllerBound = true;
    try {
      Lampa.Controller.listener.follow('toggle', function (e) {
        if (e && e.name === 'content') panelReappend();
      });
    } catch (e) {
      controllerBound = false;
    }
  }

  // activity events for OUR screen only: (component, source, url) must all match
  function onActivity(event) {
    try {
      if (!event || !event.object) return;
      var object = event.object;

      if (event.type === 'destroy') {
        if (panelScreen && panelScreen.object === object) panelScreen = null;
        return;
      }

      if (event.type !== 'start' && event.type !== 'archive') return;
      if (object.component !== 'category_full') return;
      if (object.source !== SOURCE) return;

      var kind = parseCatalogUrl(object.url);
      if (!kind) return;

      var root = null;
      try {
        if (object.activity && isFn(object.activity.render)) {
          var slide = object.activity.render(true);
          if (slide && slide.querySelector) {
            root = slide.querySelector('.activity__body > div');
          }
        }
      } catch (e) { root = null; }
      if (!root) return;

      if (root.querySelector('.' + FILTERS_CLASS)) {
        // already there (a re-start of an archived activity) — remember only
        panelScreen = {
          kind: kind, root: root,
          panel: root.querySelector('.' + FILTERS_CLASS),
          activity: object, object: object
        };
        panelReappend();
        return;
      }

      var panel = buildFilterPanel(kind, root);
      panelScreen = {
        kind: kind, root: root, panel: panel[0],
        activity: object, object: object
      };

      // let the core subtract the panel height from the scroll layer (see the
      // (N) note): scroll.minus is idempotent and only sets mheight
      try {
        var comp = object.activity && object.activity.component;
        if (comp && comp.scroll && isFn(comp.scroll.minus)) {
          comp.scroll.minus(panel[0]);
        }
        if (Lampa.Layer && isFn(Lampa.Layer.update)) Lampa.Layer.update();
      } catch (e) {}

      panelReappend();

      // warm the year/genre option lists for this kind (one fetch, 3 h cache).
      // (v2.6) no filters here either — this is a metadata warm-up, it must be
      // able to run even while a saved filter matches 0 items.
      if (!catalogMetaGranted || !catalogMetaGet(kind)) {
        catalogMetaGranted = true;
        catalogCards(kind, function () {}, false);
      }
    } catch (e) {
      console.warn('lostfilm v2: filter panel fail', e);
    }
  }

  var activityBound = false;

  function addActivityHandler() {
    if (activityBound) return;
    if (!Lampa.Listener || !isFn(Lampa.Listener.follow)) return;
    activityBound = true;
    try {
      Lampa.Listener.follow('activity', onActivity);
      addControllerHandler();
    } catch (e) {
      activityBound = false;
      console.warn('lostfilm v2: activity listen fail', e);
    }
  }

  // --------------------------------------- (D)(F) schedule by real days

  // static per-day lines; the server group label is matched through normGroup().
  // 5 lines + my + new = 7 parts on purpose: LINE_PARTS_LIMIT makes exactly this
  // many lines resolve in the very first partNext() batch, so every line renders
  // immediately. The "+2 day" line is the new one from v2.2.
  var SCHEDULE_LINES = [
    { id: 'today',    spec: 'today',    title: 'Сегодня' },
    { id: 'tomorrow', spec: 'tomorrow', title: 'Завтра' },
    { id: 'after',    spec: 'after',    title: 'Послезавтра' },
    { id: 'week',     spec: 'week',     title: 'На этой неделе' },
    { id: 'rest',     spec: 'rest',     title: 'Позже' }
  ];

  // server groups that are, by definition, later than the current week
  var SCHEDULE_LATER = ['на следующей неделе', 'позже'];

  function inList(list, value) {
    var i;
    for (i = 0; i < list.length; i++) { if (list[i] === value) return true; }
    return false;
  }

  // 'DD.MM.YYYY' -> Date at LOCAL midnight, or null when it does not parse.
  // All comparisons below stay in local time (no UTC shifts, no ISO parsing).
  function parseDay(value) {
    var m = /^(\d{1,2})\.(\d{1,2})\.(\d{4})$/.exec(trim(value));
    if (!m) return null;
    var d = parseInt(m[1], 10);
    var mo = parseInt(m[2], 10);
    var y = parseInt(m[3], 10);
    if (!(mo >= 1 && mo <= 12) || !(d >= 1 && d <= 31)) return null;
    var dt = new Date(y, mo - 1, d);
    // reject rolled-over values such as 31.02.2026
    if (dt.getFullYear() !== y || dt.getMonth() !== mo - 1 || dt.getDate() !== d) return null;
    return dt;
  }

  function dayStart(d) {
    return new Date(d.getFullYear(), d.getMonth(), d.getDate());
  }

  function todayStart() {
    return dayStart(new Date());
  }

  // whole days between two local midnights (DST-safe thanks to the rounding)
  function dayDiff(from, to) {
    return Math.round((to.getTime() - from.getTime()) / 86400000);
  }

  function dateTime(value) {
    var d = parseDay(value);
    return d ? d.getTime() : Infinity;
  }

  // (F) which line an item belongs to, decided by its real date
  function scheduleBucket(item, group) {
    var g = normGroup(group);

    // "на следующей неделе" / "позже" never get pulled into the current week
    if (inList(SCHEDULE_LATER, g)) return 'rest';

    var d = parseDay(item && item.date);
    if (d) {
      var diff = dayDiff(todayStart(), d);
      if (diff >= 0) {
        if (diff === 0) return 'today';
        if (diff === 1) return 'tomorrow';
        if (diff === 2) return 'after';
        if (diff <= 7) return 'week';
        return 'rest';
      }
    }

    // guard: unparseable (or already past) date -> keep the server grouping,
    // which is exactly the pre-v2.2 behaviour
    return groupFallback(g);
  }

  // server group -> line id, used only when the date cannot be trusted
  function groupFallback(g) {
    if (g === 'сегодня') return 'today';
    if (g === 'завтра') return 'tomorrow';
    if (g === 'на этой неделе') return 'week';
    return 'rest';
  }

  // stable ascending sort by date; unparseable dates go to the end
  function sortByDateAsc(list) {
    var wrapped = [], out = [], i;
    for (i = 0; i < list.length; i++) {
      wrapped.push({ it: list[i], i: i, t: dateTime(list[i] && list[i].date) });
    }
    wrapped.sort(function (a, b) {
      if (a.t === b.t) return a.i - b.i;
      if (a.t === Infinity) return 1;
      if (b.t === Infinity) return -1;
      return a.t - b.t;
    });
    for (i = 0; i < wrapped.length; i++) out.push(wrapped[i].it);
    return out;
  }

  // /schedule -> [{group, items}]
  function scheduleGroups(data) {
    var raw = (data && data.data) || [];
    var out = [], i, g;
    for (i = 0; i < raw.length; i++) {
      g = raw[i] || {};
      if (!g.items || !g.items.length) continue;
      out.push({ group: trim(g.group || ''), items: g.items });
    }
    return out;
  }

  // spec: 'today' | 'tomorrow' | 'after' | 'week' | 'rest'
  function scheduleItems(data, spec) {
    var groups = scheduleGroups(data);
    var out = [], i, j, g, items, it;
    for (i = 0; i < groups.length; i++) {
      g = groups[i];
      items = g.items || [];
      for (j = 0; j < items.length; j++) {
        if (scheduleBucket(items[j], g.group) !== spec) continue;
        it = copyObj(items[j]);
        it.group = g.group;
        out.push(it);
      }
    }
    if (spec === 'week') out = sortByDateAsc(out);
    return out;
  }

  function scheduleLineById(id) {
    var i;
    for (i = 0; i < SCHEDULE_LINES.length; i++) {
      if (SCHEDULE_LINES[i].id === id) return SCHEDULE_LINES[i];
    }
    return null;
  }

  // url of a schedule line: 'schedule:<lineId>' — parsed back in list()
  function scheduleUrl(id) {
    return 'schedule:' + id;
  }

  function parseScheduleUrl(url) {
    var m = /^schedule:([a-z]+)$/.exec(String(url || ''));
    if (!m) return null;
    return scheduleLineById(m[1]);
  }

  // --------------------------------------------------------------- lines

  // emptyEnvelope: for the "my" line when there is no session — the brief asks
  // for an empty envelope instead of a hard failure
  function makeLine(kind, title, limit, emptyEnvelope, spec, url) {
    return function (call) {
      var done = false;

      function finish(line) {
        if (done) return;
        done = true;
        call(line || false);
      }

      function empty() {
        finish({
          url: url || kind,
          title: title,
          page: 1,
          total_results: 0,
          total_pages: 1,
          more: false,
          results: [],
          source: SOURCE,
          small: false
        });
      }

      // build the line envelope from a card array
      function emit(cards) {
        if (!cards || !cards.length) {
          if (emptyEnvelope) { empty(); return; }
          finish(false);
          return;
        }
        if (kind === 'schedule') cards = cards.slice(0, LINE_LIMIT);

        // (J) 'new' is SERVER-paginated: /new answers one 15-episode page and
        // clamps page to 1..100. Emit that single page with the SERVER depth so
        // the core's More.onVisible draws "Ещё"; its click opens
        // category_full(url 'new', page 1) and list('new', 2, 3, ...) fetches
        // /new?...&page=N, i.e. the NEXT server page. The local card count must
        // NOT drive total_pages here: 15 cards would otherwise mean a single
        // page and the button would never appear.
        if (kind === 'new') {
          finish({
            url: url || kind,
            title: title,
            page: 1,
            total_results: NEW_TOTAL,
            total_pages: NEW_PAGES,
            more: true,
            results: cards.slice(0, NEW_PAGE_SIZE),
            source: SOURCE,
            small: false
          });
          return;
        }

        // (I) the line renders PAGE_SIZE cards at most and reports the FULL
        // count in total_results / total_pages, exactly like a category page.
        // The core's More.onVisible then draws "Ещё" whenever total_pages > 1 and
        // its click opens component 'category_full' with our url + page 1, which
        // list() serves page by page (PAGE_SIZE per screen).
        // 'catalog' keeps its wider head (CAT_LINE_LIMIT) but the same counters.
        var total = cards.length;
        var head = (kind === 'catalog') ? CAT_LINE_LIMIT : PAGE_SIZE;
        var shown = cards.slice(0, head);
        var pages = Math.max(1, Math.ceil(total / PAGE_SIZE));

        finish({
          url: url || kind,
          title: title,
          page: 1,
          total_results: total,
          total_pages: pages,
          // (I) the core More module draws "Ещё" from total_pages > 1; keep the
          // flag in sync for the (deprecated) InteractionLine reader as well
          more: pages > 1,
          results: shown,
          source: SOURCE,
          small: false
        });
      }

      function ok(data) {
        if (!data) { emit(null); return; }
        if (kind === 'schedule') {
          // per-day line: only the items of this day group
          emit(buildCards(scheduleItems(data, spec), 'schedule'));
          return;
        }
        emit(buildResults(kind, data));
      }

      if (kind === 'my') {
        apiMy(ok);
        return;
      }
      if (kind === 'schedule') {
        apiSchedule(ok);
        return;
      }
      if (kind === 'catalog') {
        // (H) line = the RAW catalogue head, NEVER filtered (v2.6): a saved
        // filter that matches 0 items must not be able to erase the line itself.
        catalogCards(spec, emit, false);
        return;
      }
      // (J) 'new' always fetches SERVER page 1 here (the same NEW_LIMIT URL the
      // main-screen row warms, so both share one cached response); the deeper
      // pages are served on demand by list().
      netGet(endpoint(kind, kind === 'new' ? NEW_LIMIT : limit), ok,
        function () { emit(null); });
    };
  }

  // (H)(I) the two catalogue lines carry a page counter so that the "Ещё" screen
  // (component 'category_full' -> our list()) pages through the SAME storage.
  // total_results / total_pages drive the core's own "more" button (More.onVisible
  // needs total_pages > 1) and it opens 'catalog:<kind>' with page 1.
  function makeCatalogLine(kind, title, spec) {
    return makeLine('catalog', title, CAT_LINE_LIMIT, false, spec, catalogUrl(kind));
  }

  // ----------------------------------------------------- serialId registry
  // registry: { [normalizeName(title_en||title)]: {key,serialId,follow,watched,episodes} }

  var registry = null;
  var registryLoading = false;
  var registryWaiters = [];

  function registryKey(titleEn, title) {
    return normalizeName(trim(titleEn) || trim(title));
  }

  function buildRegistry(list) {
    var map = {}, i, it, k;
    if (!list || !list.length) return map;
    for (i = 0; i < list.length; i++) {
      it = list[i] || {};
      k = registryKey(it.title_en, it.title);
      if (!k) continue;
      map[k] = {
        key: k,
        serialId: (it.id == null ? null : it.id),
        follow: !!it.follow,
        watched: num(it.watched),
        episodes: num(it.episodes)
      };
    }
    return map;
  }

  function lookupRegistry(map, titleEn, title) {
    var k = registryKey(titleEn, title);
    if (!k || !map) return null;
    return has(map, k) ? map[k] : null;
  }

  function loadRegistry(cb) {
    if (isFn(cb)) registryWaiters.push(cb);

    var cached = cacheGet('lostfilm_registry');
    if (cached) {
      registry = cached;
      flushRegistry();
      return;
    }

    if (registryLoading) return;
    registryLoading = true;

    apiMy(function (data) {
      registryLoading = false;
      var list = (data && data.data) || [];
      registry = buildRegistry(list);
      cacheSet('lostfilm_registry', registry);
      flushRegistry();
    });
  }

  function flushRegistry() {
    var w = registryWaiters;
    registryWaiters = [];
    var i;
    for (i = 0; i < w.length; i++) {
      if (isFn(w[i])) {
        try { w[i](registry); } catch (e) {}
      }
    }
  }

  function invalidate() {
    // drop the registry and every /my|/new|/schedule|/catalog response
    registry = null;
    registryLoading = false;
    cacheInvalidate('lostfilm_registry');
    cacheInvalidate(BASE + API_BASE);
    cacheInvalidate(API_BASE);
  }

  // ------------------------------------------------------------ tmdb resolve

  function tmdbSearchUrl(query, lang) {
    return Lampa.TMDB.api('search/multi?api_key=' + Lampa.TMDB.key() +
      '&query=' + encodeURIComponent(query) +
      '&language=' + encodeURIComponent(lang || 'ru'));
  }

  function resultMediaType(r) {
    if (!r) return '';
    if (r.media_type === 'tv' || r.media_type === 'movie' || r.media_type === 'person') return r.media_type;
    if (r.first_air_date || r.name || r.original_name) return 'tv';
    if (r.release_date || r.title) return 'movie';
    return '';
  }

  function pickTmdbResult(results, original, title, mediaType) {
    if (!results || !results.length) return null;

    var want = normalizeName(original);
    var fallback = normalizeName(title);
    var pool = [], i, r;

    for (i = 0; i < results.length; i++) {
      r = results[i];
      if (!r) continue;
      var rmt = resultMediaType(r);
      if (rmt === 'person') continue;
      if (mediaType && rmt && rmt !== mediaType) continue;
      pool.push(r);
    }

    if (!pool.length) {
      for (i = 0; i < results.length; i++) {
        r = results[i];
        if (r && resultMediaType(r) !== 'person') pool.push(r);
      }
    }

    for (i = 0; i < pool.length; i++) {
      r = pool[i];
      if (want && (normalizeName(r.original_name) === want ||
                   normalizeName(r.original_title) === want ||
                   normalizeName(r.name) === want ||
                   normalizeName(r.title) === want)) return r;
    }

    for (i = 0; i < pool.length; i++) {
      r = pool[i];
      if (fallback && (normalizeName(r.name) === fallback ||
                       normalizeName(r.title) === fallback)) return r;
    }

    return null;
  }

  // copy lostfilm_* payload from our card onto the resolved TMDB movie
  function enrichMovie(movie, card, params) {
    if (!movie) return;

    var titleEn = card.original_title || card.original_name || card.title || '';
    var rec = lookupRegistry(registry, titleEn, card.title || card.name || '');

    var serialId = (card.lostfilm_id == null ? null : card.lostfilm_id);
    if (serialId == null && rec) serialId = rec.serialId;

    if (serialId != null) movie.lostfilm_id = serialId;
    movie.lostfilm_follow = rec ? !!rec.follow : !!card.lostfilm_follow;
    movie.lostfilm_watched = rec ? num(rec.watched) : num(card.lostfilm_watched);
    movie.lostfilm_episodes = rec ? num(rec.episodes) : num(card.lostfilm_episodes);
    movie.lostfilm_episode_id = (card.lostfilm_episode_id == null ? null : card.lostfilm_episode_id);
    movie.lostfilm_link = card.lostfilm_link || '';
    movie.lostfilm_title_en = titleEn;
    movie.lostfilm_source = true;

    // make sure our own source name survives the tmdb round-trip (guard on the full screen)
    if (!movie.source) movie.source = SOURCE;

    return movie;
  }

  // ------------------------------------------------------------ follow/watched

  function makeButton(extraClass, label) {
    var btn = $('<div class="full-start__button selector lostfilm-v2-btn ' + extraClass + '"></div>');
    btn.append($('<span></span>').text(label));
    return btn;
  }

  function setLabel(btn, label) {
    btn.find('span').text(label);
  }

  function bindEnter(el, fn) {
    var lock = false;
    function run(e) {
      if (e && isFn(e.preventDefault)) { try { e.preventDefault(); } catch (err) {} }
      if (lock) return;
      lock = true;
      setTimeout(function () { lock = false; }, 400);
      try { fn(); } catch (err) { console.warn('lostfilm v2 button fail', err); }
    }
    el.on('hover:enter', run);
    el.on('click', run);
  }

  function toggleFollow(sid, btn, current, setState) {
    var prev = current;
    setState(!prev);
    setLabel(btn, !prev ? 'В избранном' : 'В избранное');

    netRaw(API_BASE + '/follow/' + encodeURIComponent(sid), function (res) {
      if (res && res.ok) {
        var on = res.result === 'on' ? true : (res.result === 'off' ? false : !prev);
        setState(on);
        setLabel(btn, on ? 'В избранном' : 'В избранное');
        invalidate();
        noty('LostFilm: ' + (on ? 'добавлено в избранное' : 'убрано из избранного'));
      } else {
        setState(prev);
        setLabel(btn, prev ? 'В избранном' : 'В избранное');
        noty('LostFilm: не удалось изменить подписку');
      }
    }, function () {
      setState(prev);
      setLabel(btn, prev ? 'В избранном' : 'В избранное');
      noty('LostFilm: ошибка сети');
    });
  }

  function markWatched(episodeId, btn) {
    netRaw(API_BASE + '/watched?episodeID=' + encodeURIComponent(episodeId) + '&mode=on', function (res) {
      if (res && res.ok) {
        invalidate();
        noty('LostFilm: эпизод отмечен просмотренным');
      } else {
        noty('LostFilm: не удалось отметить эпизод');
      }
    }, function () {
      noty('LostFilm: ошибка сети');
    });
  }

  // hard guard: touch ONLY our own cards on the full screen
  function isOurMovie(movie) {
    if (!movie) return false;
    if (movie.source === SOURCE) return true;
    if (movie.lostfilm_id != null && movie.lostfilm_id !== '') return true;
    if (movie.lostfilm_source === true) return true;
    return false;
  }

  function onFull(event) {
    try {
      if (!event || event.type !== 'complite') return;

      var data = event.data;
      var movie = data && data.movie;
      if (!isOurMovie(movie)) return;

      var root = null;
      var activity = event.object && event.object.activity;
      if (activity && isFn(activity.render)) {
        try { root = activity.render(); } catch (e) { root = null; }
      }
      if (!root || !root.find) root = (event.body && event.body.find) ? event.body : null;
      if (!root || !root.find) return;

      var cont = root.find('.full-start-new__buttons');
      if (!cont.length) cont = root.find('.full-start__buttons');
      if (!cont.length) return;
      if (cont.find('.lostfilm-v2-btn').length) return;

      var titleEn = movie.lostfilm_title_en || movie.original_title || movie.original_name || '';
      var rec = lookupRegistry(registry, titleEn, movie.title || movie.name || '');

      var serialId = (movie.lostfilm_id == null ? null : movie.lostfilm_id);
      if (serialId == null && rec) serialId = rec.serialId;

      var followState = rec ? !!rec.follow : !!movie.lostfilm_follow;
      var episodeId = movie.lostfilm_episode_id;

      var added = [];

      if (serialId != null && String(serialId) !== '') {
        var fBtn = makeButton('lostfilm-v2-follow', followState ? 'В избранном' : 'В избранное');
        bindEnter(fBtn, function () {
          toggleFollow(serialId, fBtn, followState, function (v) { followState = v; });
        });
        cont.append(fBtn);
        added.push(fBtn);
      }

      if (episodeId != null && String(episodeId) !== '') {
        var wBtn = makeButton('lostfilm-v2-watched', 'Отметить просмотренным');
        bindEnter(wBtn, function () { markWatched(episodeId, wBtn); });
        cont.append(wBtn);
        added.push(wBtn);
      }

      if (!added.length) return;

      try {
        if (Lampa.Controller && isFn(Lampa.Controller.collectionAppend)) {
          var i;
          for (i = 0; i < added.length; i++) Lampa.Controller.collectionAppend(added[i]);
        }
      } catch (e) {}
    } catch (e) {
      console.warn('lostfilm v2 full handler fail', e);
    }
  }

  // ---------------------------------------------------------- bind modal
  // Mechanic transferred from v1: hidden token parse -> captcha tied to the
  // PHP session -> urlencoded POST -> 'msg ok' / 'msg err'.

  var modal_node = null;
  var modal_prev = '';
  var modal_token = { phpssid: '', r: '' };

  function captchaSrc() {
    var r = modal_token.r || String(Math.floor(Math.random() * 900000) + 100000);
    modal_token.r = r;
    var url = BIND_URL + '/captcha?r=' + encodeURIComponent(r);
    if (modal_token.phpssid) url += '&phpssid=' + encodeURIComponent(modal_token.phpssid);
    return url + '&_=' + Date.now();
  }

  function neutralizeController() {
    try {
      if (Lampa.Controller && isFn(Lampa.Controller.add)) {
        // no remove() in this Lampa build — re-register as inert
        Lampa.Controller.add(CONTROLLER, {
          toggle: function () {},
          back: function () {}
        });
      }
    } catch (e) {}
  }

  function closeBindModal() {
    if (!modal_node) return;
    var node = modal_node;
    modal_node = null;
    try { node.remove(); } catch (e) {}
    try {
      if (Lampa.Modal && isFn(Lampa.Modal.close)) Lampa.Modal.close();
    } catch (e) {}
    neutralizeController();
    try {
      if (Lampa.Controller && isFn(Lampa.Controller.toggle)) {
        Lampa.Controller.toggle(modal_prev || 'content');
      }
    } catch (e) {}
  }

  function bindModalBody() {
    return $(
      '<div class="lostfilm-bind">' +
      '<div data-lf-msg style="display:none;margin-bottom:12px;padding:10px 12px;' +
      'border-radius:9px;font-size:14px;"></div>' +
      '<div style="font-size:13px;opacity:.65;margin:6px 0 6px;">E-mail</div>' +
      '<input data-lf-mail type="text" autocomplete="username" ' +
      'style="width:100%;box-sizing:border-box;padding:11px 12px;border-radius:9px;' +
      'border:1px solid #343943;background:#12141a;color:#e8eaed;font-size:15px;">' +
      '<div style="font-size:13px;opacity:.65;margin:12px 0 6px;">Пароль</div>' +
      '<input data-lf-pass type="password" autocomplete="current-password" ' +
      'style="width:100%;box-sizing:border-box;padding:11px 12px;border-radius:9px;' +
      'border:1px solid #343943;background:#12141a;color:#e8eaed;font-size:15px;">' +
      '<div style="font-size:13px;opacity:.65;margin:12px 0 6px;">Код с картинки</div>' +
      '<div style="display:flex;gap:10px;align-items:center;">' +
      '<input data-lf-cap type="text" autocomplete="off" ' +
      'style="flex:1;min-width:0;box-sizing:border-box;padding:11px 12px;border-radius:9px;' +
      'border:1px solid #343943;background:#12141a;color:#e8eaed;font-size:15px;">' +
      '<img data-lf-img class="selector" alt="captcha" ' +
      'style="height:52px;border-radius:8px;background:#fff;cursor:pointer;">' +
      '</div>' +
      '<div style="display:flex;gap:10px;margin-top:18px;">' +
      '<div class="selector" data-lf-submit style="flex:1;text-align:center;padding:12px;' +
      'border-radius:9px;background:#4c8dff;color:#fff;font-size:15px;font-weight:600;' +
      'cursor:pointer;">Войти</div>' +
      '<div class="selector" data-lf-cancel style="padding:12px 16px;border-radius:9px;' +
      'background:#2b2b2b;color:#e8eaed;font-size:15px;cursor:pointer;">Отмена</div>' +
      '</div>' +
      '<div style="font-size:12px;opacity:.55;margin-top:12px;">' +
      'Капча привязана к сессии сайта. Нажмите на картинку, чтобы обновить код.' +
      '</div>' +
      '</div>'
    );
  }

  function openBindModal() {
    if (modal_node) return;
    if (typeof $ === 'undefined' || !$) return;

    if (!modal_prev) {
      try {
        if (Lampa.Controller && isFn(Lampa.Controller.enabled)) {
          var cur = Lampa.Controller.enabled();
          if (cur && cur.name) modal_prev = cur.name;
        }
      } catch (e) {}
    }
    if (!modal_prev) modal_prev = 'content';

    var html = bindModalBody();
    var mail = html.find('[data-lf-mail]');
    var pass = html.find('[data-lf-pass]');
    var cap = html.find('[data-lf-cap]');
    var img = html.find('[data-lf-img]');
    var msg = html.find('[data-lf-msg]');
    var submit = html.find('[data-lf-submit]');

    modal_token = { phpssid: '', r: '' };

    function showMsg(text, ok) {
      msg.text(text).show().css({
        background: ok ? '#16301f' : '#3a1f22',
        border: '1px solid ' + (ok ? '#2b6b41' : '#6b2b31'),
        color: ok ? '#a8f0c0' : '#ffb4b4'
      });
    }

    function reloadCaptcha() {
      img.attr('src', captchaSrc());
    }

    // 1) GET the form -> hidden phpssid + r (same regexps as v1)
    function loadForm(cb) {
      bindGet(BIND_URL, function (body) {
        if (body) {
          var m1 = body.match(/name=['"]phpssid['"]\s+value=['"]([^'"]+)['"]/i);
          var m2 = body.match(/name=['"]r['"]\s+value=['"]([^'"]+)['"]/i);
          if (m1) modal_token.phpssid = m1[1];
          if (m2) modal_token.r = m2[1];
        }
        if (isFn(cb)) cb(!!body);
      });
    }

    // 4) POST urlencoded -> 'msg ok' | 'msg err'
    function doBind() {
      var m = trim(mail.val() || '');
      var p = String(pass.val() || '');
      var c = trim(cap.val() || '');

      if (!m || !p) { showMsg('Введите e-mail и пароль', false); return; }
      if (!c) { showMsg('Введите код с картинки', false); return; }

      submit.text('...');
      cap.val('');

      var body = 'phpssid=' + encodeURIComponent(modal_token.phpssid) +
        '&r=' + encodeURIComponent(modal_token.r) +
        '&mail=' + encodeURIComponent(m) +
        '&pass=' + encodeURIComponent(p) +
        '&captcha=' + encodeURIComponent(c);

      bindPost(BIND_URL, body, function (res) {
        submit.text('Войти');

        if (!res) {
          showMsg('Сайт недоступен, повторите позже', false);
          reloadCaptcha();
          return;
        }

        if (/msg\s+ok/i.test(res) || /Готово/.test(res)) {
          showMsg('Готово', true);
          invalidate();
          // (G) take ownership of the freshly created session: /status is the
          // only source of the full lf_session, so ask for it right away and
          // store it (refreshStatus -> apiStatus does both). The bind is closed
          // after 900 ms, when the request has normally landed.
          refreshStatus();
          sessionClaim(function () {
            noty('LostFilm: сессия обновлена');
          });
          setTimeout(function () { closeBindModal(); }, 900);
          return;
        }

        var em = res.match(/msg\s+err['"]>([^<]*)</i);
        showMsg(em && em[1] ? em[1] : 'Не удалось войти', false);
        reloadCaptcha(); // same r + phpssid -> new captcha for the same session
      });
    }

    img.on('click hover:enter', function (e) {
      if (e && isFn(e.preventDefault)) { try { e.preventDefault(); } catch (err) {} }
      reloadCaptcha();
    });

    submit.on('click hover:enter', function (e) {
      if (e && isFn(e.preventDefault)) { try { e.preventDefault(); } catch (err) {} }
      doBind();
    });

    html.find('[data-lf-cancel]').on('click hover:enter', function (e) {
      if (e && isFn(e.preventDefault)) { try { e.preventDefault(); } catch (err) {} }
      closeBindModal();
    });

    html.on('keydown', function (e) {
      if (e && e.keyCode === 13) {
        try { e.preventDefault(); } catch (err) {}
        doBind();
      }
    });

    modal_node = html;

    var modalApi = null;
    try { modalApi = Lampa.Modal; } catch (e) { modalApi = null; }

    // unique controller name for this modal
    try {
      if (Lampa.Controller && isFn(Lampa.Controller.add)) {
        Lampa.Controller.add(CONTROLLER, {
          toggle: function () {},
          back: function () { closeBindModal(); }
        });
        Lampa.Controller.toggle(CONTROLLER);
        Lampa.Controller.collectionSet(html);
        Lampa.Controller.collectionFocus(submit, html, true);
      }
    } catch (e) {}

    if (modalApi && isFn(modalApi.open)) {
      try {
        modalApi.open({
          title: 'LostFilm — вход',
          html: html,
          size: 'medium',
          onBack: function () { closeBindModal(); }
        });
      } catch (e) {
        console.warn('lostfilm v2 modal fail', e);
        try { $('body').append(html); } catch (err) {}
      }
    } else {
      // fallback: own overlay + our controller (v1 pattern)
      try {
        html.css({
          position: 'fixed', left: '0', top: '0', right: '0', bottom: '0',
          zIndex: '200', background: 'rgba(0,0,0,.72)', overflow: 'auto',
          display: 'flex', alignItems: 'center', justifyContent: 'center'
        });
        html.find('.lostfilm-bind').css({
          width: 'min(92vw,420px)', background: '#1d2026', border: '1px solid #2c3038',
          borderRadius: '14px', padding: '22px', color: '#e8eaed', boxSizing: 'border-box'
        });
        $('body').append(html);
      } catch (e) {}
    }

    loadForm(function () { reloadCaptcha(); });
  }

  // -------------------------------------------------------------- settings

  // Open a settings component, optionally making its BACK action return to
  // another component. app.min.js was checked for BOTH known mechanics:
  //
  //   Settings.create(name, params)  ->  function create$b(name) {
  //        var params = arguments.length > 1 && ... ? arguments[1] : {};
  //        var comp = new Component$2(name, params);
  //        body$1.empty().append(comp.render());
  //        listener$i.send('open', {name: name, body: comp.render(), params: params});
  //        last$2 = name; Controller.toggle('settings_component'); ...
  //
  //   Component$2's back  ->  back: function back() {
  //        scrl.destroy();
  //        comp.remove();
  //        Params.listener.remove('update_scroll', updateScroll);
  //        if (component_params.onBack) component_params.onBack();
  //        else Controller.toggle('settings');
  //      }
  //
  //   Controller.enabled()  ->  function enabled() { return {name: active_name,
  //                                                        controller: active$2}; }
  //
  // logo.js overrides the live controller instead:
  //   Lampa.Settings.create(LOGO_COMPONENT);
  //   Lampa.Controller.enabled().controller.back = function () { ... };
  // That works, but it REPLACES the closure that runs scrl.destroy()/comp.remove()
  // and unsubscribes Params, so every visit leaks one scroll listener. create$b
  // takes params, so the clean route is params.onBack: the core still destroys
  // the old screen and only then hands control to us. The controller.back
  // override is therefore NOT used — same entry points, no leak.
  function openSettingsComponent(name, backName) {
    try {
      if (!Lampa.Settings || !isFn(Lampa.Settings.create)) return;

      var params = {};
      if (backName) {
        params.onBack = function () { openSettingsComponent(backName, null); };
      }
      Lampa.Settings.create(name, params);
    } catch (e) {
      console.warn('lostfilm v2: settings create fail', e);
    }
  }

  function addSettings() {
    try {
      if (!Lampa.SettingsApi ||
          !isFn(Lampa.SettingsApi.addComponent) ||
          !isFn(Lampa.SettingsApi.addParam)) {
        console.warn('lostfilm v2: SettingsApi недоступен — секция настроек пропущена');
        return;
      }

      // (B) icon supported by addComponent({component, icon, name}) — the core
      // renders `aded.icon` inside `.settings-folder__icon` (see app.min.js,
      // settings folder render). Core folder icons are hardcoded white, so the
      // same colour is used to stay consistent (glass style inverts it on focus).
      Lampa.SettingsApi.addComponent({
        component: COMPONENT,
        name: 'LostFilm',
        icon: lfIcon('white')
      });

      // 1) status (type static — Lampa does not render a value for it)
      Lampa.SettingsApi.addParam({
        component: COMPONENT,
        param: { name: 'lf_status_v2', type: 'static' },
        field: { name: 'Статус', description: '...' },
        onRender: function (item) {
          try {
            item.addClass('lf-status-row');
            status_nodes.push(item);
            renderStatusNodes();
            refreshStatus();
          } catch (e) {}
        }
      });

      // 2) bind / refresh session (dispatched via Params.listener 'button')
      Lampa.SettingsApi.addParam({
        component: COMPONENT,
        param: { name: 'lf_bind_v2', type: 'button' },
        field: { name: 'Войти / Обновить сессию' }
      });

      // 3) reset cache
      Lampa.SettingsApi.addParam({
        component: COMPONENT,
        param: { name: 'lf_clearcache_v2', type: 'button' },
        field: { name: 'Сбросить кеш' }
      });

      // 3b) (M) TMDB posters trigger. Lampa SettingsApi type 'trigger' renders a
      //     toggle and stores the STRING 'true'/'false' under this name; default
      //     true. Storage.field() is read by tmdbPostersEnabled().
      Lampa.SettingsApi.addParam({
        component: COMPONENT,
        param: { name: LS_TMDB_POSTERS, type: 'trigger', 'default': true },
        field: {
          name: 'Постеры из TMDB',
          description: 'Нужно время на первый резолв: пока очередь подбирает ' +
            'постеры, видны постеры LostFilm. Дальше подставляются мгновенно.'
        },
        onChange: function () { invalidate(); }
      });

      // 4) (N) NO FILTER ROWS HERE ANY MORE. The catalogue filters live on the
      //    category_full screen itself (see the (N) header note); the LostFilm
      //    folder keeps only status / bind / clear-cache / TMDB posters.

      // (N) the LostFilm folder is the ONLY settings screen this plugin owns now,
      // so the hook has exactly one job left. The child-screen branches, the
      // helper machinery and the child-screen hiding went away with the
      // components themselves.
      try {
        if (Lampa.Settings && Lampa.Settings.listener &&
            isFn(Lampa.Settings.listener.follow)) {
          Lampa.Settings.listener.follow('open', function (e) {
            if (!e || e.name !== COMPONENT) return;

            if (e.body) {
              status_nodes = [];
              var node = $(e.body).find('.lf-status-row');
              if (node.length) status_nodes.push(node);
            }

            renderStatusNodes();
            refreshStatus();
          });
        }
      } catch (e) {}

      // fallback for builds that dispatch button params through Params.listener
      try {
        if (Lampa.Params && Lampa.Params.listener &&
            isFn(Lampa.Params.listener.follow)) {
          Lampa.Params.listener.follow('button', function (e) {
            if (!e) return;
            if (e.name === 'lf_bind_v2') openBindModal();
            if (e.name === 'lf_clearcache_v2') {
              try { api.clear(); } catch (err) {}
              noty('LostFilm: кеш очищен');
            }
          });
        }
      } catch (e) {}

    } catch (e) {
      console.warn('lostfilm v2: addSettings fail', e);
    }
  }

  // ---------------------------------------------------------------- api

  var api = {
    url: BASE + API_BASE,
    name: 'LostFilm',

    // category lines: my / new + two full-catalogue lines + one per schedule day.
    // partNext() calls every part in parallel; netGet() caches by URL, so the
    // 5 schedule lines share a single real /schedule request and both catalogue
    // lines share a single /catalog request.
    category: function (params, onsuccess, onerror) {
      if (!Lampa.Api || !isFn(Lampa.Api.partNext)) {
        if (onerror) onerror();
        return;
      }

      var parts = [
        makeLine('my', 'Мои подписки', LINE_LIMIT, true),
        // (I) "Новинки" -> "Новые релизы": one term for both the category line
        // and the main-screen row. emptyEnvelope=true: when /new answers an
        // empty/errored payload the line still renders (empty envelope) instead
        // of vanishing through finish(false) — that was the reported
        // "нет новых релизов" symptom.
        makeLine('new', 'Новые релизы', LINE_LIMIT, true),
        makeCatalogLine('serial', 'Все сериалы', 'serial'),
        makeCatalogLine('movies', 'Все фильмы', 'movies')
      ];

      var i, def;
      for (i = 0; i < SCHEDULE_LINES.length; i++) {
        def = SCHEDULE_LINES[i];
        parts.push(makeLine('schedule', def.title, LINE_LIMIT, false, def.spec,
          scheduleUrl(def.id)));
      }

      // keep every part inside the first batch (partNext slices to parts_limit)
      var limit = Math.max(LINE_PARTS_LIMIT, parts.length);

      Lampa.Api.partNext(parts, limit, function (data) {
        if (onsuccess) onsuccess(data);
      }, function () {
        if (onerror) onerror();
      });
    },

    // params.url = 'my' | 'new' | 'schedule:<lineId>' | 'catalog:<serial|movies>',
    // params.page. Catalogue screens page 20 at a time client-side over the
    // shared (server-cached 12 h) full-catalogue response.
    list: function (params, onComplite, onError) {
      params = params || {};

      var url = params.url || 'my';
      var schedLine = parseScheduleUrl(url);
      var catKind = parseCatalogUrl(url);
      var kind = schedLine ? 'schedule' : (catKind ? 'catalog' : url);
      var page = parseInt(params.page, 10);
      if (!(page > 0)) page = 1;

      // no session -> empty envelope, never an error screen
      if (kind === 'my' && authState === false) {
        if (onComplite) {
          onComplite({ page: page, results: [], total_pages: 1, total_results: 0 });
        }
        return;
      }

      function fail() {
        if (kind === 'my' || kind === 'schedule' || kind === 'catalog') {
          if (onComplite) {
            onComplite({ page: page, results: [], total_pages: 1, total_results: 0 });
          }
          return;
        }
        if (onError) onError();
        else if (onComplite) onComplite({ page: page, results: [], total_pages: 0, total_results: 0 });
      }

      function emitResults(results) {
        var total = results.length;
        var start = (page - 1) * PAGE_SIZE;
        var sliced = results.slice(start, start + PAGE_SIZE);
        var pages = Math.max(1, Math.ceil(total / PAGE_SIZE));

        // (J) 'new' is SERVER-paginated end to end: the payload already IS the
        // requested server page (15 episodes). Never slice it client-side and
        // never recompute depth from the local length — the envelope reports the
        // server clamp (1..100) so Items.onLoadNext can keep doing page++ until
        // the server stops answering. A 15-item page is fine for the core, it
        // simply renders what it got.
        if (kind === 'new') {
          if (onComplite) {
            onComplite({
              page: page,
              results: results,
              total_pages: NEW_PAGES,
              total_results: NEW_TOTAL
            });
          }
          return;
        }

        if (onComplite) {
          onComplite({
            page: page,
            results: sliced,
            total_pages: pages,
            total_results: total
          });
        }
      }

      function ok(data) {
        if (!data) { fail(); return; }

        var results;
        if (kind === 'schedule') {
          results = buildCards(scheduleItems(data, schedLine.spec), 'schedule');
        } else {
          results = buildResults(kind, data);
        }

        emitResults(results);
      }

      if (kind === 'my') { apiMy(ok); return; }
      if (kind === 'schedule') { apiSchedule(ok); return; }
      if (kind === 'catalog') {
        // (v2.6) the paginated catalogue screen is the ONE place filters apply.
        catalogCards(catKind, function (cards) {
          if (!cards || !cards.length) {
            // A saved filter can match nothing. Fail with an EMPTY envelope (the
            // core renders a "no results" screen instead of an error) and tell the
            // user WHY, pointing at the panel's reset row above the grid.
            fail();
            noty('LostFilm: фильтр дал 0 результатов — сбросьте фильтры на панели');
            return;
          }
          emitResults(cards);
        }, true);
        return;
      }
      // (J) 'new': the core's page goes STRAIGHT to the server. A bigger fetch
      // size (NEW_LIMIT=60) is pointless for the payload size — the server still
      // answers 15 — but it keeps one URL shape for line/row/list.
      netGet(endpoint(kind, kind === 'new' ? NEW_LIMIT : LIST_LIMIT, page), ok, fail);
    },

    // resolve our card to a TMDB id and delegate all heavy lifting to tmdb.full
    full: function (params, onSuccess, onError) {
      params = params || {};
      var card = params.card || {};

      var failed = false;
      var tmdbReady = false;
      var regReady = false;
      var tmdbData = null;

      function fail() {
        if (failed) return;
        failed = true;
        if (onError) onError();
      }

      function finish() {
        if (failed) return;
        if (!tmdbReady || !regReady) return;
        if (!tmdbData || !tmdbData.movie) { fail(); return; }
        enrichMovie(tmdbData.movie, card, params);
        if (onSuccess) onSuccess(tmdbData);
      }

      // registry loads in parallel — never blocks the error path
      loadRegistry(function () { regReady = true; finish(); });

      var title = card.title || params.title || '';
      var original = card.original_title || card.original_name || params.original_title || '';
      var query = trim(original) || trim(title);
      if (!query) { fail(); return; }

      var mediaType = '';
      if (card.media_type === 'tv' || card.media_type === 'movie') mediaType = card.media_type;
      else if (params.media_type === 'tv' || params.media_type === 'movie') mediaType = params.media_type;
      else if (params.serial || params.method === 'tv') mediaType = 'tv';
      else if (params.method === 'movie') mediaType = 'movie';

      var tmdbSource = null;
      try {
        if (Lampa.Api && Lampa.Api.sources) tmdbSource = Lampa.Api.sources.tmdb;
      } catch (e) { tmdbSource = null; }

      if (!tmdbSource || !isFn(tmdbSource.full)) { fail(); return; }

      var lang = 'ru';
      try { lang = Lampa.Storage.get('language') || 'ru'; } catch (e) {}

      var url = '';
      try {
        if (!Lampa.TMDB || !isFn(Lampa.TMDB.api) || !isFn(Lampa.TMDB.key)) { fail(); return; }
        url = tmdbSearchUrl(query, lang);
      } catch (e) { fail(); return; }

      netRaw(url, function (json) {
        var found = pickTmdbResult(json && json.results, original, title, mediaType);

        if (!found) {
          // fuzzy fallback: first usable hit
          var list = (json && json.results) || [];
          var i, r;
          for (i = 0; i < list.length; i++) {
            r = list[i];
            if (r && resultMediaType(r) !== 'person') { found = r; break; }
          }
          if (found) console.log('lostfilm: tmdb fuzzy match');
        }

        if (!found) { fail(); return; }

        var method = resultMediaType(found);
        if (method !== 'tv' && method !== 'movie') method = mediaType || 'movie';

        try {
          tmdbSource.full({ id: found.id, method: method, card: card }, function (data) {
            if (failed) return;
            tmdbData = data;
            tmdbReady = true;
            finish();
          }, function () {
            fail();
          });
        } catch (e) {
          fail();
        }
      }, function () {
        fail();
      });
    },

    clear: function () {
      try { if (net) net.clear(); } catch (e) {}
      cache = {};
      registry = null;
      registryLoading = false;
      registryWaiters = [];
      catalogMeta = { serial: null, movies: null };
      posterBadReset();
      catalogMetaGranted = false;   // (N) re-learn the year/genre options
      // (M) "Сбросить кеш" clears the TMDB poster map as well; the single queue
      // timer is left to drain (its queue is emptied, so the next step is a no-op).
      tmdbMapDrop();
    }
  };

  // ---------------------------------------------------------- menu item

  function register() {
    if (!Lampa.Api || !Lampa.Api.sources) return;
    if (Lampa.Api.sources[SOURCE]) return;
    Lampa.Api.sources[SOURCE] = api;
  }

  function openLostfilm() {
    try {
      Lampa.Activity.push({
        component: 'category',
        source: SOURCE,
        page: 1,
        title: 'LostFilm'
      });
    } catch (e) {
      console.warn('lostfilm v2 open fail', e);
    }
  }

  // (B) the menu item follows the core markup exactly:
  //     <li class="menu__item selector"><div class="menu__ico">SVG</div><div class="menu__text">…</div></li>
  // lnum uses the same shape; the core binds its own hover handlers to .selector.
  function buildMenuItem() {
    var item = $(
      '<li class="menu__item selector" data-action="lostfilm" data-lostfilm-menu="1">' +
      '<div class="menu__ico">' + lfIcon('currentColor') + '</div>' +
      '<div class="menu__text">LostFilm</div>' +
      '</li>'
    );
    item.on('hover:enter click', function (e) {
      if (e && isFn(e.preventDefault)) { try { e.preventDefault(); } catch (err) {} }
      openLostfilm();
    });
    return item;
  }

  function addMenu() {
    try {
      if (typeof $ === 'undefined' || !$) return;
      if (!Lampa.Api || !Lampa.Api.sources || !Lampa.Api.sources[SOURCE]) return;
      if ($('[data-lostfilm-menu]').length) return;

      var item = buildMenuItem();

      var root = $('.menu .menu__list').eq(0);
      if (root.length) { root.append(item); return; }

      // menu markup not in the DOM yet: use the public API, it appends to the
      // very same list (html$m.find('.menu__list:eq(0)')).
      // action is omitted on purpose — the item already carries its own
      // hover:enter/click handler, passing openLostfilm here would double-fire.
      if (Lampa.Menu && isFn(Lampa.Menu.addElement)) {
        Lampa.Menu.addElement(item);
      }
    } catch (e) {
      console.warn('lostfilm v2 menu fail', e);
    }
  }

  // -------------------------------------------- (C) main screen content row
  //
  // Registered through the PUBLIC Lampa.ContentRows.add — no monkey-patching of
  // the core "call" method (that hook was the main hazard in v1).
  //
  // Signature verified in app.min.js:
  //   function add$b(row) {
  //     if (_typeof(row)=='object' && typeof row.call=='function') { rows.push(row); ...
  //   function call$1(screen, params, calls) {
  //     ... row.call(params, screen) -> function|array|object, inserted with
  //         Arrays.insert(calls, (row.index||0)+i, callback)
  //
  // The core calls row.call(params, screen) EAGERLY on every main build and
  // expects the RETURNED function to be a loader (cb) => cb(json). The loader must
  // invoke cb EXACTLY ONCE, otherwise the Progress() gate in partNext (which
  // completes only when every task called back) would hang the whole main screen.

  var ROW_NAME = 'lostfilm_new_row';

  function makeRowProducer() {
    var done = false;
    var timer = null;

    return function (cb) {
      function finish(payload) {
        if (done) return;
        done = true;
        if (timer) { try { clearTimeout(timer); } catch (e) {} timer = null; }
        try { cb(payload); } catch (e) {}
      }

      if (!isFn(cb)) return;

      // hard stop: the main screen must never wait longer than this
      timer = setTimeout(function () { finish({ results: [] }); }, 15000);

      // (J) aggregate the first ROW_PAGES SERVER pages of /new in parallel
      // (15 episodes each, both land in the 15-min server cache + our 3 h
      // client cache), render the first ROW_LIMIT of them, and advertise the
      // SERVER depth. "Ещё" then opens category_full('new') and our list()
      // fetches /new?...&page=2, 3, ... — the pages the row already warmed.
      var pending = ROW_PAGES;
      var pages = [];

      function onPage(idx, data) {
        if (!data) { pages[idx] = []; } else { pages[idx] = buildResults('new', data); }
        pending--;
        if (pending > 0) return;

        var results = [];
        for (var i = 0; i < pages.length; i++) {
          results = results.concat(pages[i] || []);
        }
        if (!results.length) { finish({ results: [] }); return; }

        finish({
          url: 'new',
          title: 'Новые релизы LostFilm',
          page: 1,
          total_pages: NEW_PAGES,
          total_results: NEW_TOTAL,
          more: true,
          results: results.slice(0, ROW_LIMIT),
          source: SOURCE
        });
      }

      for (var p = 0; p < ROW_PAGES; p++) {
        (function (idx) {
          apiNew(NEW_LIMIT, idx + 1, function (data) { onPage(idx, data); });
        })(p);
      }
    };
  }

  function addContentRow() {
    try {
      if (window.__lostfilm_v2_row_added) return;
      if (!Lampa.ContentRows || !isFn(Lampa.ContentRows.add)) return;

      window.__lostfilm_v2_row_added = true;

      Lampa.ContentRows.add({
        name: ROW_NAME,
        title: 'Новые релизы LostFilm',
        // index 2 = right below the first native main rows; the channel rows
        // (continue_watch / recomend_watch, all index 1) are inserted at 1 as well,
        // so the row lands after the "Сейчас смотрят" block.
        index: 2,
        screen: ['main'],
        call: function () {
          return makeRowProducer();
        }
      });
    } catch (e) {
      window.__lostfilm_v2_row_added = false;
      console.warn('lostfilm v2 content row fail', e);
    }
  }

  // ------------------------------------------------------ full screen hook

  var fullBound = false;

  function addFullHandler() {
    if (fullBound) return;
    if (!Lampa.Listener || !isFn(Lampa.Listener.follow)) return;
    fullBound = true;
    try {
      Lampa.Listener.follow('full', onFull);
    } catch (e) {
      fullBound = false;
      console.warn('lostfilm v2 full listen fail', e);
    }
  }

  // --------------------------------------------------------------- boot

  // the menu is rendered after app ready in some builds; retry on the public
  // menu event instead of DOM polling (no timers or observers here)
  var menuBound = false;

  function addMenuHandler() {
    if (menuBound) return;
    if (!Lampa.Listener || !isFn(Lampa.Listener.follow)) return;
    menuBound = true;
    try {
      Lampa.Listener.follow('menu', function (event) {
        if (event && event.type === 'toggle') addMenu();
      });
    } catch (e) {
      menuBound = false;
    }
  }

  function init() {
    try {
      register();
      if (!Lampa.Api.sources[SOURCE]) return;
      addMenu();
      addMenuHandler();
      addContentRow();
      addFullHandler();
      addActivityHandler();
      addSettings();
      refreshStatus();
    } catch (e) {
      console.warn('lostfilm v2 init fail', e);
    }
  }

  function start() {
    if (window.__lostfilm_v2_started) return;
    window.__lostfilm_v2_started = true;
    init();
  }

  if (window.appready) {
    start();
  } else if (Lampa.Listener && isFn(Lampa.Listener.follow)) {
    Lampa.Listener.follow('app', function (event) {
      if (event && event.type === 'ready') start();
    });
  }
})();
