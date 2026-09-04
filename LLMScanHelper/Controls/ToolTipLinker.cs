using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;

namespace LlmScanHelper.Controls
{
  /// <summary>
  /// Превращает строку тултипа в TextBlock, где URL-фрагменты становятся
  /// кликабельными ссылками (открываются в браузере по умолчанию).
  /// </summary>
  /// <remarks>
  /// WPF-тултип мышь НЕ принимает: его попап создаётся «клики насквозь»
  /// (в исходниках WPF: Popup.HitTestable = !StaysOpen, а у тултипа
  /// StaysOpen по умолчанию включён). Клик проходит сквозь тултип в то,
  /// что ПОД ним. Поэтому ссылки открываем глобальным туннельным
  /// обработчиком: проверяем, не накрыт ли курсор одним из открытых
  /// тултипов и не ссылка ли под курсором.
  /// </remarks>
  public sealed class ToolTipLinker : IValueConverter
  {
    private static readonly Regex Url = new(@"https?://[^\s)""'<>]+", RegexOptions.Compiled);

    // Содержимое тултипа получает Loaded при открытии попапа и Unloaded
    // при закрытии — так мы знаем, какие тултипы открыты прямо сейчас.
    private static readonly HashSet<TextBlock> OpenTips = new();

    static ToolTipLinker()
    {
      // Без handledEventsToo: класс-обработчик вызывается на каждом узле
      // маршрута (а узлы здесь — все элементы, все они UIElement). Когда
      // клик по ссылке помечен обработанным, дальнейшие узлы не должны
      // открывать браузер повторно.
      EventManager.RegisterClassHandler(
        typeof(UIElement),
        UIElement.PreviewMouseLeftButtonDownEvent,
        new MouseButtonEventHandler(OnPreviewMouseLeftButtonDown));
    }

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      if (value is not string text || text.Length == 0)
        return new TextBlock();

      var tb = new TextBlock
      {
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 520,
        Background = System.Windows.Media.Brushes.Transparent // hit-test по всей области
      };

      var lines = text.Split('\n');
      for (int li = 0; li < lines.Length; li++)
      {
        string line = lines[li];
        int pos = 0;
        foreach (Match m in Url.Matches(line))
        {
          if (m.Index > pos)
            tb.Inlines.Add(new Run(line.Substring(pos, m.Index - pos)));
          tb.Inlines.Add(new Hyperlink(new Run(m.Value)) { NavigateUri = new Uri(m.Value) });
          pos = m.Index + m.Length;
        }
        if (pos < line.Length)
          tb.Inlines.Add(new Run(line.Substring(pos)));
        if (li + 1 < lines.Length)
          tb.Inlines.Add(new LineBreak());
      }

      tb.Loaded += (_, _) => OpenTips.Add(tb);
      tb.Unloaded += (_, _) => OpenTips.Remove(tb);

      return tb;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
      => throw new NotSupportedException();

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
      if (OpenTips.Count == 0)
        return;

      foreach (var tb in OpenTips)
      {
        // Клик «сквозь» тултип: проверяем, попал ли курсор в его содержимое.
        Point p = Mouse.GetPosition(tb);
        if (p.X < 0 || p.Y < 0 || p.X > tb.ActualWidth || p.Y > tb.ActualHeight)
          continue;

        var link = LinkAt(tb, p);
        if (link == null)
          continue;

        Open(link.NavigateUri);
        // Гасим событие: и контролу под тултипом клик не достаётся, и
        // обработчик не вызывается повторно на узлах ниже по маршруту.
        e.Handled = true;
        return;
      }
    }

    // TextPointer.Parent — это Inline (Run/Hyperlink) или сам TextBlock;
    // идём вверх по цепочке, пока не найдём гиперссылку.
    private static Hyperlink? LinkAt(TextBlock tb, Point point)
    {
      var inline = tb.GetPositionFromPoint(point, snapToText: false).Parent as Inline;
      while (inline != null)
      {
        if (inline is Hyperlink { NavigateUri: not null } h)
          return h;
        inline = inline.Parent as Inline;
      }
      return null;
    }

    private static void Open(Uri uri) =>
      Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
  }
}
