# TorBox server module

Optional TorBox integration for Lampac/Lampa. The module is disabled by default
(`manifest.enable: false`). No credentials or populated runtime configuration are
included in the repository.

## Scope of this PR

This first PR contains the server APIs and the standalone `torbox.js` extension:
My TorBox, original-file playback, Infuse actions, Airlock controls, deletion and
cache checks in the native Torrents component. It does **not** change the shared
`Online/plugin.js` or register TorBox as an online-balancer source.

The Online adapter, source registration, release badges/voice filters, menu
preview cards, selected-stream handoff and series continuation belong to a
separate dependent change. Their server endpoints remain in this module for
API review, but this PR does not advertise an incomplete balancer.

The online streaming endpoints use `stream/createstream` and require TorBox Pro,
including original-resolution HLS and external-player handoff. There is no
automatic direct-file fallback. My TorBox and the Torrents adapter use
`torrents/requestdl` for original-file playback; codec/container and embedded
track support depend on the receiving player. Lampac does not transcode video.
Provider entitlements can change; see [TorBox pricing](https://torbox.app/pricing)
and [streaming documentation](https://support.torbox.app/en/articles/12662996-torbox-web-streaming).
Whether this paid-provider dependency belongs in the project remains a maintainer
decision. This PR does not enable it automatically or change other providers.

## Installation

Install the module sources and `plugin.js` into `mods/TorBox/` in the Lampac
working directory and enable the module through the instance's module settings.
Add `https://<your-Lampac>/torbox.js` to Lampa extensions, named TorBox, and reload
Lampa. Use the actual external address/path of your instance and HTTPS.
The module serves the extension itself; no third-party script download is needed.

Place `torbox-private.json` beside `init.conf`. Restrict it to the Lampac service
account (`0600` on Unix). Never commit a populated configuration:

```json
{
  "allow_client_credentials": false,
  "allow_external_playlists": false,
  "api_key": "REPLACE_WITH_YOUR_TORBOX_KEY",
  "allowed_uids": ["REPLACE_WITH_LAMPAC_UID"],
  "torrent_origins": ["https://parser.example"],
  "jacred": {
    "base_url": "https://jacred.example",
    "api_key": "REPLACE_WITH_JACRED_KEY"
  }
}
```

The API key and UID whitelist are server-side. Normal Lampac authentication and
a nonempty authenticated UID are required for protected operations. Enable the
account's required Lampac permissions as well; client headers do not bypass them.

## Credential modes

The administrator selects one mode with the JSON boolean `allow_client_credentials`:

- **False or omitted:** only file credentials/settings are used. Personal input
  fields are hidden; saved personal values are not sent. The authenticated UID
  must be in the file's `allowed_uids`. Request headers cannot enable client mode.
- **True:** only personal client settings are used. The file's key, UID list,
  torrent origins and JacRed configuration are ignored, with no fallback. A
  minimal file can be `{ "allow_client_credentials": true }`. The independent
  `allow_external_playlists` flag remains an administrator policy in this mode.

If the private file is absent, a protected `torbox-settings.json` can explicitly
permit client mode. Missing files, an unreadable/malformed existing private file,
or a nonboolean permission flag fail closed. A client cannot override the mode
using `X-TorBox-Mode`.

When permitted, Settings → TorBox exposes the personal TorBox key, permitted
Lampac UIDs, trusted torrent origins, JacRed URL and JacRed key. An empty UID list
means the current authenticated account. Empty origins disable URL-based torrent
fetches while magnet/hash handling remains available. Missing personal JacRed
configuration disables balancer search, not original-file My TorBox playback.

Personal settings are stored in IndexedDB, scoped to the Lampac server/account.
Disabling client permission retains local values but stops using them. Key
summaries are masked. Client mode necessarily exposes supplied keys to the
browser and the Lampac server handling requests: use trusted devices/server and
HTTPS. `X-TorBox-Key` carries the TorBox key; `X-TorBox-Config` carries UTF-8/base64
JSON configuration. Base64 is encoding, not encryption. Header forwarding from
the shared Online component belongs to the dependent Online PR.

User-selected parser/torrent addresses must be public HTTP(S). DNS results and
redirect destinations are checked, connections are pinned to validated public
IPs, and private/loopback/special-purpose targets are blocked. Client parser
connections bypass automatically configured HTTP proxies. Server-file mode and
TorBox API networking retain their existing network behavior. Redirects are not
automatically followed by the module's HTTP clients.

## Cache batches and playback

Cache checks accept 1–100 valid hashes per request, normalize base32 hashes to
hex, and deduplicate them. The Torrents adapter checks the search before native
pagination, combines its cached-only preference with native filters, and offers
TorBox or the original TorrServer path for a confirmed cached selection.

Identical concurrent batches share a credential-scoped result for 30 seconds.
Upstream failures impose a 10-second credential cooldown. Each shared upstream
batch has its own 8-second deadline, independent of the first client's
cancellation. Hits, misses and unknown/error states remain distinct. Retryable
client chunks get one delayed retry; a second failure stops that queue.

Playback rechecks readiness. Attachment requests use `add_only_if_cached=true`;
there is no silent uncached-download fallback. Cache badges are snapshots and do
not guarantee the file is still available when playback starts.

## Library, deletion and Airlock

My TorBox lists account torrents/files, supports optional film metadata and
InfuseSaver actions, and uses per-key inventory snapshots with request
coalescing and mutation invalidation. Infuse actions require the instance's
existing InfuseSaver integration. Metadata is scoped to the authenticated
account (and personal key in client mode).

Deletion requires explicit user confirmation and sends only the selected ID
with `all=false`. The UI hides the card after acceptance, then checks after one
minute. Acceptance is not proof of deletion. Only confirmed disappearance
removes film metadata while the original confirmation ticket is available.
`HTTP 404` with `ITEM_NOT_FOUND` is recognized; unrelated errors and malformed
responses do not confirm disappearance. Lost tickets permit only scoped read-only
lookup, without metadata removal. Client jobs survive reloads; warnings are
deferred while another section/player is active and are rechecked before display.
Automated checks use mocks and never delete real account data.

Airlock controls operate on the selected torrent after confirming the API result;
quota/state refreshes do not imply an unconfirmed mutation succeeded.

## Link lifetimes and external-player access

- Selection menus: up to 6 hours from search creation, subject to whole-menu LRU
  eviction at the shared 512-choice limit. Expired/evicted menus return 410; reopen
  the source. Eviction does not delete TorBox torrents or files.
- Built-in series sessions: 12 hours idle, renewed by episode requests.
- External season playlists: 12 hours idle and an absolute 7-day cap from creation.
  Preparation/relay requests renew only the idle deadline. Playing an already
  resolved stream does not renew it; resolved URLs are cached for 30 seconds.
- Restarting Lampac loses the in-memory menus/sessions/playlists. TorBox-issued
  stream URLs have separate provider-controlled lifetimes.

`torbox/series/external/{access}/{index}.m3u8` is an anonymous capability route:
knowing the URL authorizes the fixed season/selected playback settings without
client credential headers. Creation still requires authenticated Lampac access.
`access` is generated using `RandomNumberGenerator.GetBytes(32)`: **256 bits of
entropy**, encoded as 64 hexadecimal characters. It is not an API key, but the
complete URL is a bearer secret. Server-side relay sessions and received personal
keys stay in process memory, without persistence to server disk. Browser personal
settings remain in IndexedDB.

External playlist creation **and resolution are disabled by default**. The
administrator must explicitly set boolean `allow_external_playlists: true` in
the active private/settings policy file after configuring logs as described below.
Client configuration cannot enable it; nonboolean values fail closed.

Every relay request checks the current mode, server key and server UID whitelist.
In client mode, the personal key is retained in the session: saving a different
key in the browser alone does not revoke an older external playlist.

Use **Settings → TorBox → Revoke external playlists** (the existing UI is localized)
or authenticated `POST /torbox/series/external/revoke` to revoke all external
playlists for the current Lampac UID. This also works with the account's new
personal key. It does not affect other UIDs. Revocation removes sessions, clears
stored keys/stream caches and blocks subsequent resolution. A TorBox URL already
delivered to a player, or an already opened stream, cannot be revoked by Lampac.

## Logs and Referer

Preparation/protected API and relay responses set `Cache-Control: no-store` and
`Referrer-Policy: no-referrer`. Core's global error output and the built-in
Telemetry/LogUserRequest-Lite URI logs redact external path tokens and `apikey`
query values before writing/truncation. The module does not log raw outbound
URLs, upstream response bodies or HTTP exception text. Client errors use fixed
messages. Do not enable HTTP traces that expose full URIs, credential headers,
response bodies or `Location` values.

The application cannot control reverse proxy/CDN/APM logs, arbitrary extensions
or external-player histories. Before enabling bearer playlists, exclude the
relay route from access/error/trace logs at **every** layer or mask its access
segment. Also exclude/mask `apikey` on the JacRed server and its proxies. Verify
with dummy tokens/keys and inspect the saved logs; do not use real secrets for
that check. For nginx, `access_log off` in a dedicated relay location covers
access logging only: error/diagnostic logs and upstream CDNs need separate review.
If those layers cannot be controlled, keep `allow_external_playlists: false`.

Browsers that honor `Referrer-Policy` will not forward the capability URL as
`Referer` across the redirect. An arbitrary external player may ignore that
policy, save the URL in its own history/logs, or send it as `Referer`. This module
cannot promise otherwise: use a trusted, verified player or leave the feature off.

The bundled [JacRed controller](../JacRed/ApiController.cs) accepts `apikey` as a
query parameter and does not implement a header equivalent. The module retains
that compatible format rather than guessing a header or silently falling back.
Search requests are server-side; their credential-bearing URLs are not sent to
the browser. Use HTTPS and configure JacRed/proxy logging as described above.

## Reproducible checks

Requires .NET SDK 10, Python 3 and Node.js. From the repository root:

```bash
dotnet build Core/Core.csproj --nologo -p:PlaywrightPlatform=linux
dotnet build Modules/TorBox/TorBox.csproj --nologo -p:PlaywrightPlatform=linux
dotnet build Modules/Telemetry/Telemetry.csproj --nologo -p:PlaywrightPlatform=linux
dotnet build Modules/LogUserRequest-Lite/LogUserRequest.csproj --nologo -p:PlaywrightPlatform=linux
python3 tools/check-torbox.py
node --check Modules/TorBox/plugin.js
node --check Online/plugin.js
```

`check-torbox.py` creates a temporary console project outside the solution,
compiles the actual `manifest.tree` production sources unchanged with a check
entrypoint, and runs in a disposable working directory. No permanent test csproj
or test framework packages are added. Test-only interception of existing
HttpClient handlers prevents real runtime network calls and mutations.

The 75 behavior checks cover credential policies/no fallback, UID checks, cache
normalization/bounds/coalescing/isolation/cooldown/cancellation, deletion and
metadata confirmation, relay policy/lifetimes/revocation and log redaction. They
do not validate physical players or operator proxy settings.

`.github/workflows/test-torbox.yml` builds the host/module/request loggers, runs
these checks and JS syntax checks, and uploads `torbox-check-logs`. It uses
read-only `pull_request` permissions and no secrets. Fork runs may require normal
maintainer approval. It can also be run manually with `workflow_dispatch`.
