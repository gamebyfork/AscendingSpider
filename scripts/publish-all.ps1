
[CmdletBinding()]
param(
    [string]$Version = "0.3.1"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $repoRoot "src/AscendingSpider/AscendingSpider.csproj"
$assetsFile = Join-Path (Split-Path $project -Parent) "obj/project.assets.json"
$releaseRoot = Join-Path $repoRoot "artifacts/release/$Version"

$script:TarExe = $null

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "Project not found: $project"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK not found. Install the .NET 10 SDK and try again."
}

# ---------------------------------------------------------------------------
# Find GNU tar
# ---------------------------------------------------------------------------

function Find-GnuTar {
    $candidates = @()

    # First, check tar.exe commands visible through PATH.
    $commands = Get-Command tar.exe -All -CommandType Application `
        -ErrorAction SilentlyContinue

    foreach ($command in $commands) {
        $candidates += $command.Source
    }

    # Check PATH entries explicitly.
    foreach ($pathEntry in ($env:PATH -split [IO.Path]::PathSeparator)) {
        if ([string]::IsNullOrWhiteSpace($pathEntry)) {
            continue
        }

        $candidate = Join-Path $pathEntry "tar.exe"

        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $candidates += $candidate
        }
    }

    # Common Git for Windows and MSYS2 locations.
    $knownPaths = @(
        (Join-Path $env:ProgramFiles "Git/usr/bin/tar.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Git/usr/bin/tar.exe"),
        "C:\msys64\usr\bin\tar.exe",
        "C:\msys32\usr\bin\tar.exe"
    )

    foreach ($candidate in $knownPaths) {
        if (
            -not [string]::IsNullOrWhiteSpace($candidate) -and
            (Test-Path -LiteralPath $candidate -PathType Leaf)
        ) {
            $candidates += $candidate
        }
    }

    $candidates = @(
        $candidates |
        Where-Object { $_ } |
        Select-Object -Unique
    )

    foreach ($candidate in $candidates) {
        Write-Host "Checking tar: $candidate"

        $versionOutput = (& $candidate --version 2>&1 | Out-String)

        if (
            $LASTEXITCODE -eq 0 -and
            $versionOutput -match "GNU tar"
        ) {
            $script:TarExe = $candidate
            Write-Host "GNU tar found: $candidate" -ForegroundColor Green
            return
        }
    }

    throw @"
GNU tar was not found.

Install Git for Windows or MSYS2.
The script needs GNU tar to create a Linux TAR.GZ archive
with executable permissions preserved.
"@
}

Find-GnuTar

# ---------------------------------------------------------------------------
# Locate native NuGet package directories
# ---------------------------------------------------------------------------

function Get-NuGetPackageNativeDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Assets,

        [Parameter(Mandatory = $true)]
        [string]$PackageName,

        [Parameter(Mandatory = $true)]
        [string]$Rid
    )

    $packageEntry = $Assets.libraries.PSObject.Properties |
        Where-Object {
            $_.Name -like "$PackageName/*"
        } |
        Select-Object -First 1

    if ($null -eq $packageEntry) {
        throw "Package $PackageName was not found in project.assets.json. Run dotnet restore and try again."
    }

    $version = $packageEntry.Name.Substring($PackageName.Length + 1)

    $relativePath = "$($PackageName.ToLowerInvariant())\$version\runtimes\$Rid\native"

    foreach ($folder in $Assets.packageFolders.PSObject.Properties) {
        $candidate = Join-Path $folder.Name $relativePath

        if (Test-Path -LiteralPath $candidate -PathType Container) {
            return $candidate
        }
    }

    throw "Native files for $PackageName ($Rid) were not found in the NuGet cache. Expected: $relativePath"
}

# ---------------------------------------------------------------------------
# Copy MonoGame native libraries
# ---------------------------------------------------------------------------

function Copy-MonoGameNativeLibraries {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Rid,

        [Parameter(Mandatory = $true)]
        [string]$Output
    )

    if (-not (Test-Path -LiteralPath $assetsFile -PathType Leaf)) {
        throw "NuGet assets file not found: $assetsFile. Run dotnet restore first."
    }

    $assets = Get-Content -LiteralPath $assetsFile -Raw |
        ConvertFrom-Json

    $packages = @(
        "MonoGame.Library.SDL",
        "MonoGame.Library.OpenAL"
    )

    foreach ($packageName in $packages) {
        $nativeDirectory = Get-NuGetPackageNativeDirectory `
            -Assets $assets `
            -PackageName $packageName `
            -Rid $Rid

        $nativeFiles = @(
            Get-ChildItem -LiteralPath $nativeDirectory -File
        )

        if ($nativeFiles.Count -eq 0) {
            throw "No native files found in $nativeDirectory"
        }

        foreach ($nativeFile in $nativeFiles) {
            $destination = Join-Path $Output $nativeFile.Name

            Copy-Item `
                -LiteralPath $nativeFile.FullName `
                -Destination $destination `
                -Force

            Write-Host "Native dependency copied: $($nativeFile.Name)" `
                -ForegroundColor DarkGreen
        }
    }

    $requiredFiles = switch ($Rid) {
        "win-x64" {
            @(
                "SDL2.dll",
                "openal.dll"
            )
        }

        "linux-x64" {
            @(
                "libSDL2-2.0.so.0",
                "libopenal.so"
            )
        }

        default {
            throw "Unsupported runtime identifier: $Rid"
        }
    }

    foreach ($requiredFile in $requiredFiles) {
        $requiredPath = Join-Path $Output $requiredFile

        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "Required native library is missing for $Rid`: $requiredFile. Archive will not be created."
        }
    }
}

# ---------------------------------------------------------------------------
# Create Linux launcher
# ---------------------------------------------------------------------------

function Add-LinuxLauncher {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Output
    )

    $launcher = @'
#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

export LD_LIBRARY_PATH="$SCRIPT_DIR${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"

exec "$SCRIPT_DIR/AscendingSpider" "$@"
'@

    # UTF-8 without BOM: the shebang must be the first bytes of the file.
    $encoding = New-Object System.Text.UTF8Encoding($false)

    [System.IO.File]::WriteAllText(
        (Join-Path $Output "run.sh"),
        $launcher + "`n",
        $encoding
    )
}

# ---------------------------------------------------------------------------
# Create Linux TAR.GZ archive
# ---------------------------------------------------------------------------

function New-TarGzArchive {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Output,

        [Parameter(Mandatory = $true)]
        [string]$Archive
    )

    $archiveDirectory = Split-Path $Archive -Parent

    $temporaryTar = Join-Path `
        $archiveDirectory `
        ([IO.Path]::GetFileName($Archive) + ".tmp.tar")

    $temporaryGzip = "$Archive.tmp"

    foreach ($temporaryFile in @($temporaryTar, $temporaryGzip)) {
        if (Test-Path -LiteralPath $temporaryFile) {
            Remove-Item -LiteralPath $temporaryFile -Force
        }
    }

    Write-Host "Creating TAR archive..." -ForegroundColor Cyan

    try {
        # IMPORTANT:
        # Run GNU tar from inside the publish directory.
        # This avoids passing Windows paths to tar's -C argument.
        Push-Location -LiteralPath $Output

        try {
            # Add regular files and directories.
            # The executable and launcher are added separately
            # with explicit 0755 permissions.
            $createArgs = @(
                "--force-local",
                "--format=posix",
                "--mode=go-w,a+rX",
                "-cf", $temporaryTar,
                "--exclude=./AscendingSpider",
                "--exclude=./run.sh",
                "."
            )

            & $script:TarExe @createArgs

            if ($LASTEXITCODE -ne 0) {
                throw "GNU tar failed while creating the TAR archive. Exit code: $LASTEXITCODE"
            }

            # Add the Linux executable and launcher with 0755 permissions.
            $appendArgs = @(
                "--force-local",
                "--format=posix",
                "--mode=0755",
                "-rf", $temporaryTar,
                "--",
                "AscendingSpider",
                "run.sh"
            )

            & $script:TarExe @appendArgs

            if ($LASTEXITCODE -ne 0) {
                throw "GNU tar failed while adding executable files. Exit code: $LASTEXITCODE"
            }

            # Verify the archive and executable permissions.
            $listing = @(
                & $script:TarExe --force-local -tvf $temporaryTar 2>&1
            )

            if ($LASTEXITCODE -ne 0) {
                throw "GNU tar failed while verifying the TAR archive. Exit code: $LASTEXITCODE"
            }

            $executableEntry = $listing | Where-Object {
                $_ -match '^-rwx\S*\s+.*\sAscendingSpider$'
            }

            $launcherEntry = $listing | Where-Object {
                $_ -match '^-rwx\S*\s+.*\srun\.sh$'
            }

            if (-not $executableEntry) {
                throw "AscendingSpider does not have executable permissions in the TAR archive."
            }

            if (-not $launcherEntry) {
                throw "run.sh does not have executable permissions in the TAR archive."
            }
        }
        finally {
            Pop-Location
        }

        # Compress the TAR file using .NET GZipStream.
        Write-Host "Compressing TAR archive..." -ForegroundColor Cyan

        $sourceStream = [System.IO.File]::OpenRead($temporaryTar)

        try {
            $destinationStream = [System.IO.File]::Create($temporaryGzip)

            try {
                $gzipStream = [System.IO.Compression.GZipStream]::new(
                    $destinationStream,
                    [System.IO.Compression.CompressionLevel]::Optimal
                )

                try {
                    $sourceStream.CopyTo($gzipStream)
                }
                finally {
                    $gzipStream.Dispose()
                }
            }
            finally {
                $destinationStream.Dispose()
            }
        }
        finally {
            $sourceStream.Dispose()
        }

        # Replace an existing archive only after successful compression.
        if (Test-Path -LiteralPath $Archive) {
            Remove-Item -LiteralPath $Archive -Force
        }

        Move-Item `
            -LiteralPath $temporaryGzip `
            -Destination $Archive `
            -Force

        Write-Host "TAR.GZ archive created: $Archive" -ForegroundColor Green
    }
    finally {
        foreach ($temporaryFile in @($temporaryTar, $temporaryGzip)) {
            if (Test-Path -LiteralPath $temporaryFile) {
                Remove-Item -LiteralPath $temporaryFile -Force
            }
        }
    }
}

# ---------------------------------------------------------------------------
# Publish one runtime
# ---------------------------------------------------------------------------

function Publish-Runtime {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Rid,

        [Parameter(Mandatory = $true)]
        [string]$ExecutableName
    )

    $output = Join-Path $releaseRoot $Rid

    $archiveExtension = if ($Rid -eq "linux-x64") {
        "tar.gz"
    }
    else {
        "zip"
    }

    $archive = Join-Path `
        $releaseRoot `
        "AscendingSpider-$Version-$Rid.$archiveExtension"

    # Clean previous output.
    if (Test-Path -LiteralPath $output) {
        Remove-Item -LiteralPath $output -Recurse -Force
    }

    if (Test-Path -LiteralPath $archive) {
        Remove-Item -LiteralPath $archive -Force
    }

    New-Item -ItemType Directory -Force -Path $output | Out-Null

    Write-Host "`n=== Publishing $Rid ===" -ForegroundColor Cyan

    $publishArgs = @(
        "publish",
        $project,
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

    # Copy SDL2 and OpenAL native libraries.
    Copy-MonoGameNativeLibraries -Rid $Rid -Output $output

    # MonoGame ContentManager loads XNB files from Content.
    $contentDir = Join-Path $output "Content"

    if (-not (Test-Path -LiteralPath $contentDir -PathType Container)) {
        throw "Content directory is missing from $Rid publish output. Archive will not be created."
    }

    $xnbCount = @(
        Get-ChildItem `
            -LiteralPath $contentDir `
            -Recurse `
            -File `
            -Filter "*.xnb"
    ).Count

    if ($xnbCount -eq 0) {
        throw "No XNB files found in $contentDir. Archive will not be created."
    }

    # Create Linux launcher and TAR.GZ.
    if ($Rid -eq "linux-x64") {
        Add-LinuxLauncher -Output $output

        New-TarGzArchive `
            -Output $output `
            -Archive $archive
    }
    else {
        # Windows release is distributed as ZIP.
        Compress-Archive `
            -Path (Join-Path $output "*") `
            -DestinationPath $archive `
            -CompressionLevel Optimal
    }

    Write-Host ""
    Write-Host "Runtime: $Rid" -ForegroundColor Cyan
    Write-Host "Executable: $executable" -ForegroundColor Green
    Write-Host "Native dependencies: verified"
    Write-Host "Content assets: $xnbCount XNB file(s)"
    Write-Host "Archive: $archive" -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# Publish all supported runtimes
# ---------------------------------------------------------------------------

New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null

Publish-Runtime `
    -Rid "win-x64" `
    -ExecutableName "AscendingSpider.exe"

Publish-Runtime `
    -Rid "linux-x64" `
    -ExecutableName "AscendingSpider"

Write-Host ""
Write-Host "Release packages created in: $releaseRoot" -ForegroundColor Green

Write-Host ""
Write-Host "Windows: extract the ZIP and run AscendingSpider.exe"
Write-Host "Linux: extract the TAR.GZ and run ./run.sh"
