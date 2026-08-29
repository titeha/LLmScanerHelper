namespace LlmScanHelper.Models
{
  /// <summary>
  /// Константы по умолчанию (перенесены из LINQPad-версии 1:1, где возможно).
  /// </summary>
  public static class AppDefaults
  {
    public const string ModelsRoot = @"W:\LLStudio\Models";
    public const string DefaultAlias = "";
    public const int DefaultContext = 32768;
    public const int DefaultPort = 12345;
    public const string DefaultHost = "127.0.0.1";
    public const string DefaultDevices = "CUDA0,CUDA1";

    // Пользовательские резервы задаём в GiB; в команду --fit-target они
    // переводятся в MiB. Desktop RTX намеренно защищаем минимум 2 GiB.
    public const double SafeReserveV100GiB = 0.50;
    public const double SafeReserveRtxGiB = 3.00;
    public const double BalancedReserveRtxGiB = 2.50;
    public const double AggressiveReserveRtxGiB = 2.00;
    public const double MinDesktopReserveGiB = 2.00;

    public const int DefaultBatch = 2048;
    public const int DefaultUBatch = 512;
    public const int DefaultSlots = 1;
    public const int DefaultCacheReuse = 0;
    public const int DefaultSsePing = 15;
    public const int DefaultTimeout = 7200;

    // Sampling-параметры (дефолты llama-server; см. подсказки в UI)
    public const bool DefaultSamplingEnabled = false;
    public const double DefaultTemp = 0.80;
    public const int DefaultTopK = 40;
    public const double DefaultTopP = 0.95;
    public const double DefaultMinP = 0.05;
    public const double DefaultRepeatPenalty = 1.00;
    public const int DefaultRepeatLastN = 64;
    public const double DefaultPresencePenalty = 0.00;
    public const double DefaultFrequencyPenalty = 0.00;
    public const int DefaultSeed = -1;

    // Ограничения парсера GGUF (защита от битых/злонамеренных файлов)
    public const ulong MaxTensorCount = 10_000_000;
    public const ulong MaxKvCount = 1_000_000;
    public const ulong MaxStringLen = 10_000_000;
    public const ulong MaxArrayLen = 1_000_000;

    public const string ServerReadmeUrl =
      "https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md";
    public const string MtmdReadmeUrl =
      "https://github.com/ggml-org/llama.cpp/blob/master/tools/mtmd/README.md";
  }
}
