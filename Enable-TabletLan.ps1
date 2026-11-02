param([int]$Port=5180)
$ErrorActionPreference='Stop'
if($Port -lt 1024 -or $Port -gt 65535){throw 'Choose a port between 1024 and 65535.'}
$tlsExecutable=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'Backend\LiveScore.Api\bin\Debug\net8.0\LiveScore.Api.exe'))
if(-not (Test-Path -LiteralPath $tlsExecutable)){throw 'Build TLS before configuring tablet access.'}
$tlsIdentity=[Security.Principal.WindowsIdentity]::GetCurrent()
$tlsPrincipal=New-Object Security.Principal.WindowsPrincipal($tlsIdentity)
if(-not $tlsPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Run Enable-TabletLan.ps1 from an elevated PowerShell. The rule is restricted to this TLS executable, port and LocalSubnet.'}
$tlsRuleName='TLS-Tablet-LAN-'+$Port
if(Get-NetFirewallRule -Name $tlsRuleName -ErrorAction SilentlyContinue){Set-NetFirewallRule -Name $tlsRuleName -Enabled True -Direction Inbound -Action Allow -Profile Any -Protocol TCP -LocalPort $Port -RemoteAddress LocalSubnet -Program $tlsExecutable | Out-Null}
else {New-NetFirewallRule -Name $tlsRuleName -DisplayName ('TLS Tablet LAN (TCP '+$Port+')') -Direction Inbound -Action Allow -Profile Any -Protocol TCP -LocalPort $Port -RemoteAddress LocalSubnet -Program $tlsExecutable | Out-Null}
Write-Output ('TLS tablet firewall rule enabled for LocalSubnet on TCP '+$Port+'.')
