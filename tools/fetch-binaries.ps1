<#
.SYNOPSIS
    Downloads yt-dlp, FFmpeg + ffprobe and Deno into tools/bin for local development.

.DESCRIPTION
    Uses the same official sources as SSnip's dependency manager. tools/bin is git-ignored.
    Point SSnip or the integration tests at another copy with "Use existing..." or SSNIP_TEST_ROOT.

.EXAMPLE
    ./tools/fetch-binaries.ps1
    ./tools/fetch-binaries.ps1 -Force
#>
[CmdletBinding()]
param(
    [string] $Destination,
    [switch] $Force
)

$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1 leaves $PSScriptRoot empty in parameter defaults, so resolve it here.
if (-not $Destination) { $Destination = Join-Path $PSScriptRoot 'bin' }
$ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest is far faster without the progress bar

$sources = @(
    @{ Name = 'yt-dlp'; Url = 'https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe'; Files = @('yt-dlp.exe') },
    @{ Name = 'FFmpeg'; Url = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'; Files = @('ffmpeg.exe', 'ffprobe.exe') },
    @{ Name = 'Deno';   Url = 'https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip'; Files = @('deno.exe') }
)

New-Item -ItemType Directory -Force $Destination | Out-Null
$work = Join-Path ([IO.Path]::GetTempPath()) "ssnip-fetch-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory $work | Out-Null

try {
    foreach ($source in $sources) {
        $missing = $source.Files | Where-Object { $Force -or -not (Test-Path (Join-Path $Destination $_)) }
        if (-not $missing) {
            Write-Host "$($source.Name): already present"
            continue
        }

        Write-Host "$($source.Name): downloading $($source.Url)"
        $download = Join-Path $work ([IO.Path]::GetFileName(([uri]$source.Url).AbsolutePath))
        Invoke-WebRequest -Uri $source.Url -OutFile $download -UseBasicParsing

        if ($download.EndsWith('.zip')) {
            $extract = Join-Path $work $source.Name
            Expand-Archive -Path $download -DestinationPath $extract
            foreach ($file in $source.Files) {
                $found = Get-ChildItem -Path $extract -Recurse -Filter $file | Select-Object -First 1
                if (-not $found) { throw "$file not found in $($source.Url)" }
                Copy-Item $found.FullName (Join-Path $Destination $file) -Force
            }
        }
        else {
            Copy-Item $download (Join-Path $Destination $source.Files[0]) -Force
        }
    }
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host "yt-dlp  $(& (Join-Path $Destination 'yt-dlp.exe') --ignore-config --version)"
Write-Host "$((& (Join-Path $Destination 'ffmpeg.exe') -hide_banner -version | Select-Object -First 1))"
Write-Host "$((& (Join-Path $Destination 'ffprobe.exe') -hide_banner -version | Select-Object -First 1))"
Write-Host "$((& (Join-Path $Destination 'deno.exe') --version | Select-Object -First 1))"
