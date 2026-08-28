using System.Diagnostics;
using System.Text.RegularExpressions;

namespace LlmScanHelper.Models
{
  /// <summary>Опрос GPU через llama-server --list-devices (как в LINQPad-версии).</summary>
  public static class GpuService
  {
    private static readonly Regex DeviceLine = new(
      @"^\s*(CUDA\d+):\s*(.+?)\s*\((\d+)\s+MiB,\s*(\d+)\s+MiB\s+free\)\s*$",
      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static Task<GpuQueryResult> QueryAsync() => Task.Run(Query);

    public static GpuQueryResult Query()
    {
      try
      {
        var psi = new ProcessStartInfo
        {
          FileName = "llama-server",
          Arguments = "--list-devices",
          UseShellExecute = false,
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          CreateNoWindow = true
        };

        using var p = Process.Start(psi);
        if (p == null)
          return new GpuQueryResult { Ok = false, Message = "не удалось запустить llama-server" };

        string output = p.StandardOutput.ReadToEnd() + "\n" + p.StandardError.ReadToEnd();
        p.WaitForExit(15000);

        var gpus = new List<GpuDeviceInfo>();
        foreach (string raw in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
          var m = DeviceLine.Match(raw);
          if (!m.Success) continue;

          gpus.Add(new GpuDeviceInfo
          {
            Id = m.Groups[1].Value,
            Name = m.Groups[2].Value.Trim(),
            TotalMiB = int.Parse(m.Groups[3].Value),
            FreeMiB = int.Parse(m.Groups[4].Value)
          });
        }

        if (gpus.Count == 0)
          return new GpuQueryResult { Ok = false, Message = output.Trim() };

        return new GpuQueryResult { Ok = true, Devices = gpus };
      }
      catch (Exception ex)
      {
        return new GpuQueryResult { Ok = false, Message = ex.Message };
      }
    }
  }
}
