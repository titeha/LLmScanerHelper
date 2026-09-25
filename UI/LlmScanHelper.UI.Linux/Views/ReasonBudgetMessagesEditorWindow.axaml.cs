using Avalonia.Controls;
using LlmScanHelper.ViewModels;

namespace LlmScanHelper.Views;

public partial class ReasonBudgetMessagesEditorWindow : Window
{
  public ReasonBudgetMessagesEditorWindow(MainViewModel parentVm)
  {
    InitializeComponent();
    DataContext = new ReasonBudgetMessagesEditorViewModel(parentVm);
  }
}
