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
            if (d is not TextBox tb) return;

            if ((bool)e.NewValue)
            {
                tb.PreviewKeyDown += OnPreviewKeyDown;
            }
            else
            {
                tb.PreviewKeyDown -= OnPreviewKeyDown;
            }
        }

        private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || sender is not TextBox tb) return;
            e.Handled = true;
            tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }
    }
}
