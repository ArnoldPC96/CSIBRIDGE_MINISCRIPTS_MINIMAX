param(
    [string]$ScriptClass = "Scripts.HelloCsi"
)

$ErrorActionPreference = "Stop"
$PSScriptRoot = Split-Path -Parent -Path $MyInvocation.MyCommand.Definition

# 1. Encontrar MSBuild
$msbuild = "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
If (-Not (Test-Path $msbuild)) {
    Write-Error "No se encontro MSBuild. Verifica la ruta o asegurate de tener los C++ build tools instalados."
    exit 1
}

$projectPath = "$PSScriptRoot\src\CsiRunner\CsiRunner.csproj"
$outDir = "$PSScriptRoot\output"

if (-Not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
if (Test-Path "$outDir\proof.json") { Remove-Item "$outDir\proof.json" -Force }

Write-Host "Compilando proyecto con MSBuild..." -ForegroundColor Cyan
& $msbuild $projectPath /p:Configuration=Debug /p:Platform=x64 /nologo /v:m
if ($LASTEXITCODE -ne 0) { throw "Error de compilacion detectado. Fallo el build." }

$exePath = "$PSScriptRoot\src\CsiRunner\bin\x64\Debug\CsiRunner.exe"
if (-Not (Test-Path $exePath)) { throw "No se encontó el ejecutable de compilación (bin\x64\Debug\CsiRunner.exe)." }

Write-Host "Ejecutando test script en COM ("$ScriptClass")..." -ForegroundColor Green
& $exePath $ScriptClass "$outDir"

if (Test-Path "$outDir\proof.json") {
    Write-Host "`n==== PROOF (JSON OUTPUT) ====" -ForegroundColor Yellow
    Get-Content "$outDir\proof.json" | Out-Host
    Write-Host "=============================" -ForegroundColor Yellow
} else {
    Write-Warning "AVISO: El proceso finalizo pero no creo el archivo proof.json en $outDir."
}
