using System.Collections.ObjectModel;
using System.Windows.Input;
using MvvmUtilites;

namespace LlmScanHelper.ViewModels;

/// <summary>
/// ViewModel для редактора сообщений бюджета reasoning.
/// Позволяет добавлять, редактировать и удалять сообщения.
/// Важно: удаление применяется ко всем моделям (глобальный список).
/// </summary>
public sealed class ReasonBudgetMessagesEditorViewModel : ObservableObject
{
  private readonly MainViewModel _parentVm;
  private readonly ObservableCollection<string> _originalList;

  // Копия списка для редактирования (отмена изменений без влияния на оригинал)
  public ObservableCollection<string> Messages { get; } = new();

  private string? _selectedMessage;
  public string? SelectedMessage
  {
    get => _selectedMessage;
    set
    {
      if (Set(ref _selectedMessage, value))
      {
        OnPropertyChanged(nameof(HasSelectedMessage));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanEdit));
      }
    }
  }

  public bool HasSelectedMessage => SelectedMessage != null;
  public bool CanDelete => HasSelectedMessage;
  public bool CanEdit => HasSelectedMessage;

  private string _editText = "";
  public string EditText
  {
    get => _editText;
    set => Set(ref _editText, value);
  }

  private bool _isEditMode;
  public bool IsEditMode
  {
    get => _isEditMode;
    set
    {
      if (Set(ref _isEditMode, value))
      {
        OnPropertyChanged(nameof(IsAddMode));
        OnPropertyChanged(nameof(ShowEditControls));
      }
    }
  }

  public bool IsAddMode => !IsEditMode;
  public bool ShowEditControls => HasSelectedMessage && !IsEditMode;

  // Команды
  public ICommand AddCommand { get; }
  public ICommand EditCommand { get; }
  public ICommand DeleteCommand { get; }
  public ICommand SaveCommand { get; }
  public ICommand CancelCommand { get; }

  public ReasonBudgetMessagesEditorViewModel(MainViewModel parentVm)
  {
    _parentVm = parentVm;
    _originalList = parentVm.ReasonBudgetMessages;

    // Синхронизируем копию с текущим состоянием
    Messages.Clear();
    foreach (var msg in _originalList)
      Messages.Add(msg);

    AddCommand = new RelayCommand(OnAdd);
    EditCommand = new RelayCommand(OnEdit);
    DeleteCommand = new RelayCommand(OnDelete);
    SaveCommand = new RelayCommand(OnSave);
    CancelCommand = new RelayCommand(OnCancel);
  }

  private void OnAdd()
  {
    IsEditMode = true;
    EditText = "";
  }

  private void OnEdit()
  {
    if (SelectedMessage is null)
      return;
    IsEditMode = true;
    EditText = SelectedMessage;
  }

  private void OnDelete()
  {
    if (SelectedMessage is null)
      return;

    // Удаляем из копии
    Messages.Remove(SelectedMessage);

    // Удаляем из всех моделей (просто перезаписываем глобальный список в родителе)
    _parentVm.ReasonBudgetMessages.Clear();
    foreach (var msg in Messages)
      _parentVm.ReasonBudgetMessages.Add(msg);

    SelectedMessage = null;
    _parentVm.FlushPendingSave();
    _parentVm.SaveNow();
  }

  private void OnSave()
  {
    // Применяем изменения из копии в родительский ViewModel
    _parentVm.ReasonBudgetMessages.Clear();
    foreach (var msg in Messages)
      _parentVm.ReasonBudgetMessages.Add(msg);

    _parentVm.SaveNow();
    IsEditMode = false;
  }

  private void OnCancel()
  {
    IsEditMode = false;
    // Отмена: копия уже не влияет на оригинал (она копия)
    // Но нужно вернутьSelectedMessage в исходное состояние
    // (в данном случае просто обнуляем, так как отмена — это выход без изменений)
  }

  public void UpdateEditText(string value)
  {
    EditText = value;
  }

  public void CommitEditText()
  {
    var v = EditText.Trim();
    if (v.Length == 0)
      return;

    // Добавляем или обновляем в копии
    if (IsEditMode && SelectedMessage is not null)
    {
      // Редактирование существующего
      var idx = Messages.IndexOf(SelectedMessage);
      if (idx >= 0)
        Messages[idx] = v;
    }
    else
    {
      // Добавление нового
      if (!Messages.Contains(v))
        Messages.Add(v);
    }

    IsEditMode = false;
    EditText = "";
  }
}
