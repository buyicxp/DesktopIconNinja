# 桌面图标忍者 · 一键编译部署脚本
# 用法：右键“使用 PowerShell 运行”，或在 PowerShell 里执行
#   powershell -ExecutionPolicy Bypass -File "D:\DesktopIconNinja\build.ps1"

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$csc  = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $csc)) {
    Write-Host "找不到 .NET Framework 编译器：$csc" -ForegroundColor Red
    exit 1
}

$src  = Join-Path $root 'DesktopIconNinja.cs'
$exe  = Join-Path $root 'DesktopIconNinja.exe'
$ico  = Join-Path $root 'app.ico'

if (-not (Test-Path -LiteralPath $ico)) {
    $mk = Join-Path $root 'make_icon.ps1'
    if (Test-Path -LiteralPath $mk) { & $mk }
}

Write-Host "正在编译 ..." -ForegroundColor Cyan
& $csc /nologo /target:winexe /optimize+ /out:$exe `
    /win32icon:$ico `
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    $src

if ($LASTEXITCODE -ne 0) {
    Write-Host "编译失败。" -ForegroundColor Red
    exit 1
}

# 桌面快捷方式
$desktop = [Environment]::GetFolderPath('Desktop')
$lnk = Join-Path $desktop '桌面图标忍者.lnk'
$wsh = New-Object -ComObject WScript.Shell
$sc = $wsh.CreateShortcut($lnk)
$sc.TargetPath = $exe
$sc.WorkingDirectory = $root
$sc.IconLocation = "$ico,0"
$sc.Description = '把桌面图标做成水果切开（经典模式 / 摸鱼小游戏）'
$sc.Save()

Write-Host ""
Write-Host "编译完成：" $exe -ForegroundColor Green
Write-Host "快捷方式：" $lnk -ForegroundColor Green
Write-Host "双击桌面上的【桌面图标忍者】即可开始。" -ForegroundColor Green
