<#
.SYNOPSIS
    Rssary AOT 跨平台打包脚本
.DESCRIPTION
    自动对当前平台支持的架构进行 AOT 编译打包。
    注意: .NET Native AOT 不支持跨 OS 编译。
      - Windows 上只能打 win-x64 / win-arm64
      - Linux   上只能打 linux-x64 / linux-arm64

    示例用法:
        .\publish.ps1               # 打包当前平台所有架构
        .\publish.ps1 -Platform win # 仅打包 Windows 目标
        .\publish.ps1 -Clean        # 清理旧输出后打包
#>

param(
    [ValidateSet('all', 'win', 'linux')]
    [string]$Platform = 'all',
    [switch]$Clean
)

$root = Split-Path -Parent $PSCommandPath
$proj = Join-Path $root 'Rssary.csproj'
$out  = Join-Path $root 'publish'

# 检测当前 OS
$currentIsWindows = [System.Environment]::OSVersion.Platform -eq 'Win32NT'
$currentIsLinux   = -not $currentIsWindows

# 根据当前 OS 过滤可用的目标
$allConfigs = @(
    @{ Rid = 'win-x64';     Platform = 'win';   Name = 'Windows x64'   },
    @{ Rid = 'win-arm64';   Platform = 'win';   Name = 'Windows ARM64' },
    @{ Rid = 'linux-x64';   Platform = 'linux'; Name = 'Linux x64'     },
    @{ Rid = 'linux-arm64'; Platform = 'linux'; Name = 'Linux ARM64'   }
)

# 过滤: 跨 OS 目标不可用
$configurations = $allConfigs | Where-Object {
    if ($Platform -ne 'all' -and $_.Platform -ne $Platform) { return $false }
    if ($currentIsWindows -and $_.Platform -eq 'linux') {
        Write-Host "  ⚠ [$($_.Name)] 跳过 — .NET AOT 不支持在 Windows 上编译 Linux 目标。请在 Linux 上运行此脚本。" -ForegroundColor Yellow
        return $false
    }
    if ($currentIsLinux -and $_.Platform -eq 'win') {
        Write-Host "  ⚠ [$($_.Name)] 跳过 — .NET AOT 不支持在 Linux 上编译 Windows 目标。请在 Windows 上运行此脚本。" -ForegroundColor Yellow
        return $false
    }
    return $true
}

if ($configurations.Count -eq 0) {
    Write-Host "当前平台没有可打包的 AOT 目标。" -ForegroundColor Red
    exit 1
}

if ($Clean -and (Test-Path $out)) {
    Write-Host "清理输出目录: $out" -ForegroundColor Yellow
    Remove-Item -Recurse -Force $out
}

foreach ($cfg in $configurations) {
    $rid = $cfg.Rid
    $outputDir = Join-Path $out $rid
    $label = $cfg.Name

    Write-Host "`n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
    Write-Host "  [$label] 打包开始" -ForegroundColor Cyan
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan

    $sw = [System.Diagnostics.Stopwatch]::StartNew()

    & dotnet publish $proj `
        -c Release `
        -r $rid `
        -o $outputDir `
        --self-contained true `
        -p:PublishAot=true `
        -p:InvariantGlobalization=true `
        -p:DebugType=none

    if ($LASTEXITCODE -eq 0) {
        $sw.Stop()
        Write-Host "  ✓ [$label] 打包成功，耗时 $($sw.Elapsed.TotalSeconds.ToString('0.0'))s" -ForegroundColor Green
        Write-Host "  输出目录: $outputDir" -ForegroundColor Green
    } else {
        $sw.Stop()
        Write-Host "  ✗ [$label] 打包失败，耗时 $($sw.Elapsed.TotalSeconds.ToString('0.0'))s" -ForegroundColor Red
        Write-Host "  请确保已安装 $rid 运行时 SDK 或对应 Visual Studio 组件。" -ForegroundColor Red
    }
}

Write-Host "`n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
Write-Host "  打包完成" -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan

# 列出产物
if (Test-Path $out) {
    Write-Host "`n输出产物:" -ForegroundColor Magenta
    Get-ChildItem $out -Directory | ForEach-Object {
        $size = "{0:N2}" -f ((Get-ChildItem $_.FullName -File -Recurse | Measure-Object Length -Sum).Sum / 1MB)
        Write-Host "  $($_.Name)  ($size MB)" -ForegroundColor Magenta
    }
}
