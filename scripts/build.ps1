# SW-WASD 构建脚本（不需要 Visual Studio）
#
#   powershell -ExecutionPolicy Bypass -File scripts\build.ps1
#   powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -SwDir "D:\SW"
#
param([string]$SwDir)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$bin = Join-Path $root 'bin'

function Find-SolidWorks {
    param([string]$Hint)
    if ($Hint -and (Test-Path (Join-Path $Hint 'SolidWorks.Interop.sldworks.dll'))) { return $Hint }
    if ($env:SWDIR -and (Test-Path (Join-Path $env:SWDIR 'SolidWorks.Interop.sldworks.dll'))) { return $env:SWDIR }

    $def = 'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS'
    if (Test-Path (Join-Path $def 'SolidWorks.Interop.sldworks.dll')) { return $def }

    $hit = Get-ChildItem 'C:\Program Files' -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { Join-Path $_.FullName 'SOLIDWORKS' } |
        Where-Object { Test-Path (Join-Path $_ 'SolidWorks.Interop.sldworks.dll') } |
        Select-Object -First 1
    if ($hit) { return $hit }

    $hit = Get-ChildItem 'C:\Program Files\SOLIDWORKS Corp' -Directory -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName 'SolidWorks.Interop.sldworks.dll') } |
        Select-Object -First 1 -ExpandProperty FullName
    return $hit
}

$sw = Find-SolidWorks -Hint $SwDir
if (-not $sw) {
    Write-Host '[错误] 没找到 SolidWorks 安装目录。' -ForegroundColor Red
    Write-Host '       用 -SwDir 指定，例如：'
    Write-Host '       powershell -File scripts\build.ps1 -SwDir "C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS"'
    exit 1
}
Write-Host "SolidWorks 目录: $sw" -ForegroundColor Cyan

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    Write-Host '[错误] 找不到 .NET Framework 4.x 的 csc.exe' -ForegroundColor Red
    exit 1
}

New-Item -ItemType Directory -Force -Path $bin | Out-Null

$src = Join-Path $root 'src\SwWasdAddin.cs'
$out = Join-Path $bin 'SwWasd.dll'

Write-Host '编译中...' -ForegroundColor Cyan
& $csc -nologo -target:library -platform:x64 -out:"$out" `
    -r:"$(Join-Path $sw 'SolidWorks.Interop.sldworks.dll')" `
    -r:"$(Join-Path $sw 'SolidWorks.Interop.swpublished.dll')" `
    -r:System.Windows.Forms.dll -r:System.Drawing.dll `
    "$src"

if ($LASTEXITCODE -ne 0) {
    Write-Host '[错误] 编译失败' -ForegroundColor Red
    exit $LASTEXITCODE
}

# 把 SW 的 interop DLL 拷到 bin（不提交进仓库，只在本机用）
Copy-Item (Join-Path $sw 'SolidWorks.Interop.sldworks.dll') $bin -Force
Copy-Item (Join-Path $sw 'SolidWorks.Interop.swpublished.dll') $bin -Force

# 配置文件
$ini = Join-Path $bin 'swwasd.ini'
if (-not (Test-Path $ini)) {
    Copy-Item (Join-Path $root 'swwasd.ini') $ini
}

Write-Host ''
Write-Host "构建完成 -> $out" -ForegroundColor Green
Get-ChildItem $bin | Format-Table Name, Length -AutoSize
Write-Host '下一步：powershell -ExecutionPolicy Bypass -File scripts\install.ps1' -ForegroundColor Yellow
