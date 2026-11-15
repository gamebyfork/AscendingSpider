[CmdletBinding()]
param(
    [string]$Version = "0.3.1"
)

$ErrorActionPreference = "Stop"

# Ищем корень репозитория вверх от папки скрипта.
$repoRoot = (Get-Item -LiteralPath $PSScriptRoot).FullName

while (-not (Test-Path -LiteralPath (Join-Path $repoRoot "src/AscendingSpider/AscendingSpider.csproj") -PathType Leaf)) {
    $parent = Split-Path -Parent $repoRoot

    if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $repoRoot) {
        throw "Could not locate src/AscendingSpider/AscendingSpider.csproj above: $PSScriptRoot"
    }

    $repoRoot = $parent
}

$project = Join-Path $repoRoot "src/AscendingSpider/AscendingSpider.csproj"
$assetsFile = Join-Path (Split-Path $project -Parent) "obj/project.assets.json"
$releaseRoot = Join-Path $repoRoot "artifacts/release/$Version"

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Invalid version '$Version'. Expected a version such as 0.3.1."
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK not found. Install the .NET 10 SDK and try again."
}

New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null

# ------------------------------------------------------------
# Поиск нативных библиотек MonoGame в NuGet
# ------------------------------------------------------------

function Get-NuGetPackageNativeDirectory {
    param(
        [Parameter(Mandatory=$true)][object]$Assets,
        [Parameter(Mandatory=$true)][string]$PackageName,
        [Parameter(Mandatory=$true)][string]$Rid
    )

    $packageEntry = $Assets.libraries.PSObject.Properties |
        Where-Object { $_.Name -like "$PackageName/*" } |
        Select-Object -First 1

    if ($null -eq $packageEntry) {
        throw "Package $PackageName was not found in project.assets.json."
    }

    $version = $packageEntry.Name.Substring($PackageName.Length + 1)
    $relativePath = "$($PackageName.ToLowerInvariant())\$version\runtimes\$Rid\native"

    foreach ($folder in $Assets.packageFolders.PSObject.Properties) {
        $candidate = Join-Path $folder.Name $relativePath

        if (Test-Path -LiteralPath $candidate -PathType Container) {
            return $candidate
        }
    }

    throw "Native files for $PackageName ($Rid) were not found. Expected: $relativePath"
}

function Copy-MonoGameNativeLibraries {
    param(
        [Parameter(Mandatory=$true)][string]$Rid,
        [Parameter(Mandatory=$true)][string]$Output
    )

    if (-not (Test-Path -LiteralPath $assetsFile -PathType Leaf)) {
        throw "NuGet assets file not found: $assetsFile"
    }

    $assets = Get-Content -LiteralPath $assetsFile -Raw | ConvertFrom-Json

    $packages = @(
        "MonoGame.Library.SDL",
        "MonoGame.Library.OpenAL"
    )

    foreach ($packageName in $packages) {
        $nativeDirectory = Get-NuGetPackageNativeDirectory `
            -Assets $assets `
            -PackageName $packageName `
            -Rid $Rid

        $nativeFiles = @(Get-ChildItem -LiteralPath $nativeDirectory -File)

        if ($nativeFiles.Count -eq 0) {
            throw "No native files found in $nativeDirectory"
        }

        foreach ($nativeFile in $nativeFiles) {
            Copy-Item `
                -LiteralPath $nativeFile.FullName `
                -Destination (Join-Path $Output $nativeFile.Name) `
                -Force

            Write-Host "Native dependency copied: $($nativeFile.Name)" -ForegroundColor DarkGreen
        }
    }

    # Проверяем обязательные библиотеки для каждого RID.
    $requiredFiles = switch ($Rid) {
        "win-x64" {
            @("SDL2.dll", "openal.dll")
        }

        "linux-x64" {
            @("libSDL2-2.0.so.0", "libopenal.so")
        }

        default {
            throw "Unsupported runtime identifier: $Rid"
        }
    }

    foreach ($requiredFile in $requiredFiles) {
        $requiredPath = Join-Path $Output $requiredFile

        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "Required native library is missing for $Rid`: $requiredFile"
        }
    }
}

# ------------------------------------------------------------
# Linux launcher
# ------------------------------------------------------------

function Add-LinuxLauncher {
    param(
        [Parameter(Mandatory=$true)][string]$Output
    )

    $launcher = @'
#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

export LD_LIBRARY_PATH="$SCRIPT_DIR${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"

exec "$SCRIPT_DIR/AscendingSpider" "$@"
'@

    $encoding = New-Object System.Text.UTF8Encoding($false)

    [System.IO.File]::WriteAllText(
        (Join-Path $Output "run.sh"),
        $launcher + "`n",
        $encoding
    )

    $readme = @'
Ascending Spider — Linux x64

1. Extract this archive.
2. Open a terminal in the extracted folder.
3. Run:

     ./run.sh

No chmod command is required.

This is a self-contained .NET linux-x64 build.
The .NET runtime is included.
MonoGame SDL2 and OpenAL native libraries are bundled.

Target platform:
64-bit Linux with glibc.

A working desktop session and compatible OpenGL graphics
drivers and system libraries are still required.

This package is not intended for Alpine Linux or other
musl-based distributions.
'@

    [System.IO.File]::WriteAllText(
        (Join-Path $Output "README-Linux.txt"),
        $readme + "`n",
        $encoding
    )
}

# ------------------------------------------------------------
# Создание Linux TAR.GZ с Unix-правами
# ------------------------------------------------------------

function New-LinuxTarGz {
    param(
        [Parameter(Mandatory=$true)][string]$Output,
        [Parameter(Mandatory=$true)][string]$Archive
    )

    $tarCommand = Get-Command tar -ErrorAction SilentlyContinue

    if ($null -eq $tarCommand) {
        throw "GNU tar not found. Install GNU tar and add it to PATH."
    }

    $tarExe = $tarCommand.Source

    $tarVersion = & $tarExe --version 2>&1 | Out-String

    if ($tarVersion -notmatch "GNU tar") {
        throw "GNU tar is required. The detected tar implementation is not GNU tar."
    }

    $temporaryTar = Join-Path $releaseRoot "linux-package-temp.tar"

    if (Test-Path -LiteralPath $temporaryTar) {
        Remove-Item -LiteralPath $temporaryTar -Force
    }

    try {
        # Сначала добавляем исполняемый файл и launcher
        # с Unix-правами 755.
        & $tarExe `
            --format=posix `
            --mode=0755 `
            -cf $temporaryTar `
            -C $Output `
            "AscendingSpider" `
            "run.sh"

        if ($LASTEXITCODE -ne 0) {
            throw "GNU tar failed while adding Linux executables."
        }

        # Добавляем остальные файлы и каталоги.
        # Иерархия Content/Fonts, Content/Sounds и т.д. сохраняется.
        $otherItems = Get-ChildItem -LiteralPath $Output -Force |
            Where-Object {
                $_.Name -ne "AscendingSpider" -and
                $_.Name -ne "run.sh"
            }

        foreach ($item in $otherItems) {
            & $tarExe `
                --format=posix `
                -rf $temporaryTar `
                -C $Output `
                $item.Name

            if ($LASTEXITCODE -ne 0) {
                throw "GNU tar failed while adding $($item.Name)."
            }
        }

        # Сжимаем tar средствами .NET.
        $inputStream = [System.IO.File]::OpenRead($temporaryTar)
        $outputStream = [System.IO.File]::Create($Archive)

        try {
            $gzipStream = New-Object System.IO.Compression.GZipStream(
                $outputStream,
                [System.IO.Compression.CompressionLevel]::Optimal
            )

            try {
                $inputStream.CopyTo($gzipStream)
            }
            finally {
                $gzipStream.Dispose()
            }
        }
        finally {
            $inputStream.Dispose()
            $outputStream.Dispose()
        }

        Write-Host "Linux TAR.GZ created: $Archive" -ForegroundColor Green
    }
    finally {
        if (Test-Path -LiteralPath $temporaryTar) {
            Remove-Item -LiteralPath $temporaryTar -Force
        }
    }
}

# ------------------------------------------------------------
# Публикация отдельной платформы
# ------------------------------------------------------------

function Publish-Runtime {
    param(
        [Parameter(Mandatory=$true)][string]$Rid,
        [Parameter(Mandatory=$true)][string]$ExecutableName
    )

    $output = Join-Path $releaseRoot $Rid

    if ($Rid -eq "linux-x64") {
        $archive = Join-Path $releaseRoot "AscendingSpider-$Version-linux-x64.tar.gz"
    }
    else {
        $archive = Join-Path $releaseRoot "AscendingSpider-$Version-$Rid.zip"
    }

    if (Test-Path -LiteralPath $output) {
        Remove-Item -LiteralPath $output -Recurse -Force
    }

    if (Test-Path -LiteralPath $archive) {
        Remove-Item -LiteralPath $archive -Force
    }

    New-Item -ItemType Directory -Force -Path $output | Out-Null

    Write-Host "`n=== Publishing $Rid ===" -ForegroundColor Cyan

    $publishArgs = @(
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

        "-p:PublishSingleFile=false",
        "-p:IncludeNativeLibrariesForSelfExtract=false",
        "-p:PublishTrimmed=false",
        "-p:PublishReadyToRun=false",
        "-p:TieredCompilation=false",
        "-p:UseAppHost=true",

        "-p:DebugType=None",
        "-p:DebugSymbols=false"
    )

    & dotnet @publishArgs

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE for $Rid"
    }

    $executable = Join-Path $output $ExecutableName

    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "Executable was not created: $executable"
    }

    # Копируем SDL2 и OpenAL рядом с исполняемым файлом.
    Copy-MonoGameNativeLibraries -Rid $Rid -Output $output

    # Проверяем наличие контента.
    $contentDir = Join-Path $output "Content"

    if (-not (Test-Path -LiteralPath $contentDir -PathType Container)) {
        throw "Content directory is missing from $Rid publish output."
    }

    $xnbFiles = @(
        Get-ChildItem -LiteralPath $contentDir -Recurse -File -Filter "*.xnb"
    )

    if ($xnbFiles.Count -eq 0) {
        throw "No XNB files found in $contentDir."
    }

    if ($Rid -eq "linux-x64") {
        Add-LinuxLauncher -Output $output

        New-LinuxTarGz `
            -Output $output `
            -Archive $archive
    }
    else {
        # ZIP для Windows.
        Compress-Archive `
            -Path (Join-Path $output "*") `
            -DestinationPath $archive `
            -CompressionLevel Optimal
    }

    Write-Host "Executable: $executable" -ForegroundColor Green
    Write-Host "Native dependencies: verified"
    Write-Host "Content assets: $($xnbFiles.Count) XNB file(s)"
    Write-Host "Archive: $archive" -ForegroundColor Green
}

# ------------------------------------------------------------
# Release
# ------------------------------------------------------------

Publish-Runtime -Rid "win-x64" -ExecutableName "AscendingSpider.exe"

Publish-Runtime -Rid "linux-x64" -ExecutableName "AscendingSpider"

Write-Host "`nRelease packages created in: $releaseRoot" -ForegroundColor Green
Write-Host "Windows x64: extract and run AscendingSpider.exe"
Write-Host "Linux x64: extract and run ./run.sh"