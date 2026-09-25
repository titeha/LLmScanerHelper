# LLM Scan Helper v5 (MVVM; WPF + Avalonia)

Сканер GGUF + генератор параметров `llama-server` для пары V100 + desktop RTX:
безопасный AUTO `--fit`, MANUAL-режим, MTP, reasoning, мультимодальность (mmproj),
sampling-параметры разработчика, сохранение профилей и оценка распределения слоёв.

Прямой потомок LINQPad-скрипта v3: формулы и флаги перенесены 1:1,
памятка «ПОЧЕМУ ТАК» доступна в приложении в отдельном окне **«Справка (?)»** (кнопка в заголовке).

## Сборка и запуск

Требуется [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
(или готовый runtime: .NET 10 Desktop Runtime на Windows, .NET 10 Runtime на Linux).
Артефакты сборки — в корневом `artifacts/`, см. `Directory.Build.props`.

### На Windows

Сборка всего решения (обе оболочки — WPF и Avalonia):

```cmd
dotnet build -c Release
```

Запуск WPF-версии:

```cmd
dotnet run -c Release --project UI/LlmScanHelper.UI.Windows
```

Готовый exe: `artifacts\bin\LLMScanHelper\release\LLMScanHelper.exe`.

### На Linux (Avalonia)

Сборка и запуск на самой Linux-машине:

```bash
dotnet build -c Release UI/LlmScanHelper.UI.Linux
dotnet run -c Release --project UI/LlmScanHelper.UI.Linux
```

Кросс-сборка с Windows под linux-x64:

```cmd
dotnet build -c Release UI/LlmScanHelper.UI.Linux -r linux-x64 --self-contained false
```

Результат: `artifacts\bin\LlmScanHelper.UI.Linux\release_linux-x64\` (исполняемый
`LLMScanHelper` + dll; запускать на Linux с .NET 10 runtime).

Примечание: целиком решение на Linux не собирается — WPF-проект требует Windows
(NETSDK1100); собирайте `UI.Linux`, он сам тянет Core и UI.Common.

## Открытие в IDE

- **Visual Studio 2022** (17.12+, рабочая нагрузка «Разработка классических приложений .NET»):
  откройте `LLMScanHelper.sln` (или папку через «Открыть папку») → `F5` — сборка,
  отладка и XAML-редактор работают из коробки.
- **VS Code**: установите .NET 10 SDK и расширение **C# Dev Kit**
  (VS Code сам предложит его через `.vscode/extensions.json`).
  Открыть папку проекта → `Ctrl+Shift+B` (сборка), `F5` (запуск с отладкой).
  Визуального XAML-дизайнера нет — только подсветка и подсказки.

## Что где

Решение `LLMScanHelper.sln` — 5 проектов (после расщепления Core/UI):
**Core** (`Core/LlmScanHelper.Core/`, net10.0) — доменная логика, без UI;
**UI.Common** (`UI/LlmScanHelper.UI.Common/`, net10.0) — MainViewModel и абстракции, без WPF;
**UI.Windows** (`UI/LlmScanHelper.UI.Windows/`, net10.0-windows) — WPF-оболочка (Windows);
**UI.Linux** (`UI/LlmScanHelper.UI.Linux/`, net10.0) — Avalonia-оболочка (Linux);
**Tests** (`tests/LLMScanHelper.Tests/`, net10.0) — xUnit (23 теста).

| Файл | Назначение |
|---|---|
| `Core/LlmScanHelper.Core/GgufInfo.cs` | парсер GGUF (архитектура, блоки, KV, MTP/nextn, reasoning, tools) |
| `Core/LlmScanHelper.Core/GgufScannerService.cs` | обход дерева моделей, издатель, поиск mmproj |
| `Core/LlmScanHelper.Core/GpuService.cs` | `llama-server --list-devices` (парсинг CUDA-id и свободной VRAM) |
| `Core/LlmScanHelper.Core/LayerEstimator.cs` | грубая оценка раскладки блоков (веса+KV) по картам |
| `Core/LlmScanHelper.Core/Command/LlamaServerCommandBuilder.cs` | строка запуска llama-server + список предупреждений (`Build`/`BuildWarnings`) — единственный источник флагов |
| `Core/LlmScanHelper.Core/Command/LlamaServerParams.cs` | объект-параметры для сборщика: все поля команды в одном месте |
| `Core/LlmScanHelper.Core/Gpu/FitTargets.cs` | математика `--fit-target`: разбор устройств, резервы GiB→MiB |
| `Core/LlmScanHelper.Core/Estimation/LayerEstimateFormatter.cs` | текстовое форматирование оценки распределения слоёв |
| `Core/LlmScanHelper.Core/AliasBuilder.cs` | генератор алиаса из имени файла (убирает квант-теги) |
| `Core/LlmScanHelper.Core/AppDefaults.cs` | константы по умолчанию (корень моделей, контекст, порт, хост) |
| `Core/LlmScanHelper.Core/ModelTypes.cs` | доменные типы: ModelEntry, MmprojEntry, GpuDeviceInfo, GpuQueryResult |
| `Core/LlmScanHelper.Core/Settings/SettingsStore.cs` | JSON-хранилище (portable) |
| `UI/LlmScanHelper.UI.Common/ViewModels/MainViewModel.cs` | ядро: состояние, параметры, команды, сканирование; DI: IClipboard/IUiWindows/IFolderPicker |
| `UI/LlmScanHelper.UI.Common/ViewModels/MainViewModel.Model.cs` | инфо о модели, загрузка, мультимодальность (mmproj) |
| `UI/LlmScanHelper.UI.Common/ViewModels/MainViewModel.MtpReasoning.cs` | сервер (хост/порт), MTP, reasoning, jinja |
| `UI/LlmScanHelper.UI.Common/ViewModels/MainViewModel.Gpu.cs` | GPU layout и опрос устройств |
| `UI/LlmScanHelper.UI.Common/ViewModels/MainViewModel.Presets.cs` | пресеты |
| `UI/LlmScanHelper.UI.Common/ViewModels/MainViewModel.Catalogs.cs` | корневые каталоги моделей |
| `UI/LlmScanHelper.UI.Common/ViewModels/MainViewModel.Persistence.cs` | сохранение/загрузка настроек (дебаунс через Debouncer) |
| `UI/LlmScanHelper.UI.Common/ViewModels/MainViewModel.Output.cs` | сборка команды, предупреждения, оценка слоёв, буфер обмена |
| `UI/LlmScanHelper.UI.Common/Services/IClipboard.cs`, `IUiWindows.cs`, `IFolderPicker.cs` | абстракции UI (`IFolderPicker` — async-only: `PickFolderAsync`); WPF-реализации — `UI.Windows/Platform/`, Avalonia-реализации — `UI.Linux/Platform/` |
| `UI/LlmScanHelper.UI.Common/Services/Debouncer.cs` | дебаунс (сохранение настроек, индикатор копирования) |
| `UI/LlmScanHelper.UI.Common/Texts/memo.md` | памятка «ПОЧЕМУ ТАК» (Markdown, показывается обычным текстом в окне «Справка») |
| `UI/LlmScanHelper.UI.Common/Texts/ToolTips.cs` | popup-подсказки по всем параметрам (зачем/влияет/дока) |
| `UI/LlmScanHelper.UI.Windows/App.xaml`, `App.xaml.cs` | старт приложения: темы MahApps, финальное сохранение при выходе |
| `UI/LlmScanHelper.UI.Windows/Windows/MainWindow.xaml` | каркас окна: панель (PanelTabView) + кнопки в заголовке (Настройки/Справка) |
| `UI/LlmScanHelper.UI.Windows/Views/PanelTabView.xaml` | основной UserControl на главном окне: параметры, инфо, команда |
| `UI/LlmScanHelper.UI.Windows/Views/SettingsTabView.xaml` | вкладка «Настройки» (каталоги моделей); встроена в SettingsWindow |
| `UI/LlmScanHelper.UI.Windows/Windows/SettingsWindow.xaml` | отдельное окно «Настройки», открывается из заголовка |
| `UI/LlmScanHelper.UI.Windows/Windows/HelpWindow.xaml` | отдельное окно «Справка» (памятка «Почему так»), открывается из заголовка |
| `UI/LlmScanHelper.UI.Windows/Views/MemoTabView.xaml` | UserControl с памяткой «ПОЧЕМУ ТАК» (используется в HelpWindow) |
| `UI/LlmScanHelper.UI.Windows/Controls/TextBoxHelpers.cs` | attached-поведение: коммит по Enter (TextBox / редактируемый ComboBox) |
| `UI/LlmScanHelper.UI.Windows/Controls/ToolTipLinker.cs` | кликабельные ссылки в тултипах (перехват «сквозного» клика) |
| `UI/LlmScanHelper.UI.Windows/Platform/WpfClipboard.cs`, `WpfUiWindows.cs`, `WpfFolderPicker.cs` | WPF-реализации абстракций из UI.Common |
| `UI/LlmScanHelper.UI.Linux/Program.cs`, `App.axaml(.cs)` | старт Avalonia: Fluent-тема, desktop-lifetime, финальное сохранение при выходе |
| `UI/LlmScanHelper.UI.Linux/MainWindow.axaml(.cs)` | главное окно: `DataContext = MainViewModel` (Avalonia-реализации), `InitializeAsync`/`FlushPendingSave` |
| `UI/LlmScanHelper.UI.Linux/Views/PanelTabView.axaml` | Avalonia-перенос основной панели (параметры, инфо, строка запуска) |
| `UI/LlmScanHelper.UI.Linux/Views/SettingsTabView.axaml`, `Windows/SettingsWindow.axaml` | окно «Настройки» (каталоги моделей) |
| `UI/LlmScanHelper.UI.Linux/Views/MemoTabView.axaml`, `Windows/HelpWindow.axaml` | окно «Справка» (памятка; читается из встроенного `Texts/memo.md` через `avares://`) |
| `UI/LlmScanHelper.UI.Linux/Platform/AvaloniaClipboard.cs`, `AvaloniaUiWindows.cs`, `AvaloniaFolderPicker.cs` | Avalonia-реализации абстракций из UI.Common |
| `UI/LlmScanHelper.UI.Windows/Assets/app.ico` / `app-icon.png` | иконка приложения (exe и окно) |

## Параметры

- **Хранилище**: `settings.json` создаётся **рядом с exe** (portable-режим).
  Глобальные параметры (GPU/сервер/sampling) + по-модельные профили
  (контекст, KV, MTP, reasoning, mmproj, алиас) — восстанавливаются
  между сессиями и перечитываниями моделей. Битый файл уводится в `settings.json.bad`.
- **Корень моделей**: по умолчанию `W:\LLStudio\Models`, правится в интерфейсе.
- **GPU**: для опроса `llama-server` должен быть доступен в `PATH`.
- **MTP**: переключатель доступен, если в модели есть MTP/nextn-тензоры;
  инфо-панель показывает тип и число доп. токенов за шаг, если удалось распознать.
- **Reasoning**: 3-режим (on/off/auto; дефолт нового профиля — auto — runtime решает
  по чат-шаблону). Бюджет/сообщение активны при on и auto (при off — только `--reasoning off`).
  При `on` и бюджете `0` используется минимальный бюджет 1024 токенов
  (`--reasoning-budget 1024`); при `auto` и `0` — флаг не передаётся.
  Сообщение бюджета —
  редактируемый ComboBox: общий список (все модели) или новый ввод; список живёт в
  `settings.json` (раздел `ReasonBudgetMessages`).
- **Инструменты (агентская работа)**: сканер оценивает поддержку tool-calls —
  сканирует все `tokenizer.chat_template*` на обработку `tools`/`tool_calls`
  и словарь на спец-токены вида `<tool_call>`. Вердикт (да/нет/неизвестно)
  виден в инфо о модели; при «да» автоматически включается `--jinja`
  (родной chat-шаблон GGUF — сервер передаёт функции и парсит `tool_calls`).
  Ручное переключение `--jinja` запоминается в профиле модели.
- **Алиас**: не приводится к нижнему регистру; разделители сохраняются как в имени
  файла; правленный вручную алиас запоминается за моделью.
- **Строка запуска**: отдельный блок в правой колонке; обновляется по кнопке
  «Собрать команду» (вместе с предупреждениями и оценкой слоёв); одна кнопка
  «Копировать в буфер».
