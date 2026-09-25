using System.Windows;
using LlmScanHelper.ViewModels;
using MvvmUtilites;

namespace LlmScanHelper.Views;

/// <summary>
/// Логика взаимодействия для ReasonBudgetMessagesEditorWindow.xaml
/// </summary>
public partial class ReasonBudgetMessagesEditorWindow : Window
{
  public ReasonBudgetMessagesEditorWindow(MainViewModel parentVm)
  {
    InitializeComponent();
    DataContext = new ReasonBudgetMessagesEditorViewModel(parentVm);
  }
}
