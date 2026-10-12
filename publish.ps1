# Publishes a Release build of Repo Workshop and the rw command-line client, creates Start Menu
# and Desktop shortcuts, and installs the repo-workshop skill for Claude Code.
# Re-run to update the installed copy. Close the app first.
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\RepoWorkshop'),
    [switch]$NoDesktopShortcut,
    # Skip copying skills\repo-workshop to ~\.claude\skills.
    [switch]$NoSkill,
    # Add the install folder to the user PATH so "rw" works from any terminal.
    [switch]$AddToPath
)

$ErrorActionPreference = 'Stop'

if (Get-Process RepoWorkshop, rw -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$InstallDir*" }) {
    throw "Repo Workshop (or rw) is running from $InstallDir. Close it and run this script again."
}

foreach ($name in 'RepoWorkshop', 'RepoWorkshop.Cli') {
    $project = Join-Path $PSScriptRoot "src\$name\$name.csproj"
    dotnet publish $project -c Release -r win-x64 --self-contained false -o $InstallDir -p:DebugType=None
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish of $name failed." }
}

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

if (-not $NoSkill) {
    $skillDir = Join-Path $HOME '.claude\skills\repo-workshop'
    New-Item -ItemType Directory -Force $skillDir | Out-Null
    Copy-Item (Join-Path $PSScriptRoot 'skills\repo-workshop\*') $skillDir -Recurse -Force
    Write-Host "Skill: $skillDir"
}

if ($AddToPath) {
    # Edit the registry directly, keeping %VAR% references and the value type intact
    # ([Environment]::SetEnvironmentVariable would expand them and store a plain string).
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment', $true)
    $kind = if ($key.GetValueNames() -contains 'Path') { $key.GetValueKind('Path') } else { 'ExpandString' }
    $userPath = $key.GetValue('Path', '', 'DoNotExpandEnvironmentNames')
    if (($userPath -split ';') -notcontains $InstallDir) {
        $key.SetValue('Path', ($userPath.TrimEnd(';') + ";$InstallDir").TrimStart(';'), $kind)
        # Tell Explorer and other apps the environment changed.
        Add-Type -Namespace Win32 -Name Native -MemberDefinition @'
[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);
'@
        $result = [UIntPtr]::Zero
        [Win32.Native]::SendMessageTimeout([IntPtr]0xffff, 0x1A, [UIntPtr]::Zero, 'Environment', 2, 5000, [ref]$result) | Out-Null
        Write-Host "Added $InstallDir to the user PATH (open a new terminal to use 'rw')."
    }
}

Write-Host "Installed to $InstallDir"
