using System.Collections.ObjectModel;
using LlmScanHelper.Models;
using LlmScanHelper.Texts;

namespace LlmScanHelper.ViewModels
{
  /// <summary>
  /// Частичный класс: сервер (хост/порт), MTP, reasoning, jinja.
  /// </summary>
  public sealed partial class MainViewModel
  {
    // ==================== MTP / reasoning / server ====================

    private string _host = AppDefaults.DefaultHost;
    public string Host { get => _host; set { if (Set(ref _host, value)) SaveSoon(); } }

    private int _port = AppDefaults.DefaultPort;
    public int Port { get => _port; set { if (Set(ref _port, value)) SaveSoon(); } }

    private bool _mtpAvailable;
    public bool MtpAvailable
    {
      get => _mtpAvailable;
      private set
      {
        if (Set(ref _mtpAvailable, value))
        {
          OnPropertyChanged(nameof(MtpLockReason));
          OnPropertyChanged(nameof(MtpToolTip));
          OnPropertyChanged(nameof(IsMtpControlsEnabled));
        }
      }
    }

    private bool _mtpChecked;
    public bool MtpChecked
    {
      get => _mtpChecked;
      set
      {
        if (Set(ref _mtpChecked, value))
        {
          OnPropertyChanged(nameof(IsMtpControlsEnabled));
          SyncDraftRange();
          SaveSoon();
        }
      }
    }

    /// <summary>Поля draft-группы активны только при включённом и доступном MTP.</summary>
    public bool IsMtpControlsEnabled => MtpAvailable && MtpChecked;

    /// <summary>Подсказка, почему MTP недоступен (пустая — доступен).</summary>
    public string MtpLockReason =>
      !MtpAvailable
        ? (_gguf == null ? "Модель не загружена" : "В модели нет MTP/nextn-тензоров")
        : "";

    /// <summary>Полный tooltip для MTP: документация + причина блокировки, если есть.</summary>
    public string MtpToolTip =>
      MtpAvailable ? ToolTips.Mtp : ToolTips.Mtp + "\n\nСейчас недоступно: " + MtpLockReason;

    private int _draftMax = 3;
    public int DraftMax
    {
      get => _draftMax;
      set
      {
        value = Math.Clamp(value, 1, 16);
        if (Set(ref _draftMax, value))
        {
          if (DraftMin > _draftMax)
            DraftMin = _draftMax; // min никогда не > max
          OnPropertyChanged(nameof(DraftMin));
          SaveSoon();
        }
      }
    }

    private int _draftMin = 0;
    public int DraftMin
    {
      get => _draftMin;
      set { value = Math.Clamp(value, 0, DraftMax); if (Set(ref _draftMin, value)) SaveSoon(); }
    }

    private double _draftP = 0;
    public double DraftP { get => _draftP; set { if (Set(ref _draftP, value)) SaveSoon(); } }

    private string _draftK = "q8_0";
    public string DraftK { get => _draftK; set { if (Set(ref _draftK, value)) SaveSoon(); } }

    private string _draftV = "q8_0";
    public string DraftV { get => _draftV; set { if (Set(ref _draftV, value)) SaveSoon(); } }

    private bool _reasoningAvailable;
    public bool ReasoningAvailable { get => _reasoningAvailable; private set => Set(ref _reasoningAvailable, value); }

    // ТЗ2: режим рассуждений on/off/auto (ранее bool «включить»).
    private string _reasoningMode = AppDefaults.DefaultReasoningMode;
    public string ReasoningMode
    {
      get => _reasoningMode;
      set
      {
        if (Set(ref _reasoningMode, value))
        {
          OnPropertyChanged(nameof(IsReasoningControlsEnabled));
          SaveSoon();
        }
      }
    }

    /// <summary>Поля бюджета/сообщения активны при on и auto (не off).</summary>
    public bool IsReasoningControlsEnabled => ReasoningMode != "off";

    private int _reasonBudget = 4096;
    public int ReasonBudget
    {
      get => _reasonBudget;
      set { value = Math.Clamp(value, 0, 1_000_000); if (Set(ref _reasonBudget, value)) SaveSoon(); }
    }

    // ТЗ1: общий список сообщений бюджета (settings.json, раздел ReasonBudgetMessages).
    // Переиспользуется между моделями; ItemsSource редактируемого ComboBox.
    public ObservableCollection<string> ReasonBudgetMessages { get; } = [];

    private bool _suppressReasonMsgEdit;  // программная установка (загрузка профиля)

    private string _reasonBudgetMessage = AppDefaults.DefaultReasonBudgetMessage;
    public string ReasonBudgetMessage
    {
      get => _reasonBudgetMessage;
      set
      {
        if (!Set(ref _reasonBudgetMessage, value))
          return;
        if (_suppressReasonMsgEdit)
          return;
        // Ручная правка/выбор: новое значение уходит в общий список (без дублей).
        AddReasonBudgetMessage(value);
        SaveSoon();
      }
    }

    private void AddReasonBudgetMessage(string? value)
    {
      var v = value?.Trim() ?? "";
      if (v.Length == 0)
        return;
      if (!ReasonBudgetMessages.Contains(v))
        ReasonBudgetMessages.Add(v);
    }

    // --jinja: родной chat-шаблон GGUF (нужен для tool-calls/функций в OpenAI API).
    // По умолчанию включается, если сканер нашёл в шаблоне обработку tools;
    // ручное переключение фиксируется (JinjaEdited) и авто-детектом больше не трогается.
    private bool _suppressJinjaEdit;                  // программная установка флага
    private bool _jinjaChecked;
    public bool JinjaChecked
    {
      get => _jinjaChecked;
      set
      {
        if (Set(ref _jinjaChecked, value))
        {
          if (!_suppressJinjaEdit && _currentPath != null)
          {
            var ms = _store.GetOrCreateModel(_currentPath);
            ms.UseJinja = value;
            ms.JinjaEdited = true;
          }
          SaveSoon();
        }
      }
    }

    private bool _jinjaAvailable;
    public bool JinjaAvailable { get => _jinjaAvailable; private set => Set(ref _jinjaAvailable, value); }

    private void SyncDraftRange()
    {
      // DraftMin max привязан к DraftMax в UI; здесь только страховка значения
      if (DraftMin > DraftMax)
        DraftMin = DraftMax;
    }
  }
}
