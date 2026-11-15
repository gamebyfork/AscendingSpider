
[CmdletBinding()]
param(
    [string]$Version = "0.3.1"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $repoRoot "src/AscendingSpider/AscendingSpider.csproj"
$assetsFile = Join-Path (Split-Path $project -Parent) "obj/project.assets.json"
$releaseRoot = Join-Path $repoRoot "artifacts/release/$Version"

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "Project not found: $project"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK not found. Install the .NET 10 SDK and try again."
}

# ------------------------------------------------------------
# Find GNU tar.
#
# Windows may provide its own tar.exe (bsdtar).
# We explicitly search for GNU tar and remember its full path.
# ------------------------------------------------------------

function Find-GnuTar {
    $candidates = [System.Collections.Generic.List[string]]::new()

    function Add-TarCandidate {
        param([string]$Path)

        if ([string]::IsNullOrWhiteSpace($Path)) {
            return
        }

        $normalizedPath = $Path.Trim('"')

        if (
            (Test-Path -LiteralPath $normalizedPath -PathType Leaf) -and
            -not $candidates.Contains($normalizedPath)
        ) {
            $candidates.Add($normalizedPath)
        }
    }

    # Applications visible to PowerShell.
    Get-Command tar.exe -All -CommandType Application -ErrorAction SilentlyContinue |
        ForEach-Object {
            Add-TarCandidate $_.Source
        }

    # Search every directory from PATH.
    foreach ($pathEntry in ($env:PATH -split [System.IO.Path]::PathSeparator)) {
        if ([string]::IsNullOrWhiteSpace($pathEntry)) {
            continue
        }

        Add-TarCandidate (Join-Path $pathEntry.Trim('"') "tar.exe")
    }

    # Common Git for Windows locations.
    $gitTarPaths = @(
        (Join-Path $env:ProgramFiles "Git\usr\bin\tar.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Git\usr\bin\tar.exe")
    )

    foreach ($path in $gitTarPaths) {
        Add-TarCandidate $path
    }

    # Common MSYS2 locations.
    $msysRoots = @(
        $env:MSYS2_ROOT,
        "C:\msys64",
        "C:\msys32",
        "C:\tools\msys64"
    )

    foreach ($root in $msysRoots) {
        if (-not [string]::IsNullOrWhiteSpace($root)) {
            Add-TarCandidate (Join-Path $root "usr\bin\tar.exe")
        }
    }

    foreach ($candidate in $candidates) {
        Write-Host "Checking tar: $candidate" -ForegroundColor DarkGray

        $versionOutput = (& $candidate --version 2>&1 | Out-String)
        $exitCode = $LASTEXITCODE

        if ($exitCode -eq 0 -and $versionOutput -match "GNU tar") {
            Write-Host "GNU tar found: $candidate" -ForegroundColor Green
            return $candidate
        }
    }

    $checkedPaths = if ($candidates.Count -gt 0) {
        $candidates -join "`n"
    }
    else {
        "(no tar.exe candidates found)"
    }

    throw @"
GNU tar was not found.

Install Git for Windows or MSYS2, or add the directory
containing GNU tar.exe to PATH.

Checked paths:
$checkedPaths
"@
}

$script:TarExe = Find-GnuTar

New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null

# ------------------------------------------------------------
# Find native NuGet package directories.
# ------------------------------------------------------------

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

# ------------------------------------------------------------
# Copy MonoGame native libraries.
# ------------------------------------------------------------

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
            $destination = Join-Path $Output $nativeFile.Name

            Copy-Item `
                -LiteralPath $nativeFile.FullName `
                -Destination $destination `
                -Force

            Write-Host "Native dependency copied: $($nativeFile.Name)" -ForegroundColor DarkGreen
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

# ------------------------------------------------------------
# Create Linux launcher.
# ------------------------------------------------------------

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

    # UTF-8 without BOM: required for a reliable shebang.
    $encoding = New-Object System.Text.UTF8Encoding($false)

    $launcherPath = Join-Path $Output "run.sh"

    [System.IO.File]::WriteAllText(
        $launcherPath,
        $launcher + "`n",
        $encoding
    )
}

# ------------------------------------------------------------
# Create a TAR.GZ archive using GNU tar.
#
# Regular files: 0644
# Directories:   0755
# Executables:   0755
#
# The executable files are appended separately so their
# executable permissions are preserved in the TAR archive.
# ------------------------------------------------------------

function New-TarGzArchive {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Output,

        [Parameter(Mandatory = $true)]
        [string]$Archive
    )

    $temporaryTar = "$Archive.tmp.tar"

    if (Test-Path -LiteralPath $temporaryTar) {
        Remove-Item -LiteralPath $temporaryTar -Force
    }

    if (Test-Path -LiteralPath $Archive) {
        Remove-Item -LiteralPath $Archive -Force
    }

    try {
        Write-Host "Creating TAR archive..." -ForegroundColor Cyan

        # First pass:
        # Add everything except the two executable files.
        #
        # go-w,a+rX:
        # - remove group/other write permissions;
        # - add read permissions for everyone;
        # - add execute permissions to directories.
        # Regular files remain non-executable.

        $createArgs = @(
            "--format=posix",
            "--mode=go-w,a+rX",
            "-cf",
            $temporaryTar,
            "-C",
            $Output,
            "--exclude=./AscendingSpider",
            "--exclude=./run.sh",
            "."
        )

        & $script:TarExe @createArgs

        if ($LASTEXITCODE -ne 0) {
            throw "GNU tar failed while creating the TAR archive. Exit code: $LASTEXITCODE"
        }

        # Second pass:
        # Append the application executable and launcher
        # with explicit executable permissions (0755).

        $appendArgs = @(
            "--format=posix",
            "--mode=0755",
            "-rf",
            $temporaryTar,
            "-C",
            $Output,
            "--",
            "AscendingSpider",
            "run.sh"
        )

        & $script:TarExe @appendArgs

        if ($LASTEXITCODE -ne 0) {
            throw "GNU tar failed while adding executable files. Exit code: $LASTEXITCODE"
        }

        # Verify that both executable entries have 0755-like
        # permissions in the generated TAR archive.

        $listing = @(& $script:TarExe -tvf $temporaryTar 2>&1)

        if ($LASTEXITCODE -ne 0) {
            throw "Could not verify the generated TAR archive."
        }

        $listingText = $listing -join "`n"

        if ($listingText -notmatch '(?m)^-rwx[^\r\n]*AscendingSpider$') {
            throw "The AscendingSpider executable does not have executable permissions in the TAR archive."
        }

        if ($listingText -notmatch '(?m)^-rwx[^\r\n]*run\.sh$') {
            throw "The run.sh launcher does not have executable permissions in the TAR archive."
        }

        # Compress the TAR file using .NET GZipStream.
        # This avoids depending on a separate gzip.exe.

        Write-Host "Compressing TAR archive..." -ForegroundColor Cyan

        $inputStream = $null
        $outputStream = $null
        $gzipStream = $null

        try {
            $inputStream = [System.IO.File]::OpenRead($temporaryTar)
            $outputStream = [System.IO.File]::Create($Archive)

            $gzipStream = [System.IO.Compression.GZipStream]::new(
                $outputStream,
                [System.IO.Compression.CompressionLevel]::Optimal
            )

            $inputStream.CopyTo($gzipStream)
        }
        finally {
            if ($null -ne $gzipStream) {
                $gzipStream.Dispose()
            }

            if ($null -ne $inputStream) {
                $inputStream.Dispose()
            }

            if ($null -ne $outputStream) {
                $outputStream.Dispose()
            }
        }

        Write-Host "TAR.GZ archive created: $Archive" -ForegroundColor Green
    }
    finally {
        if (Test-Path -LiteralPath $temporaryTar) {
            Remove-Item -LiteralPath $temporaryTar -Force
        }
    }
}

# ------------------------------------------------------------
# Publish one runtime.
# ------------------------------------------------------------

function Publish-Runtime {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Rid,

        [Parameter(Mandatory = $true)]
        [string]$ExecutableName
    )

    $output = Join-Path $releaseRoot $Rid

    if ($Rid -eq "linux-x64") {
        $archive = Join-Path $releaseRoot "AscendingSpider-$Version-$Rid.tar.gz"
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

    # Copy SDL and OpenAL native libraries.
    Copy-MonoGameNativeLibraries -Rid $Rid -Output $output

    # MonoGame ContentManager loads XNB files from Content.
    $contentDir = Join-Path $output "Content"

    if (-not (Test-Path -LiteralPath $contentDir -PathType Container)) {
        throw "Content directory is missing from $Rid publish output. Archive will not be created."
    }

    $xnbCount = @(
        Get-ChildItem -LiteralPath $contentDir -Recurse -File -Filter "*.xnb"
    ).Count

    if ($xnbCount -eq 0) {
        throw "No XNB files found in $contentDir. Archive will not be created."
    }

    if ($Rid -eq "linux-x64") {
        Add-LinuxLauncher -Output $output

        New-TarGzArchive `
            -Output $output `
            -Archive $archive
    }
    else {
        Compress-Archive `
            -Path (Join-Path $output "*") `
            -DestinationPath $archive `
            -CompressionLevel Optimal
    }

    Write-Host ""
    Write-Host "Runtime: $Rid" -ForegroundColor Green
    Write-Host "Executable: $executable"
    Write-Host "Native dependencies: verified"
    Write-Host "Content assets: $xnbCount XNB file(s)"
    Write-Host "Archive: $archive" -ForegroundColor Green
}

# ------------------------------------------------------------
# Build release packages.
# ------------------------------------------------------------

Publish-Runtime `
    -Rid "win-x64" `
    -ExecutableName "AscendingSpider.exe"

Publish-Runtime `
    -Rid "linux-x64" `
    -ExecutableName "AscendingSpider"

Write-Host ""
Write-Host "Release packages created in: $releaseRoot" -ForegroundColor Green
Write-Host "Windows: extract the ZIP and run AscendingSpider.exe"
Write-Host "Linux: extract the TAR.GZ and run ./run.sh"
