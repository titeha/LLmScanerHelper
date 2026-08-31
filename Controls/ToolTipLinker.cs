using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;

namespace LlmScanHelper.Controls
{
  /// <summary>
  /// Превращает строку тултипа в TextBlock, где URL-фрагменты становятся
  /// кликабельными ссылками (открываются в браузере по умолчанию).
  /// </summary>
  public sealed class ToolTipLinker : IValueConverter
  {
    private static readonly Regex Url = new(@"https?://[^\s)""'<>]+", RegexOptions.Compiled);

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      if (value is not string text || text.Length == 0)
        return new TextBlock();

      var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 520 };
      var lines = text.Split('\n');
      for (int li = 0; li < lines.Length; li++)
      {
        string line = lines[li];
        int pos = 0;
        foreach (Match m in Url.Matches(line))
        {
          if (m.Index > pos)
            tb.Inlines.Add(new Run(line.Substring(pos, m.Index - pos)));
          var link = new Hyperlink(new Run(m.Value)) { NavigateUri = new Uri(m.Value) };
          link.RequestNavigate += (_, e) =>
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
          tb.Inlines.Add(link);
          pos = m.Index + m.Length;
        }
        if (pos < line.Length)
          tb.Inlines.Add(new Run(line.Substring(pos)));
        if (li + 1 < lines.Length)
          tb.Inlines.Add(new LineBreak());
      }
      return tb;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
      => throw new NotSupportedException();
  }
}
