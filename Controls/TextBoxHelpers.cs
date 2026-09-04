using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LlmScanHelper.Controls
{
  /// <summary>
  /// Поведение TextBox: Enter фиксирует значение (передвигает фокус),
  ///LostFocus-биндинг дописывает коммит. Курсор при печати не дёргается.
  /// </summary>
  public static class TextBoxHelpers
  {
    public static readonly DependencyProperty CommitOnEnterProperty =
      DependencyProperty.RegisterAttached(
        "CommitOnEnter", typeof(bool), typeof(TextBoxHelpers),
        new FrameworkPropertyMetadata(false, OnCommitOnEnterChanged));

    public static bool GetCommitOnEnter(DependencyObject obj) => (bool)obj.GetValue(CommitOnEnterProperty);
    public static void SetCommitOnEnter(DependencyObject obj, bool value) => obj.SetValue(CommitOnEnterProperty, value);

    private static void OnCommitOnEnterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
      bool on = (bool)e.NewValue;

      if (d is TextBox tb)
      {
        if (on) tb.PreviewKeyDown += OnPreviewKeyDown;
        else tb.PreviewKeyDown -= OnPreviewKeyDown;
        return;
      }

      // Редактируемый ComboBox: внутренний TextBox недоступен напрямую,
      // ловим Enter на самом контроле и сдвигаем фокус (LostFocus-биндинг коммитит текст).
      if (d is ComboBox cb)
      {
        if (on) cb.PreviewKeyDown += OnComboPreviewKeyDown;
        else cb.PreviewKeyDown -= OnComboPreviewKeyDown;
      }
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
      if (e.Key != Key.Enter || sender is not TextBox tb) return;
      e.Handled = true;
      tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }

    private static void OnComboPreviewKeyDown(object sender, KeyEventArgs e)
    {
      if (e.Key != Key.Enter || sender is not ComboBox cb) return;
      // Даундроп открыт — даём стандартному поведению выбрать подсвеченный пункт.
      if (cb.IsDropDownOpen)
        return;
      e.Handled = true;
      cb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }
  }
}
