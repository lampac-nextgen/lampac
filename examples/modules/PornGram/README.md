# PornGram (пример)

Учебный модуль раздела **Sisi**: **`IModuleSisi`** возвращает один канал **`PornGram`** с **`SisiSettings`**; дополнительно реализован **`IModuleSisiAsync`** с **`InvokeAsync`**, возвращающим **`Task.FromResult(default(...))`** (пустой асинхронный контур).

## Поведение

- **`Invoke`** возвращает один канал **`PornGram`** с текущими **`SisiSettings`**.
- Конфиг: **`ModuleInvoke.Init("PornGram", …)`**, хост **`porngram.com`** в примере, **`streamproxy`**, **`displayindex = 1`**.

## Файлы

Обычно рядом **`Controller.cs`** (см. **`manifest.json`**: `Controller.cs`, `ModInit.cs`).

Контент **18+**, если подключать к реальному источнику.
