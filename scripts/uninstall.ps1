# SW-WASD 卸载脚本（会自己弹 UAC）
#
#   powershell -ExecutionPolicy Bypass -File scripts\uninstall.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$bin = Join-Path $root 'bin'
$dll = Join-Path $bin 'SwWasd.dll'

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

if (Get-Process SLDWORKS -ErrorAction SilentlyContinue) {
    Write-Host '[错误] SolidWorks 正在运行，请先关闭。' -ForegroundColor Red
    exit 1
}

$guid = '{7B2E5C41-9A3D-4F18-B6E2-1C8D4A7F0E93}'

# 反注册 COM（会顺带调用 ComUnregisterFunction 清掉 HKCU 那套登记）
$regasm = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe'
if (Test-Path $dll) {
    Write-Host '反注册 COM 组件...' -ForegroundColor Cyan
    & $regasm /unregister /silent "$dll" | Out-Null
}

Write-Host '删除插件登记项...' -ForegroundColor Cyan
foreach ($hive in 'HKLM:', 'HKCU:') {
    $p = "$hive\SOFTWARE\SolidWorks\Addins\$guid"
    if (Test-Path $p) { Remove-Item $p -Recurse -Force }
}

Write-Host ''
Write-Host '卸载完成。bin 目录可以手动删除。' -ForegroundColor Green
