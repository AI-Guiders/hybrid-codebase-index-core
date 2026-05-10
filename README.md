# HybridCodebaseIndex.Core

Библиотека **`AIGuiders.HybridCodebaseIndex.Core`**: локальный гибридный индекс для кода (SQLite **FTS5** + опционально векторный канал и эмбеддинги через ONNX), сканирование workspace, настройки через TOML.  
Используется в [Hybrid Codebase Index MCP](https://github.com/KarataevDmitry/hybrid-codebase-index) и во встроенном контуре **HCI** [Cascade IDE](https://github.com/KarataevDmitry/cascade-ide).

Лицензия: **MIT** ([LICENSE](LICENSE)). Авторство: **LonelySoul** / **AIGuiders**.

---

## Возможности

- Индекс документов в SQLite с полнотекстом (FTS5) и метаданными.
- Режим **scope**: workspace и/или solution (несколько баз под общим корнем).
- Опционально: эмбеддинги, гибридный поиск (настраивается в `IndexSettings` / `settings.default.toml`).
- Встраиваемый дефолтный конфиг: `DefaultSettings/settings.default.toml` (embedded resource).

Подробнее о тулсах MCP и сценариях — в репозитории [hybrid-codebase-index](https://github.com/KarataevDmitry/hybrid-codebase-index) (`docs/`).

---

## Установка

```bash
dotnet add package AIGuiders.HybridCodebaseIndex.Core --version 0.1.0
```

Актуальная версия: [nuget.org](https://www.nuget.org/packages/AIGuiders.HybridCodebaseIndex.Core).

---

## Сборка локально

```bash
dotnet build HybridCodebaseIndex.Core.csproj -c Release
dotnet pack HybridCodebaseIndex.Core.csproj -c Release -o ./out
```

---

## Публикация на nuget.org (Trusted Publishing)

Долгоживущий API key не требуется: [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing) + шаг **`NuGet/login@v1`** в [workflow](.github/workflows/publish-nuget.yml).

На **nuget.org** добавь политику: **Repository owner** `KarataevDmitry`, **Repository** `hybrid-codebase-index-core`, **Workflow file** `publish-nuget.yml`.  
В workflow указан вход **`user: LonelySoul`** — совпадай с учётной записью, у которой политика и владение пакетом.

Публикация: тег **`v0.x.y`** или **Run workflow** с версией без префикса `v`.

---

## Репозиторий

<https://github.com/KarataevDmitry/hybrid-codebase-index-core>
