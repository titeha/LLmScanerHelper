using System.IO;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;

namespace LlmScanHelper.Views;

public partial class MemoTabView : UserControl
{
  public MemoTabView()
  {
    InitializeComponent();

    // memo.md живёт в UI.Common; на Avalonia нет pack:// — читаем asset по avares://LLMScanHelper/Texts/memo.md
    var s = AssetLoader.Open(new System.Uri("avares://LLMScanHelper/Texts/memo.md"));
    try
    {
      if (MemoTextBlock is not null)
        MemoTextBlock.Text = new StreamReader(s).ReadToEnd();
    }
    finally { s.Dispose(); }
  }
}
