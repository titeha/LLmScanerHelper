using LlmScanHelper.Models;

namespace LlmScanHelper.Texts
{
  /// <summary>
  /// Popup-подсказки (ToolTip) для элементов управления:
  /// зачем параметр, на что влияет, дефолт и ссылка на документацию.
  /// </summary>
  public static class ToolTips
  {
    private const string ServerDoc = "Дока: " + AppDefaults.ServerReadmeUrl;

    public const string ModelCombo =
      "Выбор модели. При смене модели подтягивается её сохранённый профиль из settings.json\n" +
      "(контекст, KV, MTP, reasoning, mmproj, алиас).";

    public const string ScanButton =
      "Перечитать дерево моделей. Параметры не сбрасываются — они восстанавливаются из хранилища.";

    public const string RefreshGpu =
      "Опросить llama-server --list-devices: свободная VRAM, авто-подстановка CUDA0/CUDA1...\n" +
      "Нужна для fit-target и оценки распределения слоёв. llama-server должен быть в PATH.\n" + ServerDoc;

    public const string Alias =
      "--alias: имя модели, которое видит API-клиент (model=\"...\").\n" +
      "Влияние: только на идентификацию в API, на скорость/память не влияет.\n" +
      "Регистр сохраняется как в имени файла (GPT, UD и аббревиатуры не трогаются).\n" +
      "Изменённый вручную алиас запоминается для модели и не перегенерируется.\n" + ServerDoc;

    public const string ToolsDetect =
      "Эвристика «умеет ли модель tool-calls» (агентная работа) по GGUF:\n" +
      "1) скан всех ключей tokenizer.chat_template* — есть ли в jinja-шаблоне\n" +
      "   обработка tools / tool_calls / role==tool;\n" +
      "2) словарь спец-токенов — есть ли токены вида <tool_call>, [TOOL_CALLS].\n" +
      "«да» — шаблон умеет инструменты: для агентов держи --jinja включённым.\n" +
      "«нет» — шаблон есть, но tools там нет: функции через этот шаблон не заведутся.\n" +
      "«неизвестно» — шаблона в GGUF нет: llama-server подберёт встроенный по семейству.\n" +
      "Это прокси-оценка: финальное поведение зависит ещё от сборки llama.cpp\n" +
      "и от того, обучалась ли модель вызывать функции.\n" + ServerDoc;

    public const string JinjaFlag =
      "--jinja: использовать chat-шаблон из GGUF вместо встроенных эвристик сервера.\n" +
      "Зачем: именно с --jinja llama-server передаёт модели tools из OpenAI-запроса\n" +
      "и парсит её ответ в tool_calls (агентная работа, функции, MCP-клиенты).\n" +
      "Без шаблона в GGUF сервер подберёт приближение по семейству модели.\n" +
      "Включается автоматически, если сканер нашёл в шаблоне обработку tools;\n" +
      "ручное переключение запоминается в профиле модели.\n" + ServerDoc;

    public const string Context =
      "Контекст окна, токенов (-c).\n" +
      "Влияет: объём KV-кэша, RS-cache и scratch — чем больше, тем больше VRAM.\n" +
      "Дефолт UI: 32768. Для агента сначала 32k; 64k только когда реально нужно.\n" + ServerDoc;

    public const string KvK =
      "--cache-type-k: тип квантования K-кэша.\n" +
      "q8_0 — рабочий компромисс; q4_0 экономит память, но на длинном контексте\n" +
      "может сильнее влиять на качество. С FlashAttention нужна сборка с FA-квант-кernels.\n" + ServerDoc;

    public const string KvV =
      "--cache-type-v: тип квантования V-кэша. См. подсказку KV K.\n" + ServerDoc;

    public const string Flash =
      "-fa auto|on|off — FlashAttention.\n" +
      "auto — решает runtime; on — принудительно; с квантованным KV проверяй лог.\n" +
      "Кастомная сборка: GGML_CUDA_FA_ALL_QUANTS=ON.\n" + ServerDoc;

    public const string Mode =
      "AUTO — llama.cpp сам раскладывает модель (--fit on), точный -ngl не задаётся.\n" +
      "MANUAL — экспертный: --fit off, явный -ngl и --tensor-split (пропорции!).\n" +
      "Для новой модели начинай с AUTO + профиль V100 ONLY.\n" + ServerDoc;

    public const string Devices =
      "--device: список устройств в порядке передачи --fit-target.\n" +
      "Кнопка «Обновить GPU» подставляет реальные CUDA-id автоматически.";

    public const string SplitMode =
      "--split-mode layer|row|tensor|none (MANUAL-режим).\n" +
      "layer — правильный дефолт для V100+RTX на разных PCIe; tensor — экспериментальный.\n" + ServerDoc;

    public const string ReserveV100 =
      "Целевой запас свободной VRAM на V100 ПОСЛЕ загрузки (--fit-target), GiB.\n" +
      "V100 без Windows/WDDM: достаточно 0.50 GiB технического запаса.";

    public const string ReserveRtx =
      "Целевой запас свободной VRAM на desktop RTX ПОСЛЕ загрузки (--fit-target), GiB.\n" +
      "На RTX живёт WDDM/рабочий стол: SAFE=3.00, BALANCED=2.50, AGGRESSIVE=2.00.\n" +
      "Ниже 2.00 GiB UI намеренно не даёт поставить. Это НЕ hard-cap — не открывай\n" +
      "новые GPU-приложения во время работы модели.";

    public const string ManualNgl =
      "-ngl: сколько блоков (слоёв) выгружать на GPU, MANUAL-режим.\n" +
      "В AUTO не трогается — раскладку делает --fit. OOM здесь — ответственность ручной раскладки.\n" + ServerDoc;

    public const string TensorSplit =
      "--tensor-split X,Y: ПРОПОРЦИИ распределения offload между устройствами, НЕ слои.\n" + ServerDoc;

    public const string Batch =
      "-b: логический batch (в основном prompt processing).\n" +
      "Дефолт 2048. Больше batch — крупнее шаг prefill, больше scratch.\n" + ServerDoc;

    public const string UBatch =
      "-ub: физический кусок batch (uvirtual batch), влияет на scratch и скорость prefill.\n" +
      "Дефолт 512. Для V100 полезно БЕНЧМАРКОМ проверить 64/128/256/512/1024.\n" +
      "Не должен быть больше -b.\n" + ServerDoc;

    public const string Slots =
      "-np: число параллельных слотов (одновременных запросов).\n" +
      "Для одного coding-agent быстрее/предсказуемее 1: параллельность делит\n" +
      "ресурсы и контекст между слотами.\n" + ServerDoc;

    public const string Threads =
      "-t: CPU-потоки генерации (0 = авто). Речь про CPU-часть вычислений (offload/общие слои).\n" + ServerDoc;

    public const string ThreadsBatch =
      "-tb: CPU-потоки prompt processing (0 = авто).\n" + ServerDoc;

    public const string PromptCache =
      "--cache-prompt / --no-cache-prompt: кэширование промпта между запросами.\n" +
      "Сервер по умолчанию кэширует. Для чистого benchmark можно отключить.\n" + ServerDoc;

    public const string CacheReuse =
      "--cache-reuse N: переиспользование кусков кэша (минимальный размер блока).\n" +
      "Помогает агентам на повторяющихся длинных префиксах; для ЧИСТОГО benchmark держи 0.\n" + ServerDoc;

    public const string SsePing =
      "--sse-ping-interval N (сек): пинг SSE-потока, чтобы соединение не висело немым\n" +
      "во время долгого prefill. Рекомендуется 10–15 с.\n" + ServerDoc;

    public const string Timeout =
      "--timeout N (сек): HTTP-таймаут сервера.\n" +
      "Если агент имеет свой жёсткий timeout, его тоже надо увеличить в настройках агента.\n" + ServerDoc;

    public const string Perf =
      "--perf: вывод времени prompt/eval в ответе (полезно для замеров).";

    public const string Host =
      "--host: адрес, который слушает llama-server. 127.0.0.1 — только локально.\n" + ServerDoc;

    public const string Port =
      "--port: порт llama-server (не занимай его другими сервисами).\n" + ServerDoc;

    public const string Mtp =
      "--spec-type draft-mtp: мульти-токен предикшн (спекулятивный декодинг через MTP-блок модели).\n" +
      "Ускоряет decode при хорошем acceptance, но расходует память и требует свободной VRAM.\n" +
      "Дефолты llama.cpp: draft max=3, draft min=0; min не больше max.\n" +
      "На пограничной по VRAM модели сначала добейся стабильной работы БЕЗ MTP.\n" + ServerDoc;

    public const string DraftMax =
      "--spec-draft-n-max: максимум черновых токенов за шаг MTP (дефолт 3).";

    public const string DraftMin =
      "--spec-draft-n-min: минимум черновых токенов (дефолт 0). Никогда не больше draft max.";

    public const string DraftP =
      "--spec-draft-p-min: порог вероятности принятия чернового токена. 0 = не передавать флаг.";

    public const string DraftKv =
      "Тип квантования KV-кэша спекулятивного (draft) контекста MTP.\n" + ServerDoc;

    public const string Reasoning =
      "--reasoning on|off|auto: режим рассуждений (для thinking-моделей).\n" +
      "auto — runtime решает сам по чат-шаблону (дефолт); on — всегда включено; off — всегда выкл.\n" +
      "Бюджет и сообщение доступны при on и auto (при off не передаются).\n" +
      "Детектится по чат-шаблону (enable_thinking/reasoning) и тегам GGUF.\n" + ServerDoc;

    public const string ReasonBudgetMessage =
      "--reasoning-budget-message: сообщение про исчерпание бюджета рассуждений.\n" +
      "Передаётся при непустом значении. Выбор из общего списка (все модели)\n" +
      "или ввод нового текста (добавится в общий список). Дефолт — стандартный текст;\n" +
      "при очищенном поле флаг не передаётся (дефолт runtime — none).";

    public const string ReasonBudget =
      "Бюджет reasoning-токенов (флаг задаётся константой ReasoningBudgetFlag).\n" +
      "0 — флаг не передаётся (runtime — без лимита), но при режиме on берётся минимальный\n" +
      "бюджет 1024 (DefaultReasonBudgetMinimum).\n" +
      "Рекомендация: 4–8k в рутине, 16–32k в сложном коде.\n" +
      "Дока: " + ServerDoc;

    public const string MmprojEnabled =
      "--mmproj: подключить мультимодальный проектор (картинки/видео -> модель).\n" +
      "Файлы mmproj*.gguf ищутся только в папке модели.\n" +
      "Влияние: проектор загружается дополнительно (см. его размер рядом).\n" +
      "Дока: " + AppDefaults.MtmdReadmeUrl;

    public const string MmprojCombo =
      "Выбор mmproj-файла. Ищутся только в папке модели.\n" +
      "Дока: " + AppDefaults.MtmdReadmeUrl;

    public const string SamplingEnabled =
      "Общий выключатель: передавать ли sampling-параметры в команду llama-server.\n" +
      "Это ДЕФОЛТЫ СЕРВЕРА — их можно переопределять в каждом API-запросе.\n" +
      "Для чистого benchmark держи выключенным (runtime использует свои дефолты).\n" + ServerDoc;

    public const string Temp =
      "--temp: температура сэмплинга. Выше — креативнее/случайнее, ниже — детерминированнее.\n" +
      "0 = жадный выбор. Для кода часто 0.0–0.6; дефолт llama-server 0.8.\n" + ServerDoc;

    public const string TopK =
      "--top-k: ограничение кандидатов K самыми вероятными токенами. 0 = отключено.\n" + ServerDoc;

    public const string TopP =
      "--top-p: nucleus sampling — отсечение хвоста распределения по накопленной вероятности.\n" + ServerDoc;

    public const string MinP =
      "--min-p: отсечение токенов ниже p * max_probability. Мягкая альтернатива top-p.\n" + ServerDoc;

    public const string RepeatPenalty =
      "--repeat-penalty: штраф за повторение уже сгенерированных токенов.\n" +
      "1.0 = выключен. Осторожно: слишком высокий ломает код и URL.\n" + ServerDoc;

    public const string RepeatLastN =
      "--repeat-last-n: сколько последних токенов учитывать штрафом повтора.\n" + ServerDoc;

    public const string PresencePenalty =
      "--presence-penalty: аддитивный штраф за факт появления токена. 0 = выключен.\n" + ServerDoc;

    public const string FrequencyPenalty =
      "--frequency-penalty: штраф, растущий с частотой токена. 0 = выключен.\n" + ServerDoc;

    public const string Seed =
      "--seed: зерно генератора. -1 = случайное. Фиксированный seed делает прогон воспроизводимым.\n" + ServerDoc;

    public const string BuildButton =
      "Собрать строку запуска llama-server из текущих параметров (и обновить предупреждения\n" +
      "и оценку распределения слоёв).";

    public const string CopyButton =
      "Скопировать строку запуска в буфер обмена (формат cmd; кавычки уже расставлены).";

    public const string LayerEstimate =
      "ГРУБАЯ оценка: блоки (веса + KV по выбранному типу) раскладываются по картам\n" +
      "по остатку свободной VRAM после fit-target. Не учитывает RS-cache, scratch,\n" +
      "speculative context. Точную раскладку делает llama.cpp через --fit.";

    public const string FitTargets =
      "Как будет выглядеть --fit-target в команде: свободная VRAM минус резервы,\n" +
      "в порядке --device. MiB считаются из GiB (1024^3).";

    public const string MtpInfoRow =
      "MTP (multi-token prediction): есть ли в модели встроенные слои доп. предсказания.\n" +
      "Тип: nextn / mtp / доп. блоки — как реализовано; +N токенов за шаг — сколько доп.\n" +
      "токенов модель умеет предсказать (по числу MTP-слоёв).";

    public const string MultimodalRow =
      "Мультимодальность: найден ли рядом с моделью файл-проектор mmproj*.gguf.\n" +
      "Включается в блоке «Мультимодальность (--mmproj)».";

    public const string ReasoningRow =
      "Рассуждения: есть ли поддержка режима рассуждений (в chat-шаблоне найдено\n" +
      "enable_thinking/reasoning или тег reasoning). Включается чекбоксом «reasoning ВКЛ».";

    public const string CatalogCombo =
      "Список корневых каталогов с моделями. Каталог добавляется, удаляется или редактируется кнопками ниже.";

    public const string AddCatalog =
      "Добавить новый корневой каталог с моделями (выбор папки).";

    public const string RemoveCatalog =
      "Удалить выбранный каталог. Последний каталог удалить нельзя.";

    public const string EditCatalog =
      "Изменить путь выбранного каталога (выбор папки).";
  }
}
