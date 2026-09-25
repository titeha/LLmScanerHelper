# deploy.ps1 — копирует готовые результаты сборки в папку деплоя.
#
# Путь назначения: файл deploy-target.txt РЯДОМ с этим скриптом (одна строка).
# Файл не коммитится: он исключён через .git/info/exclude (локально, не пушится).
#
# Использование (из корня репозитория):
#   .\deploy.ps1                  # скопировать оба результата: windows + linux-x64
#   .\deploy.ps1 -Target windows  # только Windows-сборку (WPF)
#   .\deploy.ps1 -Target linux    # только Linux-сборку (Avalonia, linux-x64)
#   .\deploy.ps1 -Clean           # перед копированием очистить содержимое целевых подпапок
#
# Итоговая структура:
#   <путь из deploy-target.txt>\windows\LLMScanHelper.exe   (+ dll)
#   <путь из deploy-target.txt>\linux-x64\LLMScanHelper     (+ dll)

param(
  [ValidateSet('windows', 'linux', 'all')]
  [string]$Target = 'all',
  [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$configFile = Join-Path $repoRoot 'deploy-target.txt'

# --- Путь деплоя: первая непустая строка deploy-target.txt, не начинающаяся с '#'
if (-not (Test-Path $configFile)) {
  throw "Не найден файл $configFile. Создайте его и впишите путь к папке деплоя одной строкой."
}
# -Encoding UTF8: файл обычно правится в VS Code (UTF-8 по умолчанию)
$dest = Get-Content $configFile -Encoding UTF8 |
  Where-Object { $_.Trim() -ne '' -and -not $_.TrimStart().StartsWith('#') } |
  Select-Object -First 1
if (-not $dest) {
  throw "В $configFile не указан путь (только комментарии/пусто). Впишите путь к папке деплоя одной строкой."
}
$dest = $dest.Trim()

# --- Источники: результаты сборки (см. README, раздел «Сборка и запуск»)
$sources = [ordered]@{
  'windows'   = Join-Path $repoRoot 'artifacts\bin\LlmScanHelper.UI.Windows\release'
  'linux-x64' = Join-Path $repoRoot 'artifacts\bin\LlmScanHelper.UI.Linux\release_linux-x64'
}

$toCopy = switch ($Target) {
  'windows' { @('windows') }
  'linux'   { @('linux-x64') }
  default   { @('windows', 'linux-x64') }
}

foreach ($t in $toCopy) {
  $src = $sources[$t]
  if (-not (Test-Path $src)) {
    throw "Нет собранного результата: $src`nСоберите сначала (README, раздел «Сборка и запуск»)."
  }
  $outDir = Join-Path $dest $t
  if ($Clean) {
    Get-ChildItem -Path $outDir -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
  }
  New-Item -ItemType Directory -Force -Path $outDir | Out-Null
  Copy-Item -Path (Join-Path $src '*') -Destination $outDir -Recurse -Force
  Write-Host "[$t] $src -> $outDir"
}
Write-Host "Готово: $dest"
