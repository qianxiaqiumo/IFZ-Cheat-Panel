param(
    [switch]$Install,
    [string]$GameDir = ""
)

$ErrorActionPreference = 'Stop'

function Find-GameDir {
    $candidates = @()
    if ($GameDir) { $candidates += $GameDir }
    try {
        $steam = (Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam' -ErrorAction SilentlyContinue).InstallPath
        if ($steam) {
            $libFile = Join-Path $steam 'steamapps\libraryfolders.vdf'
            if (Test-Path -LiteralPath $libFile) {
                foreach ($m in [regex]::Matches((Get-Content -LiteralPath $libFile -Raw), '"path"\s+"([^"]+)"')) {
                    # vdf 里的路径是转义过的（D:\\steam），需要还原成 D:\steam
                    $libPath = $m.Groups[1].Value -replace '\\\\', '\'
                    if ($libPath) {
                        try { $candidates += [System.IO.Path]::Combine($libPath, 'steamapps\common\Infection Free Zone') } catch { }
                    }
                }
            }
            try { $candidates += [System.IO.Path]::Combine($steam, 'steamapps\common\Infection Free Zone') } catch { }
        }
    } catch { }

    foreach ($c in $candidates) {
        if (-not $c) { continue }
        try {
            if (Test-Path -LiteralPath (Join-Path $c 'Infection Free Zone_Data')) { return $c }
        } catch { }
    }
    return $null
}

$gameDir = Find-GameDir
if (-not $gameDir) { throw '没找到无感染区安装目录' }
Write-Host "游戏目录：$gameDir"

$managed = Join-Path $gameDir 'Infection Free Zone_Data\Managed'
$bepCore = Join-Path $gameDir 'BepInEx\core'
$bepPlugins = Join-Path $gameDir 'BepInEx\plugins'
if (-not (Test-Path -LiteralPath $bepCore)) { throw "BepInEx 未安装：$bepCore 不存在" }

$src = Join-Path $PSScriptRoot 'src\IfzCheat.cs'
$outDir = Join-Path $PSScriptRoot 'bin'
$outDll = Join-Path $outDir 'IfzCheat.dll'

$refs = @()
$refs += (Join-Path $managed 'mscorlib.dll')
$refs += (Join-Path $managed 'System.dll')
$refs += (Join-Path $managed 'System.Core.dll')
$refs += (Join-Path $managed 'netstandard.dll')
$refs += (Join-Path $managed 'UnityEngine.dll')
$refs += (Join-Path $managed 'UnityEngine.CoreModule.dll')
$refs += (Join-Path $managed 'UnityEngine.IMGUIModule.dll')
$refs += (Join-Path $managed 'UnityEngine.InputLegacyModule.dll')
$refs += (Join-Path $managed 'UnityEngine.UIModule.dll')
$refs += (Join-Path $managed 'UnityEngine.TextRenderingModule.dll')
$refs += (Join-Path $managed 'Ifz.dll')
Get-ChildItem -LiteralPath $bepCore -Filter *.dll -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -notmatch 'Mono\.Cecil|Il2Cpp' } |
    ForEach-Object { $refs += $_.FullName }

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { throw "找不到 csc.exe：$csc" }

$cscArgs = @('/nologo', '/noconfig', '/nostdlib+', '/target:library', '/platform:x64', '/optimize+', "/out:$outDll")
foreach ($r in $refs) { $cscArgs += "/r:$r" }
$cscArgs += $src

Write-Host "编译中…（引用 $($refs.Count) 个程序集）"
& $csc $cscArgs
if ($LASTEXITCODE -ne 0) { throw '编译失败，见上面错误。' }
Write-Host "编译成功：$outDll" -ForegroundColor Green

if ($Install) {
    New-Item -ItemType Directory -Force -Path $bepPlugins | Out-Null
    if (Get-Process 'Infection Free Zone' -ErrorAction SilentlyContinue) {
        Write-Host '警告：游戏正在运行，先关掉游戏再复制，否则会失败。' -ForegroundColor Yellow
    }
    try {
        Copy-Item -LiteralPath $outDll -Destination $bepPlugins -Force
        Write-Host "已复制到：$bepPlugins" -ForegroundColor Green
    } catch {
        Write-Host "复制失败（游戏可能占用着文件）：$($_.Exception.Message)" -ForegroundColor Red
    }
}
