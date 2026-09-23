# LLM Scan Helper v5 (WPF / MVVM)

Сканер GGUF + генератор параметров `llama-server` для пары V100 + desktop RTX:
безопасный AUTO `--fit`, MANUAL-режим, MTP, reasoning, мультимодальность (mmproj),
sampling-параметры разработчика, сохранение профилей и оценка распределения слоёв.

Прямой потомок LINQPad-скрипта v3: формулы и флаги перенесены 1:1,
памятка «ПОЧЕМУ ТАК» доступна в приложении в отдельном окне **«Справка (?)»** (кнопка в заголовке).

## Сборка и запуск

Требуется Windows 10/11 и [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
(или просто .NET 10 Desktop Runtime для готовой сборки).

```cmd
dotnet build -c Release
dotnet run -c Release --project UI/LlmScanHelper.UI.Windows
```

Готовый exe: `artifacts\bin\LLMScanHelper\release\LLMScanHelper.exe`
(артефакты сборки — в корневом `artifacts/`, см. `Directory.Build.props`).

## Открытие в IDE

- **Visual Studio 2022** (17.12+, рабочая нагрузка «Разработка классических приложений .NET»):
  откройте `LLMScanHelper.sln` (или папку через «Открыть папку») → `F5` — сборка,
  отладка и XAML-редактор работают из коробки.
- **VS Code**: установите .NET 10 SDK и расширение **C# Dev Kit**
  (VS Code сам предложит его через `.vscode/extensions.json`).
  Открыть папку проекта → `Ctrl+Shift+B` (сборка), `F5` (запуск с отладкой).
  Визуального XAML-дизайнера нет — только подсветка и подсказки.

## Что где

Решение `LLMScanHelper.sln` — 4 проекта (после расщепления Core/UI):
**Core** (`Core/LlmSanHelper.Core/`, net10.0) — доменная логика, без UI;
**UI.Common** (`UI/LlmScanHelper.UI.Common/`, net10.0) — MainViewModel и абстракции, без WPF;
**UI.Windows** (`UI/LlmScanHelper.UI.Windows/`, net10.0-windows) — WPF-оболочка;
**Tests** (`tests/LLMScanHelper.Tests/`, net10.0) — xUnit (23 теста).

| Файл | Назначение |
|---|---|
| `Core/LlmScanHelper.Core/GgufInfo.cs` | парсер GGUF (архитектура, блоки, KV, MTP/nextn, reasoning, tools) |
| `Core/LlmScanHelper.Core/GgufScannerService.cs` | обход дерева моделей, издатель, поиск mmproj |
| `Core/LlmSanHelper.Core/GpuService.cs` | `llama-server --list-devices` (парсинг CUDA-id и свободной VRAM) |
| `Core/LlmScanHelper.Core/LayerEstimator.cs` | грубая оценка раскладки блоков (веса+KV) по картам |
| `Core/LlmScanHelper.Core/AliasBuilder.cs` | генератор алиаса из имени файла (убирает квант-теги) |
| `Core/LlmScanHelper.Core/AppDefaults.cs` | константы по умолчанию (корень моделей, контекст, порт, хост) |
| `Core/LlmScanHelper.Core/ModelTypes.cs` | доменные типы: ModelEntry, MmprojEntry, GpuDeviceInfo, GpuQueryResult |
| `Core/LlmSanHelper.Core/Settings/SettingsStore.cs` | JSON-хранилище (portable) |
| `UI/LlmScanHelper.UI.Common/ViewModels/MainViewModel.cs` | ядро: состояние, параметры, команды, сканирование; DI: IClipboard/IUiWindows/IFolderPicker |
| `UI/LlmSanHelper.UI.Common/ViewModels/MainViewModel.Model.cs` | инфо о модели, загрузка, мультимодальность (mmproj) |
| `UI/LlmScanHelper.UI.Common/ViewModels/MainViewModel.MtpReasoning.cs` | сервер (хост/порт), MTP, reasoning, jinja |
| `UI/LlmScanHelper.UI.Common/ViewModels/MainViewModel.Gpu.cs` | GPU layout и опрос устройств |
| `UI/LlmSanHelper.UI.Common/ViewModels/MainViewModel.Presets.cs` | пресеты |
| `UI/LlmScanHelper.UI.Common/ViewModels/MainViewModel.Catalogs.cs` | корневые каталоги моделей |
| `UI/LlmScanHelper.UI.Common/ViewModels/MainViewModel.Persistence.cs` | сохранение/загрузка настроек (дебаунс через Debouncer) |
| `UI/LlmSanHelper.UI.Common/ViewModels/MainViewModel.Output.cs` | сборка команды, предупреждения, оценка слоёв, буфер обмена |
| `UI/LlmScanHelper.UI.Common/Services/IClipboard.cs`, `IUiWindows.cs`, `IFolderPicker.cs` | абстракции UI; WPF-реализации — в `UI.Windows/Platform/` |
| `UI/LlmSanHelper.UI.Common/Services/Debouncer.cs` | дебаунс (сохранение настроек, индикатор копирования) |
| `UI/LlmScanHelper.UI.Common/Texts/memo.md` | памятка «ПОЧЕМУ ТАК» (Markdown, вкладка «Памятка»; парсер — позже) |
| `UI/LlmScanHelper.UI.Common/Texts/ToolTips.cs` | popup-подсказки по всем параметрам (зачем/влияет/дока) |
| `UI/LlmScanHelper.UI.Windows/App.xaml`, `App.xaml.cs` | старт приложения: темы MahApps, финальное сохранение при выходе |
| `UI/LlmScanHelper.UI.Windows/Windows/MainWindow.xaml` | каркас окна: панель (PanelTabView) + кнопки в заголовке (Настройки/Справка) |
| `UI/LlmSanHelper.UI.Windows/Views/PanelTabView.xaml` | основной UserControl на главном окне: параметры, инфо, команда |
| `UI/LlmScanHelper.UI.Windows/Views/SettingsTabView.xaml` | вкладка «Настройки» (каталоги моделей); встроена в SettingsWindow |
| `UI/LlmSanHelper.UI.Windows/Windows/SettingsWindow.xaml` | отдельное окно «Настройки», открывается из заголовка |
| `UI/LlmScanHelper.UI.Windows/Windows/HelpWindow.xaml` | отдельное окно «Справка» (памятка «Почему так»), открывается из заголовка |
| `UI/LlmScanHelper.UI.Windows/Views/MemoTabView.xaml` | UserControl с памяткой «ПОЧЕМУ ТАК» (используется в HelpWindow) |
| `UI/LlmScanHelper.UI.Windows/Controls/TextBoxHelpers.cs` | attached-поведение: коммит по Enter (TextBox / редактируемый ComboBox) |
| `UI/LlmSanHelper.UI.Windows/Controls/ToolTipLinker.cs` | кликабельные ссылки в тултипах (перехват «сквозного» клика) |
| `UI/LlmScanHelper.UI.Windows/Platform/WpfClipboard.cs`, `WpfUiWindows.cs`, `WpfFolderPicker.cs` | WPF-реализации абстракций из UI.Common |
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
