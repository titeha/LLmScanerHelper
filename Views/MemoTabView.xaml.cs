using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace LlmScanHelper.Views
{
  public partial class MemoTabView : UserControl
  {
    public MemoTabView()
    {
      InitializeComponent();

      var s = Application.GetResourceStream(new Uri("pack://application:,,,/Texts/memo.md"));
      if (s != null)
        MemoTextBlock.Text = new StreamReader(s.Stream).ReadToEnd();
    }
  }
}
