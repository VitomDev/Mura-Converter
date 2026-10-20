# Builds Mura Converter and its installer: .\build.ps1  ->  dist\MuraConverter-Setup-x.y.z.exe
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'Mura Converter'
$tools = Join-Path $project 'tools'
New-Item -ItemType Directory -Force $tools | Out-Null

# 1. Bundled tools (not tracked in git).
$ytdlp = Join-Path $tools 'yt-dlp.exe'
if (-not (Test-Path $ytdlp)) {
    Write-Host 'Downloading yt-dlp...'
    Invoke-WebRequest 'https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe' -OutFile $ytdlp
}

if (-not (Test-Path (Join-Path $tools 'ffmpeg.exe')) -or -not (Test-Path (Join-Path $tools 'ffprobe.exe'))) {
    Write-Host 'Downloading FFmpeg (essentials)...'
    $zip = Join-Path $env:TEMP 'ffmpeg-essentials.zip'
    $extract = Join-Path $env:TEMP 'ffmpeg-essentials'
    Invoke-WebRequest 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip' -OutFile $zip
    Remove-Item -Recurse -Force $extract -ErrorAction SilentlyContinue
    Expand-Archive $zip $extract
    Get-ChildItem $extract -Recurse -Include ffmpeg.exe, ffprobe.exe | Copy-Item -Destination $tools
}

# 2. Publish (self-contained, ReadyToRun).
$publish = Join-Path $root 'publish'
Remove-Item -Recurse -Force $publish -ErrorAction SilentlyContinue
dotnet publish (Join-Path $project 'Mura Converter.csproj') -c Release -o $publish
if ($LASTEXITCODE) { throw 'dotnet publish failed' }

# 3. Installer.
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup not found. Install it with: winget install JRSoftware.InnoSetup' }

& $iscc (Join-Path $root 'installer\MuraConverter.iss')
if ($LASTEXITCODE) { throw 'Inno Setup failed' }

Get-ChildItem (Join-Path $root 'dist') -Filter *.exe
