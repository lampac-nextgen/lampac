# Documentation project instructions

Repo-wide agent routing is the root [AGENTS.md](../AGENTS.md). Role cards and theses live in `.agents/`. This file applies only while editing the Mintlify site in `docs/` and does not route agents.

## About this project

- This is the Russian documentation site for [Lampac NextGen](https://github.com/lampac-nextgen/lampac).
- The site uses Mintlify. Pages are MDX files with YAML frontmatter.
- Mintlify root is this `docs/` directory: `docs.json`, `.mintignore`, and all MDX live here.
- The parent repository is the product source of truth: application code, `config/base.conf`, `config/example.init.*`, `manifest.json`, compose/Helm, and module READMEs.
- Public DeepWiki is not a source of truth. Do not copy Mintlify MDX into `.devin/wiki.json`. That file steers a 30-page English code wiki; operator docs stay on this Mintlify site.
- Use the Mintlify docs MCP server to verify current components and configuration.
- In the Mintlify dashboard the content path must be `/docs`. GitHub Pages workflow `.github/workflows/pages.yml` builds the Astro project in `site/` and publishes `site/dist`. It does not replace this Mintlify deployment.

## Terminology

- Write the product name as “Lampac NextGen” on first mention, then “Lampac”.
- Keep module and config names as in code: `SkipModules`, `accsdb`, `init.conf`, `GStreamer`, `JacRed`, `TorrServer`.
- Use “модуль”, “провайдер”, “плагин”, “клиент Lampa”.
- Keep API paths and HTTP methods in English: `GET /online.js`, `/nws`, `/api/v2.0`.

## Style preferences

- Write in Russian using active voice and second person (“вы”).
- Keep sentences concise. Use one idea per sentence.
- Use sentence case for headings.
- Bold UI labels: нажмите **Релизы**.
- Use code formatting for file names, commands, paths, options, and endpoints.
- Use root-relative internal links without the `/docs/` prefix and without `.mdx`.
- Add `title`, `sidebarTitle`, `description`, and useful `keywords` to pages.
- Prefer Mintlify components when they improve scanning, not for decoration.
- Give every code fence a language. Keep examples runnable against port `9118`.

## Content boundaries

- Never publish real tokens, passwords, provider credentials, private hosts, cookies, or user data.
- Distinguish `config/base.conf` defaults, `config/example.init.*` starter overrides, and `manifest.json` module state.
- Document public-exposure risks for administrative, proxy, transcoding, filesystem, and debugging modules.
- Do not invent defaults. Verify them in source code or the canonical config.
- One page per functional module and per provider group. Do not create a page per VOD provider.
- API remains hand-written MDX. Do not add OpenAPI unless explicitly requested.
- Before completion, run `mint validate`, `mint broken-links --check-anchors`, and `mint a11y` from `docs/`.
