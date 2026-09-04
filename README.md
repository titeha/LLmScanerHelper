# LLM Scan Helper v4 (WPF / MVVM)

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
dotnet run -c Release
```

Готовый exe: `bin\Release\net10.0-windows\LLMScanHelper.exe`.

## Открытие в IDE

- **Visual Studio 2022** (17.12+, рабочая нагрузка «Разработка классических приложений .NET»):
  откройте `LLMScanHelper.sln` (или папку через «Открыть папку») → `F5` — сборка,
  отладка и XAML-редактор работают из коробки.
- **VS Code**: установите .NET 10 SDK и расширение **C# Dev Kit**
  (VS Code сам предложит его через `.vscode/extensions.json`).
  Открыть папку проекта → `Ctrl+Shift+B` (сборка), `F5` (запуск с отладкой).
  Визуального XAML-дизайнера нет — только подсветка и подсказки.

## Что где

| Файл | Назначение |
|---|---|
| `Views/MainWindow.xaml` | каркас окна: панель (PanelTabView) + кнопки в заголовке (Настройки/Справка) |
| `Views/PanelTabView.xaml` | основной UserControl на главном окне: параметры, инфо, команда |
| `Views/SettingsWindow.xaml` | отдельное окно «Настройки» (каталоги моделей), открывается из заголовка |
| `Views/HelpWindow.xaml` | отдельное окно «Справка» (памятка «Почему так»), открывается из заголовка |
| `Views/MemoTabView.xaml` | UserControl с памяткой «ПОЧЕМУ ТАК» (используется в HelpWindow) |
| `ViewModels/MainViewModel.cs` | ядро: состояние, параметры, команды, сканирование |
| `ViewModels/MainViewModel.Model.cs` | инфо о модели, загрузка, мультимодальность (mmproj) |
| `ViewModels/MainViewModel.MtpReasoning.cs` | сервер (хост/порт), MTP, reasoning, jinja |
| `ViewModels/MainViewModel.Gpu.cs` | GPU layout и опрос устройств |
| `ViewModels/MainViewModel.Presets.cs` | пресеты |
| `ViewModels/MainViewModel.Catalogs.cs` | корневые каталоги моделей |
| `ViewModels/MainViewModel.Persistence.cs` | сохранение/загрузка настроек |
| `ViewModels/MainViewModel.Output.cs` | сборка команды, предупреждения, оценка слоёв, буфер обмена |
| `Models/GgufInfo.cs` | парсер GGUF (архитектура, блоки, KV, MTP/nextn, reasoning, tools) |
| `Models/GgufScannerService.cs` | обход дерева моделей, издатель, поиск mmproj |
| `Models/GpuService.cs` | `llama-server --list-devices` (парсинг CUDA-id и свободной VRAM) |
| `Models/LayerEstimator.cs` | грубая оценка раскладки блоков (веса+KV) по картам |
| `Models/Settings/SettingsStore.cs` | JSON-хранилище (portable) |
| `Texts/memo.md` | памятка «ПОЧЕМУ ТАК» (Markdown, вкладка «Памятка»; парсер — позже) |
| `Texts/ToolTips.cs` | popup-подсказки по всем параметрам (зачем/влияет/дока) |
| `Controls/TextBoxHelpers.cs` | attached-поведение: коммит по Enter (TextBox / редактируемый ComboBox) |
| `Controls/ToolTipLinker.cs` | кликабельные ссылки в тултипах (перехват «сквозного» клика) |
| `Assets/app.ico` / `app-icon.png` | иконка приложения (exe и окно) |

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
