using System.Threading;

namespace LlmScanHelper.UI.Services;

/// <summary>Однократный таймер-дебаунс: Start/Stop; перезапуск обнуляет отсчёт.</summary>
public sealed class Debouncer
{
  private readonly int _intervalMs;
  private readonly Action _onFire;
  private Timer? _timer;

  public Debouncer(int intervalMs, Action onFire) { _intervalMs = intervalMs; _onFire = onFire; }

  public void Start()
  {
    Stop();
    // Timer требует TimerCallback — оборачиваем Action в лямбду.
    _timer = new Timer(_ => _onFire(), null, _intervalMs, Timeout.Infinite);
  }

  public void Stop()
  {
    _timer?.Dispose();
    _timer = null;
  }
}
