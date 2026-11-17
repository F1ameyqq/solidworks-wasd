# SW-WASD 安装脚本
#
#   powershell -ExecutionPolicy Bypass -File scripts\install.ps1
#
# 需要管理员权限（会自己弹 UAC）。装之前请先关掉 SolidWorks。

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$bin = Join-Path $root 'bin'
$dll = Join-Path $bin 'SwWasd.dll'

if (-not (Test-Path $dll)) {
    Write-Host '[错误] 没找到 bin\SwWasd.dll，先跑 build.ps1' -ForegroundColor Red
    exit 1
}

# --- 自提权 ---
$isAdmin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()
).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host '需要管理员权限，正在提权...' -ForegroundColor Yellow
    Start-Process powershell -Verb RunAs -Wait -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath
    )
    exit 0
}

# --- 关掉 SW 再装 ---
$sw = Get-Process SLDWORKS -ErrorAction SilentlyContinue
if ($sw) {
    Write-Host '[错误] SolidWorks 正在运行，请先关闭再安装。' -ForegroundColor Red
    exit 1
}

$swDir = Join-Path $env:ProgramFiles 'SOLIDWORKS Corp\SOLIDWORKS'
foreach ($n in 'SolidWorks.Interop.sldworks.dll', 'SolidWorks.Interop.swpublished.dll') {
    $src = Join-Path $swDir $n
    if (Test-Path $src) { Copy-Item $src $bin -Force }
    elseif (-not (Test-Path (Join-Path $bin $n))) {
        Write-Host "[错误] 缺少 $n —— 先跑 build.ps1，或用 SWDIR 环境变量指定 SolidWorks 目录" -ForegroundColor Red
        exit 1
    }
}

$ini = Join-Path $bin 'swwasd.ini'
if (-not (Test-Path $ini)) { Copy-Item (Join-Path $root 'swwasd.ini') $ini }

# --- COM 注册 ---
$regasm = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe'
Write-Host '注册 COM 组件...' -ForegroundColor Cyan
& $regasm /codebase /silent "$dll"
if ($LASTEXITCODE -ne 0) {
    Write-Host '[错误] RegAsm 失败' -ForegroundColor Red
    exit $LASTEXITCODE
}

# --- 插件登记（SolidWorks 只认 HKLM）---
$guid = '{7B2E5C41-9A3D-4F18-B6E2-1C8D4A7F0E93}'
$key = "HKLM:\SOFTWARE\SolidWorks\Addins\$guid"
Write-Host '登记 SolidWorks 插件...' -ForegroundColor Cyan
New-Item -Path $key -Force | Out-Null
Set-ItemProperty -Path $key -Name '(default)' -Value 1 -Type DWord
Set-ItemProperty -Path $key -Name 'Title' -Value 'WASD View'
Set-ItemProperty -Path $key -Name 'Description' -Value 'Keyboard WASD view control (F9 toggle)'

Write-Host ''
Write-Host '安装完成。' -ForegroundColor Green
Write-Host '  1) 启动 SolidWorks' -ForegroundColor Yellow
Write-Host '  2) 工具 → 插件 → 勾选「WASD View」' -ForegroundColor Yellow
Write-Host '  3) 打开零件，按 F9 启用' -ForegroundColor Yellow
Write-Host ''
Write-Host "参数文件: $ini   （改完按两次 F9 热加载）" -ForegroundColor Cyan
Write-Host "运行日志: $(Join-Path $bin 'addin.log')" -ForegroundColor Cyan
