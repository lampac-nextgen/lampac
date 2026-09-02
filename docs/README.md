# Документация Lampac NextGen

Исходники сайта документации для [Lampac NextGen](https://github.com/lampac-nextgen/lampac) — самохостируемого backend-сервера для [Lampa](https://github.com/yumata/lampa). Операторские гайды публикуются на [docs.lampac.dev](https://docs.lampac.dev).

Сайт построен на [Mintlify](https://mintlify.com). Основная конфигурация находится в `docs.json`, страницы — в MDX-файлах.

Маркетинговый хаб [lampac.dev](https://lampac.dev) живёт в каталоге `site/` (Astro, GitHub Pages) и не входит в это дерево.

## Локальная разработка

Установите Mintlify CLI и запустите предпросмотр из каталога `docs`:

```bash
npm install --global mint
cd docs
mint dev
```

Сайт откроется по адресу `http://localhost:3000`.

## Проверки

Перед отправкой изменений выполните:

```bash
cd docs
mint validate
mint broken-links --check-anchors
mint a11y
```

## Источники данных

- Код приложения, `config/base.conf`, `config/example.init.*`, `manifest.json`, compose и Helm — источник правды.
- Брендинг Mintlify: `docs/assets/`.
- Постоянные инструкции агента: `docs/AGENTS.md`.

Не добавляйте OpenAPI, пока это явно не запрошено. Не копируйте MDX в `.devin/wiki.json`.

## Правила контента

- Пишите по-русски, в активном залоге и обращайтесь к читателю на «вы».
- Используйте корневые внутренние ссылки без расширения: `/configuration/overview`.
- Не копируйте секреты, токены, cookie или реальные ключи из конфигурационных файлов.
- Проверяйте технические утверждения по коду и каноническому конфигу, а не по лендингу или DeepWiki.
