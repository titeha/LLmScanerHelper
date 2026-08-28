using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Globalization;

namespace LlmScanHelper.Controls
{
  /// <summary>
  /// Числовой контрол (замена WinForms NumericUpDown): [−] [поле] [+].
  ///  • проверка ПОСЛЕ ввода (Enter или потеря фокуса) — курсор при печати не дёргается;
  ///  • чушь/вылет за границы -> ближайшее безопасное число;
  ///  • кнопки с авто-повтором при удержании;
  ///  • запятая и точка в десятичных считаются одинаково.
  /// MVVM: двусторонняя привязка к Value.
  /// </summary>
  public class NumberBox : UserControl
  {
    public static readonly DependencyProperty ValueProperty =
      DependencyProperty.Register(nameof(Value), typeof(double), typeof(NumberBox),
        new FrameworkPropertyMetadata(0.0,
          FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
          OnValueChanged, CoerceValue));

    public static readonly DependencyProperty MinimumProperty =
      DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(NumberBox),
        new FrameworkPropertyMetadata(0.0, OnLimitsChanged));

    public static readonly DependencyProperty MaximumProperty =
      DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(NumberBox),
        new FrameworkPropertyMetadata(100.0, OnLimitsChanged));

    public static readonly DependencyProperty IncrementProperty =
      DependencyProperty.Register(nameof(Increment), typeof(double), typeof(NumberBox),
        new FrameworkPropertyMetadata(1.0));

    public static readonly DependencyProperty DecimalsProperty =
      DependencyProperty.Register(nameof(Decimals), typeof(int), typeof(NumberBox),
        new FrameworkPropertyMetadata(0));

    public double Value
    {
      get => (double)GetValue(ValueProperty);
      set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
      get => (double)GetValue(MinimumProperty);
      set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
      get => (double)GetValue(MaximumProperty);
      set => SetValue(MaximumProperty, value);
    }

    public double Increment
    {
      get => (double)GetValue(IncrementProperty);
      set => SetValue(IncrementProperty, value);
    }

    public int Decimals
    {
      get => (int)GetValue(DecimalsProperty);
      set => SetValue(DecimalsProperty, value);
    }

    private TextBox? _textBox;
    private bool _editing; // коммит из поля -> DP, чтобы не переписывать текст во время правки

    public NumberBox()
    {
      BuildUi();
      Loaded += (_, _) => RefreshText();
    }

    private void BuildUi()
    {
      var grid = new Grid();
      grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
      grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
      grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });

      var minus = new RepeatButton { Content = "−", Focusable = false, Padding = new Thickness(0, -3, 0, 0) };
      Grid.SetColumn(minus, 0);
      minus.Click += (_, _) => Step(-Increment);

      _textBox = new TextBox
      {
        VerticalContentAlignment = VerticalAlignment.Center,
        Padding = new Thickness(3, 0, 3, 0)
      };
      Grid.SetColumn(_textBox, 1);
      _textBox.LostFocus += (_, _) => CommitText();
      _textBox.KeyDown += (_, e) =>
      {
        if (e.Key == Key.Enter)
        {
          e.Handled = true;
          CommitText();
          // убираем фокус с поля: коммит зафиксирован визуально
          _textBox?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }
      };

      var plus = new RepeatButton { Content = "+", Focusable = false, Padding = new Thickness(0, -3, 0, 0) };
      Grid.SetColumn(plus, 2);
      plus.Click += (_, _) => Step(Increment);

      grid.Children.Add(minus);
      grid.Children.Add(_textBox);
      grid.Children.Add(plus);

      Content = grid;
    }

    private void Step(double delta)
    {
      CommitText();
      double v = Value + delta;
      v = Math.Round(v / Math.Max(Increment, 1e-9)) * Math.Max(Increment, 1e-9); // кратность шагу
      Value = v;
      RefreshText();
    }

    private void CommitText()
    {
      if (_textBox == null) return;
      string raw = _textBox.Text.Trim().Replace(',', '.');

      if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
      {
        _editing = true;
        try { Value = v; }
        finally { _editing = false; }
      }
      RefreshText();
    }

    private void RefreshText()
    {
      if (_textBox == null || _editing) return;
      string fmt = Decimals > 0 ? "0." + new string('0', Decimals) : "0";
      _textBox.Text = Value.ToString(fmt, CultureInfo.InvariantCulture);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
      ((NumberBox)d).RefreshText();
    }

    private static void OnLimitsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
      var nb = (NumberBox)d;
      nb.CoerceValue(ValueProperty);
    }

    private static object CoerceValue(DependencyObject d, object baseValue)
    {
      var nb = (NumberBox)d;
      double v = (double)baseValue;
      return Math.Clamp(v, nb.Minimum, nb.Maximum);
    }
  }
}
