using System.Windows;
using LlmScanHelper.ViewModels;
using MahApps.Metro.Controls;

namespace LlmScanHelper.Views
{
  public partial class SettingsWindow : MetroWindow
  {
    public SettingsWindow(MainViewModel vm)
    {
      InitializeComponent();
      DataContext = vm;
    }

    // Вернём фокус на главное окно после закрытия модального окна.
    protected override void OnClosed(EventArgs e)
    {
      if (Owner is Window owner)
        owner.Focus();
      base.OnClosed(e);
    }
  }
}
