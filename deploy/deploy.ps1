<#
.SYNOPSIS
  Builds the bot and the Exponent service and updates them on the Ubuntu server (see DEPLOY.md).

.DESCRIPTION
  Needs the .NET SDK, Node.js, and the Windows OpenSSH client + tar (both ship with Windows 10/11).
  The SSH password is typed by you once at the prompt; nothing secret is stored in this file.
  The server must already be set up as described in deploy/DEPLOY.md (user, /opt/onrebot, /etc/onrebot.env, systemd units).

.EXAMPLE
  .\deploy\deploy.ps1 -Server root@YOUR_SERVER_IP

.EXAMPLE
  .\deploy\deploy.ps1 -Server root@YOUR_SERVER_IP -DryRun    # builds and lists what would be uploaded, touches nothing
#>
param(
    [Parameter(Mandatory = $true)][string]$Server,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$staging = Join-Path ([System.IO.Path]::GetTempPath()) "onrebot-deploy"

if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
New-Item -ItemType Directory -Force "$staging\bot", "$staging\exponent" | Out-Null

Write-Host "1/3 Publishing the bot..."
dotnet publish "$root\OnReBot" -c Release -o "$staging\bot" -p:UseAppHost=false | Out-Null
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "2/3 Building the Exponent service..."
Push-Location "$root\ExponentService"
try {
    npm run build | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "npm run build failed" }
}
finally { Pop-Location }
Copy-Item "$root\ExponentService\dist" "$staging\exponent\dist" -Recurse
Copy-Item "$root\ExponentService\package.json", "$root\ExponentService\package-lock.json" "$staging\exponent"
Copy-Item "$PSScriptRoot\remote-update.sh" $staging

if ($DryRun) {
    Write-Host "Dry run: this would be uploaded to $Server`:/opt/onrebot"
    # same pipe as the real upload, but listing instead of extracting
    cmd /c "tar -cf - -C `"$staging`" . | tar -tf -"
    Remove-Item -Recurse -Force $staging
    return
}

Write-Host "3/3 Uploading and restarting on $Server (enter the SSH password when asked)..."
# One tar stream over a single ssh connection, so the password is asked only once.
cmd /c "tar -cf - -C `"$staging`" . | ssh $Server `"tar -xf - -C /opt/onrebot && bash /opt/onrebot/remote-update.sh`""
if ($LASTEXITCODE -ne 0) { throw "Upload or restart failed (see the messages above)" }

Remove-Item -Recurse -Force $staging
Write-Host "Done."
