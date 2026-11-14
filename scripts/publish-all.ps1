[CmdletBinding()]
param(
    [string]$Version = "0.3.0"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $repoRoot "src/AscendingSpider/AscendingSpider.csproj"
$releaseRoot = Join-Path $repoRoot "artifacts/release/$Version"

if (-not (Test-Path $project)) {
    throw "Не найден проект: $project"
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK не найден. Установи .NET 10 SDK и повтори запуск."
}

New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null

function Publish-Runtime {
    param(
        [Parameter(Mandatory=$true)][string]$Rid,
        [Parameter(Mandatory=$true)][string]$ExecutableName
    )

    $output = Join-Path $releaseRoot $Rid
    $archive = Join-Path $releaseRoot "AscendingSpider-$Version-$Rid.zip"

    if (Test-Path $output) { Remove-Item $output -Recurse -Force }
    if (Test-Path $archive) { Remove-Item $archive -Force }
    New-Item -ItemType Directory -Force -Path $output | Out-Null

    Write-Host "`n=== Публикация $Rid ===" -ForegroundColor Cyan
    $args = @(
        "publish", $project,
        "--configuration", "Release",
        "--runtime", $Rid,
        "--self-contained", "true",
        "--output", $output,
        "--nologo",
        "-p:Version=$Version",
        "-p:AssemblyVersion=$Version.0",
        "-p:FileVersion=$Version.0",
        "-p:InformationalVersion=$Version",
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:EnableCompressionInSingleFile=true",
        "-p:PublishTrimmed=false",
        "-p:PublishReadyToRun=false",
        "-p:TieredCompilation=false",
        "-p:UseAppHost=true",
        "-p:DebugType=None",
        "-p:DebugSymbols=false"
    )

    & dotnet @args
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish завершился с кодом $LASTEXITCODE для $Rid"
    }

    $executable = Join-Path $output $ExecutableName
    if (-not (Test-Path $executable)) {
        throw "Не найден исполняемый файл $executable"
    }

    # MonoGame ContentManager loads XNB files from the Content directory at runtime.
    # Keep Content beside the single-file apphost; managed/native dependencies are bundled.
    $contentDir = Join-Path $output "Content"
    if (-not (Test-Path $contentDir)) {
        throw "Публикация $Rid не содержит папку Content. Проверь вывод MGCB/Content Builder; архив не будет создан, чтобы не выдать неполную сборку."
    }
    $xnbCount = @(Get-ChildItem $contentDir -Recurse -File -Filter "*.xnb").Count
    if ($xnbCount -eq 0) {
        throw "В $contentDir нет .xnb-файлов. Сборка Content Pipeline не попала в publish."
    }

    Compress-Archive -Path (Join-Path $output "*") -DestinationPath $archive -CompressionLevel Optimal
    Write-Host "Готово: $executable" -ForegroundColor Green
    Write-Host "Content assets: $xnbCount .xnb file(s)"
    Write-Host "Архив: $archive" -ForegroundColor Green
}

Publish-Runtime -Rid "win-x64" -ExecutableName "AscendingSpider.exe"
Publish-Runtime -Rid "linux-x64" -ExecutableName "AscendingSpider"

Write-Host "`nПубликации созданы в: $releaseRoot" -ForegroundColor Green
Write-Host "Для Linux после распаковки при необходимости выполни: chmod +x AscendingSpider"
