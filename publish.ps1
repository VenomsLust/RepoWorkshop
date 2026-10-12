# Publishes a Release build of Repo Workshop and creates Start Menu and Desktop shortcuts.
# Re-run to update the installed copy. Close the app first.
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\RepoWorkshop'),
    [switch]$NoDesktopShortcut
)

$ErrorActionPreference = 'Stop'

if (Get-Process RepoWorkshop -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$InstallDir*" }) {
    throw "Repo Workshop is running from $InstallDir. Close it and run this script again."
}

$project = Join-Path $PSScriptRoot 'src\RepoWorkshop\RepoWorkshop.csproj'
dotnet publish $project -c Release -r win-x64 --self-contained false -o $InstallDir -p:DebugType=None
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$exe = Join-Path $InstallDir 'RepoWorkshop.exe'
$shell = New-Object -ComObject WScript.Shell

$locations = @([Environment]::GetFolderPath('Programs'))
if (-not $NoDesktopShortcut) { $locations += [Environment]::GetFolderPath('Desktop') }

foreach ($folder in $locations) {
    $shortcut = $shell.CreateShortcut((Join-Path $folder 'Repo Workshop.lnk'))
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $InstallDir
    $shortcut.Description = 'Repo Workshop'
    $shortcut.Save()
    Write-Host "Shortcut: $($shortcut.FullName)"
}

Write-Host "Installed to $InstallDir"
