<#
.SYNOPSIS
    Rssary AOT 跨平台打包脚本
.DESCRIPTION
    自动对 Windows (x64/ARM64) 和 Linux (x64/ARM64) 进行 AOT 编译打包。
    在非目标平台上编译时需确保已安装对应的目标运行时 SDK。

    示例用法:
        .\publish.ps1               # 打包所有平台
        .\publish.ps1 -Platform win  # 仅打包 Windows
        .\publish.ps1 -Platform linux -Clean
#>

param(
    [ValidateSet('all', 'win', 'linux')]
    [string]$Platform = 'all',
    [switch]$Clean
)

$root = Split-Path -Parent $PSCommandPath
$proj = Join-Path $root 'Rssary.csproj'
$out  = Join-Path $root 'publish'

$configurations = @(
    @{ Rid = 'win-x64';   Platform = 'win';   Name = 'Windows x64'   },
    @{ Rid = 'win-arm64'; Platform = 'win';   Name = 'Windows ARM64' },
    @{ Rid = 'linux-x64';   Platform = 'linux'; Name = 'Linux x64'     },
    @{ Rid = 'linux-arm64'; Platform = 'linux'; Name = 'Linux ARM64'   }
)

if ($Clean -and (Test-Path $out)) {
    Write-Host "🧹 清理输出目录: $out" -ForegroundColor Yellow
    Remove-Item -Recurse -Force $out
}

foreach ($cfg in $configurations) {
    if ($Platform -ne 'all' -and $cfg.Platform -ne $Platform) {
        continue
    }

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
