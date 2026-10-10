#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Models.Base;
using Shared.Services;
using VImage = NetVips.Image;
using VEnums = NetVips.Enums;

namespace QRAuth.Services
{
    /// <summary>
    /// Poster wall behind the deny page (Netflix-style). The page is shown to visitors
    /// who are NOT authorized yet, and Lampac's accsdb middleware blocks /tmdb/* for them —
    /// so instead of opening the whole TMDB proxy via whitepattern, this module fetches
    /// posters itself through the same /tmdb proxy as a local request (lcrqpasswd header
    /// passes accsdb), caches them on disk, renders them into pre-tilted wall images and
    /// serves only those from /tgbot/qr/wall (let through by ModInit.AllowQrRoutes).
    /// TVs only ever talk to this server, never to TMDB directly (blocked in some regions).
    /// </summary>
    public static class PosterWall
    {
        public const int MaxPosters = 30;
        // w500: the walls use 370px tiles — w342 had to be upscaled into them and the
        // wall looked pixelated; w500 is downscaled instead. The files are only wall
        // sources, never served to the page. Part of the cache key (meta.txt), so a size
        // change refetches the set instead of serving the old files.
        const string Size = "w500";
        // "v2" = pre-dimmed, 1080p + 4K, glass; "-m" = portrait wall for phones. Old wall
        // files are simply ignored and the set's walls are rebuilt in place (the fresh-set
        // path builds missing walls).
        const string WallFile = "wall-v2.jpg", Wall4kFile = "wall-v2-4k.jpg",
                     LqipFile = "wall-v2-lqip.jpg", GlassFile = "wall-v2-glass.jpg",
                     PortraitFile = "wall-v2-sq.jpg", LqipPortraitFile = "wall-v2-sq-lqip.jpg";
        const double Dim = 0.55;
        static string? _lqip, _glass, _lqipPortrait;
        static long _lqipVersion, _glassVersion, _lqipPortraitVersion;
        static bool _wallFailed;
        static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

        static readonly string Dir =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache", "qrauth", "posters");
        static string MetaPath => Path.Combine(Dir, "meta.txt");

        static readonly SemaphoreSlim _gate = new(1, 1);

        // Fallback chain when the host's own /tmdb proxy can't reach api.themoviedb.org /
        // image.tmdb.org (blocked in RU/BY without a proxy). Same public mirrors the Lampa
        // client itself uses with "Проксировать TMDB" on — src/core/tmdb/proxy.js
        // (path_api / path_api_backup / ImageMirror) and src/core/manifest.js (cub domains).
        static readonly string[] ApiMirrors =
        {
            "https://apitmdb.cub.red/3/",
            "https://apitmdb.kurwa-bober.ninja/3/",
            "https://apitmdb.nackhui.com/3/",
            "http://lampa.byskaz.ru/tmdb/api/3/"
        };
        static readonly string[] ImgMirrors =
        {
            "https://imagetmdb.com/",
            "https://nl.imagetmdb.com/",
            "https://de.imagetmdb.com/",
            "https://pl.imagetmdb.com/"
        };

        /// <summary>Number of cached posters (0 = none yet / disabled / fetch failed).</summary>
        public static int Count { get; private set; }

        /// <summary>Cache-busting stamp of the current set, appended as ?v= by the page.</summary>
        public static long Version { get; private set; }

        /// <summary>Short machine-readable outcome of the last refresh, returned by
        /// /tgbot/qr/posters so a failure is diagnosable without shell access to the
        /// server (details still go to tgbot.log): pending | ok | tmdb_unreachable |
        /// tmdb_empty | images_failed | error.</summary>
        public static string State { get; private set; } = "pending";

        /// <summary>Pre-rendered wall (wall.jpg) of the current set, or null if it couldn't be
        /// built (NetVips off/unavailable) — the page then keeps its plain dark background.</summary>
        public static string? WallPath(bool uhd = false)
        {
            if (Count == 0) return null;
            string path = Path.Combine(Dir, uhd ? Wall4kFile : WallFile);
            return File.Exists(path) ? path : null;
        }

        /// <summary>64x36 preview of the wall as base64 JPEG (~1KB) — baked into deny.js so the
        /// page shows the wall's colours on its very first paint while wall.jpg downloads.</summary>
        public static string? WallLqipBase64() => Inline(LqipFile, ref _lqip, ref _lqipVersion);

        /// <summary>The wall pre-blurred/saturated/brightened exactly like the buttons'
        /// backdrop-filter would (320x180, a few KB base64). In wall mode the page paints it
        /// as the buttons' own background, aligned to the screen, instead of a live
        /// backdrop-filter, which showed no blur at all in the Android TV WebView.</summary>
        public static string? WallGlassBase64() => Inline(GlassFile, ref _glass, ref _glassVersion);

        static string? Inline(string file, ref string? cached, ref long cachedVersion)
        {
            if (WallPath() == null) return null;
            if (cached != null && cachedVersion == Version) return cached;
            string path = Path.Combine(Dir, file);
            if (!File.Exists(path)) return null;
            cached = Convert.ToBase64String(File.ReadAllBytes(path));
            cachedVersion = Version;
            return cached;
        }

        /// <summary>Square wall for phones (≤700px wide, either orientation), or null if not built.</summary>
        public static string? PortraitPath()
        {
            if (Count == 0) return null;
            string path = Path.Combine(Dir, PortraitFile);
            return File.Exists(path) ? path : null;
        }

        /// <summary>Portrait counterpart of <see cref="WallLqipBase64"/> (~1KB).</summary>
        public static string? PortraitLqipBase64() =>
            PortraitPath() == null ? null : Inline(LqipPortraitFile, ref _lqipPortrait, ref _lqipPortraitVersion);

        /// <summary>Cheap startup hook: picks up the set already on disk (meta.txt) without
        /// waiting for the first RefreshAsync (15s after load). Without it the deny.js
        /// generated at startup had no wall, and Lampac's FileCache kept serving that copy
        /// for 10 minutes after every restart. Skipped while a refresh is swapping dirs.</summary>
        public static void LoadCached(string source)
        {
            if (Count == 0 && _gate.CurrentCount > 0)
                LoadMeta(source);
        }

        /// <summary>Restores Count/Version from disk after a restart, so a fresh set isn't
        /// re-downloaded just because the process came back up.</summary>
        static void LoadMeta(string source)
        {
            try
            {
                if (!File.Exists(MetaPath)) return;
                var parts = File.ReadAllText(MetaPath).Split(';');
                if (parts.Length < 3 || parts[2] != Key(source)) return;
                Count = int.Parse(parts[0]);
                Version = long.Parse(parts[1]);
            }
            catch { Count = 0; }
        }

        /// <summary>Refetches the set if it's older than a day or the source changed.
        /// Safe to call often — cheap no-op while the cache is fresh.</summary>
        public static async Task RefreshAsync(string source)
        {
            if (!await _gate.WaitAsync(0)) return;
            try
            {
                if (Count == 0) LoadMeta(source);
                var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(Version);
                if (Count > 0 && age < MaxAge && File.Exists(MetaPath) && File.ReadAllText(MetaPath).EndsWith(";" + Key(source)))
                {
                    if (!_wallFailed && (!File.Exists(Path.Combine(Dir, WallFile)) || !File.Exists(Path.Combine(Dir, PortraitFile))))
                        await Task.Run(() => BuildWall(Dir, Count));
                    State = "ok";
                    return;
                }

                string host = $"http://{CoreInit.conf.listen.localhost}:{CoreInit.conf.listen.port}";
                var headers = HeadersModel.Init(("lcrqpasswd", CoreInit.rootPasswd));
                var posters = new List<string>();
                bool answered = false;

                // Host's own proxy first (honours its proxyapi settings), then public mirrors.
                // Whichever answers page 1 is used for the remaining pages.
                var apiBases = new List<string> { $"{host}/tmdb/api/3/" };
                apiBases.AddRange(ApiMirrors);
                string? apiBase = null;

                for (int page = 1; page <= 3 && posters.Count < MaxPosters; page++)
                {
                    JObject? json = null;
                    foreach (var b in apiBase != null ? new List<string> { apiBase } : apiBases)
                    {
                        json = await Http.Get<JObject>(
                            $"{b}{Endpoint(source)}?api_key={CoreInit.conf.cub.api_key}&language=ru&page={page}",
                            timeoutSeconds: 10, headers: b.StartsWith(host) ? headers : null);
                        if (json?["results"] is JArray) { apiBase = b; break; }
                    }

                    if (json?["results"] is not JArray results) break;
                    if (!answered) FileLog.Write($"[PosterWall] {source}: TMDB API через {apiBase}");
                    answered = true;
                    foreach (var item in results)
                    {
                        if (item.Value<bool?>("adult") == true) continue;
                        string? poster = item.Value<string>("poster_path");
                        if (!string.IsNullOrEmpty(poster) && !posters.Contains(poster))
                            posters.Add(poster);
                        if (posters.Count >= MaxPosters) break;
                    }
                }

                if (posters.Count == 0)
                {
                    State = answered ? "tmdb_empty" : "tmdb_unreachable";
                    FileLog.Write(answered
                        ? $"[PosterWall] {source}: TMDB вернул 0 постеров — оставляю прежний набор ({Count})"
                        : $"[PosterWall] {source}: ни {host}/tmdb/api, ни зеркала CUB не ответили — оставляю прежний набор ({Count})");
                    return;
                }

                // Download into a side directory and swap, so the page never sees a
                // half-written set.
                string tmp = Dir + ".tmp";
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                Directory.CreateDirectory(tmp);

                // Same idea for images: stick with the first base that delivers, fall back
                // down the list per poster only when it fails.
                var imgBases = new List<string> { $"{host}/tmdb/img/" };
                imgBases.AddRange(ImgMirrors);
                int imgIdx = 0;

                int saved = 0;
                foreach (var poster in posters)
                {
                    for (int i = imgIdx; i < imgBases.Count; i++)
                    {
                        string b = imgBases[i];
                        var bytes = await Http.Download($"{b}t/p/{Size}{poster}", timeoutSeconds: 15, headers: b.StartsWith(host) ? headers : null);
                        if (bytes == null || bytes.Length < 1024) continue;
                        if (i != imgIdx || saved == 0) FileLog.Write($"[PosterWall] картинки через {b}");
                        imgIdx = i;
                        await File.WriteAllBytesAsync(Path.Combine(tmp, saved + ".jpg"), bytes);
                        saved++;
                        break;
                    }
                }

                if (saved == 0)
                {
                    Directory.Delete(tmp, true);
                    State = "images_failed";
                    FileLog.Write($"[PosterWall] {source}: не скачался ни один постер — оставляю прежний набор ({Count})");
                    return;
                }

                await Task.Run(() => BuildWall(tmp, saved));

                long version = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                await File.WriteAllTextAsync(Path.Combine(tmp, "meta.txt"), $"{saved};{version};{Key(source)}");

                Count = 0;
                if (Directory.Exists(Dir)) Directory.Delete(Dir, true);
                Directory.CreateDirectory(Path.GetDirectoryName(Dir)!);
                Directory.Move(tmp, Dir);
                Count = saved;
                Version = version;
                State = "ok";
                FileLog.Write($"[PosterWall] {source}: сохранено {saved} постеров");
            }
            catch (Exception ex)
            {
                State = "error";
                FileLog.Write("[PosterWall] refresh failed", ex);
            }
            finally
            {
                _gate.Release();
            }
        }

        static string Key(string source) => source + "@" + Size;

        // Wall geometry (built at 4K; 1080p is a downscale of it): 12 x 6 tiles of 370x556
        // (2:3) with a 16px gap → 4616x3416, rotated 7° counter-clockwise (the CSS wall's
        // rotateZ(-7deg)) and centre-cropped to 3840x2160 — the rotated grid still covers
        // the whole frame. The CSS wall's rotateX perspective is not reproduced (flat tilt).
        const int TileW = 370, TileH = 556, Gap = 16, Across = 12, Down = 6, Radius = 20;
        const int OutW = 3840, OutH = 2160;
        // Phone wall: a 2532x2532 square, so the same file covers the phone in both
        // orientations without upscaling — portrait 1170x2532 and landscape 2532x1170 (iPhone-
        // class at dpr 3) are both cover-crops of it. The page picks the wall once on open, so
        // a phone rotated afterwards keeps it; the old 1170x2532 portrait wall had to be
        // blown up ~2x for landscape and looked empty/washed out at the sides. 8 x 6 tiles →
        // 3072x3416; rotated by 7° a centred 2532x2532 crop needs ≥2822x2822 of grid, so it's
        // still fully covered. Same tile size as the 4K wall: ~3 posters across a portrait
        // phone, like the old 4-per-row CSS tiles.
        const int AcrossM = 8, DownM = 6, OutWM = 2532, OutHM = 2532;
        // Glass = the buttons' backdrop-filter: blur(.73em) saturate(1.6) brightness(1.3).
        // .73em of the button font is 1.41% of the viewport width at any size (Lampa's body
        // font = innerWidth/84.17, x1.25 #dpc-l, x1.3 .dpc-b), so sigma = 1.41% of width.
        const int GlassW = 320, GlassH = 180;
        const double GlassSigma = 0.0141 * GlassW, GlassSat = 1.6, GlassBright = 1.3;
        static readonly double[] Bg = { 10, 10, 11 }; // #0a0a0b, the page base colour

        /// <summary>
        /// Renders all posters of a set into flat JPEGs with NetVips, which Lampac already
        /// ships for its image proxy: a landscape wall (4K + 1080p) for TVs/desktops and a
        /// portrait one for phones. One pre-tilted background image is a single request, a
        /// single decode and no compositing work — the old per-poster CSS wall (30 requests,
        /// 72 tiles on a 3D-transformed layer) choked TVs. Pre-dimmed ×.55. Failure is
        /// non-fatal: no wall files → the page keeps its plain dark background.
        /// </summary>
        static void BuildWall(string dir, int count)
        {
            string output = Path.Combine(dir, WallFile);
            string outPortrait = Path.Combine(dir, PortraitFile);
            try
            {
                if (CoreInit.conf.imagelibrary != "NetVips" || count == 0)
                    return;

                // One shuffle, cycled: a poster repeats only every `count` cells (30 → 2.5 rows
                // apart, never a neighbour). Re-shuffling per cycle put repeats side by side.
                var rnd = new Random();
                var shuffled = Enumerable.Range(0, count).OrderBy(_ => rnd.Next()).ToList();
                var order = Enumerable.Range(0, Across * Down).Select(i => shuffled[i % count]).ToList();
                // Portrait starts mid-shuffle so the phone wall doesn't open with the same
                // posters as the top-left of the landscape one.
                var orderM = Enumerable.Range(0, AcrossM * DownM).Select(i => shuffled[(i + count / 2) % count]).ToList();

                // Rounded-corner mask (the CSS tiles have border-radius .45em ≈ 10px).
                using var mask = (VImage.Black(TileW, TileH) + 255).Cast(VEnums.BandFormat.Uchar).Mutate(m =>
                {
                    foreach (var (x, y) in new[] { (0, 0), (TileW - Radius, 0), (0, TileH - Radius), (TileW - Radius, TileH - Radius) })
                        m.DrawRect(new[] { 0.0 }, x, y, Radius, Radius, fill: true);
                    foreach (var (cx, cy) in new[] { (Radius, Radius), (TileW - Radius - 1, Radius), (Radius, TileH - Radius - 1), (TileW - Radius - 1, TileH - Radius - 1) })
                        m.DrawCircle(new[] { 255.0 }, cx, cy, Radius, fill: true);
                });

                var cache = new Dictionary<int, VImage>();
                try
                {
                    VImage Tile(int n)
                    {
                        if (cache.TryGetValue(n, out var tile))
                            return tile;

                        var steps = new List<VImage>();
                        try
                        {
                            var img = VImage.Thumbnail(Path.Combine(dir, n + ".jpg"), TileW, height: TileH, crop: VEnums.Interesting.Centre);
                            steps.Add(img);
                            if (img.HasAlpha()) steps.Add(img = img.Flatten(Bg));
                            if (img.Bands < 3) steps.Add(img = img.Colourspace(VEnums.Interpretation.Srgb));
                            if (img.Width != TileW || img.Height != TileH) steps.Add(img = img.Embed(0, 0, TileW, TileH, background: Bg));
                            // Rounded corners: poster where mask=255, page base colour where 0.
                            tile = (img * mask / 255 + (255 - mask) * Bg[0] / 255).Cast(VEnums.BandFormat.Uchar);
                        }
                        finally
                        {
                            foreach (var st in steps) st.Dispose();
                        }
                        cache[n] = tile;
                        return tile;
                    }

                    // bicubic: the default bilinear visibly softened the poster edges
                    using var bicubic = NetVips.Interpolate.NewFromName("bicubic");

                    // Tilted grid, centre-cropped and dimmed. Dim baked in (×.55 — what the .45
                    // black layer did in CSS): darker pixels compress much better. q60 with
                    // 4:2:0 chroma showed blocking/banding in the dark areas on TVs, hence
                    // higher q and no chroma subsampling in Save().
                    VImage Render(List<int> cells, int across, int outW, int outH)
                    {
                        using var grid = VImage.Arrayjoin(cells.Select(Tile).ToArray(), across: across, shim: Gap, background: Bg);
                        using var rotated = grid.Similarity(angle: -7.0, interpolate: bicubic, background: Bg);
                        using var frame = rotated.Crop((rotated.Width - outW) / 2, (rotated.Height - outH) / 2, outW, outH);
                        return (frame * Dim).Cast(VEnums.BandFormat.Uchar);
                    }

                    void Save(VImage img, string path, int q)
                    {
                        img.Jpegsave(path + ".tmp", q: q, optimizeCoding: true, interlace: true, subsampleMode: VEnums.ForeignSubsample.Off, keep: VEnums.ForeignKeep.None);
                        File.Move(path + ".tmp", path, true);
                    }

                    using (var portrait = Render(orderM, AcrossM, OutWM, OutHM))
                    {
                        using var lqipM = portrait.ThumbnailImage(48, height: 48);
                        lqipM.Jpegsave(Path.Combine(dir, LqipPortraitFile), q: 50, keep: VEnums.ForeignKeep.None);
                        Save(portrait, outPortrait, 82);
                    }

                    using var dimmed = Render(order, Across, OutW, OutH);
                    using var hd = dimmed.Resize(0.5);
                    using var lqip = hd.ThumbnailImage(64, height: 36);
                    lqip.Jpegsave(Path.Combine(dir, LqipFile), q: 50, keep: VEnums.ForeignKeep.None);

                    // Glass: blur, then the CSS saturate() matrix (sRGB, Filter Effects spec),
                    // then brightness — the same chain as the buttons' backdrop-filter.
                    double sat = GlassSat;
                    using var satMatrix = VImage.NewFromArray(new double[,]
                    {
                        { 0.213 + 0.787 * sat, 0.715 - 0.715 * sat, 0.072 - 0.072 * sat },
                        { 0.213 - 0.213 * sat, 0.715 + 0.285 * sat, 0.072 - 0.072 * sat },
                        { 0.213 - 0.213 * sat, 0.715 - 0.715 * sat, 0.072 + 0.928 * sat }
                    });
                    using var small = hd.ThumbnailImage(GlassW, height: GlassH);
                    using var blurred = small.Gaussblur(GlassSigma);
                    using var saturated = blurred.Recomb(satMatrix);
                    using var glass = (saturated * GlassBright).Cast(VEnums.BandFormat.Uchar);
                    glass.Jpegsave(Path.Combine(dir, GlassFile), q: 75, keep: VEnums.ForeignKeep.None);

                    string out4k = Path.Combine(dir, Wall4kFile);
                    Save(dimmed, out4k, 78);

                    // 1080p last: its existence is what WallPath() checks, so the page never
                    // sees a half-built set.
                    Save(hd, output, 85);
                    _wallFailed = false;
                    FileLog.Write($"[PosterWall] стена собрана: 1080p {new FileInfo(output).Length / 1024} KB, 4K {new FileInfo(out4k).Length / 1024} KB, телефон {new FileInfo(outPortrait).Length / 1024} KB");
                }
                finally
                {
                    foreach (var t in cache.Values) t.Dispose();
                }
            }
            catch (Exception ex)
            {
                // DllNotFoundException / TypeInitializationException when libvips' native
                // part is missing for this platform, or a broken poster file.
                _wallFailed = true;
                FileLog.Write("[PosterWall] стена не собрана, страница останется без фона", ex);
                try { if (File.Exists(output)) File.Delete(output); } catch { }
                try { if (File.Exists(outPortrait)) File.Delete(outPortrait); } catch { }
            }
        }

        static string Endpoint(string source) => source switch
        {
            "popular" => "movie/popular",
            "now_playing" => "movie/now_playing",
            "top_rated" => "movie/top_rated",
            _ => "trending/all/week"
        };
    }
}
