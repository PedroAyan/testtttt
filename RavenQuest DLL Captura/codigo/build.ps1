param([string]$ZigPath)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ZigPath)) {
    $ZigPath = Join-Path $PSScriptRoot 'toolchain\zig-x86_64-windows-0.16.0\zig.exe'
}
if (-not (Test-Path -LiteralPath $ZigPath -PathType Leaf)) {
    throw 'Informe -ZigPath com o caminho do compilador Zig portable.'
}
$taskOut = Join-Path $PSScriptRoot 'entrega'
New-Item -ItemType Directory -Path $taskOut -Force | Out-Null
$env:ZIG_GLOBAL_CACHE_DIR = Join-Path $PSScriptRoot 'toolchain\cache'
$env:ZIG_LOCAL_CACHE_DIR = Join-Path $PSScriptRoot 'toolchain\local-cache'
& $ZigPath cc -target x86_64-windows-gnu -shared -O2 -Wall -Wextra -Werror `
    (Join-Path $PSScriptRoot 'CaptureRestore.c') -luser32 -lkernel32 `
    -o (Join-Path $taskOut 'CaptureRestore.dll')
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar a DLL nativa.' }
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe /platform:x64 `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll `
    ('/out:' + (Join-Path $taskOut 'TestarDLL.exe')) (Join-Path $PSScriptRoot 'TestarDLL.cs')
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o host de teste.' }
Get-FileHash -LiteralPath (Join-Path $taskOut 'CaptureRestore.dll'),(Join-Path $taskOut 'TestarDLL.exe') -Algorithm SHA256
