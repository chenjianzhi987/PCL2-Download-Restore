# 构建 PCL 原版下载恢复工具（生成单文件 exe，内嵌 Mono.Cecil）
# 依赖：Windows 自带 .NET Framework 4.x 的 csc.exe + vendor/Mono.Cecil.dll（net40）

param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$src = Join-Path $root 'src\Program.cs'
$cecil = Join-Path $root 'vendor\Mono.Cecil.dll'
$outDir = Join-Path $root 'dist'
$outExe = Join-Path $outDir 'PclDownloadRestore.exe'

if (-not (Test-Path $src)) { throw "找不到源码：$src" }
if (-not (Test-Path $cecil)) { throw "找不到 Mono.Cecil.dll：$cecil（请先放入 vendor 目录）" }

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "找不到 csc.exe：$csc" }

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$args = @(
    '/nologo'
    '/target:exe'
    '/platform:anycpu'
    '/optimize+'
    '/codepage:65001'
    "/out:$outExe"
    "/reference:$cecil"
    "/resource:$cecil,PCLDownloadRestore.Mono.Cecil.dll"
    $src
)

Write-Host '正在编译…'
& $csc @args
if ($LASTEXITCODE -ne 0) { throw "编译失败（exit $LASTEXITCODE）" }

$hash = (Get-FileHash -LiteralPath $outExe -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  PclDownloadRestore.exe" | Set-Content -LiteralPath (Join-Path $outDir 'SHA256SUMS.txt') -Encoding ascii
Write-Host ("编译完成：" + $outExe)
Write-Host ("SHA-256：" + $hash)
