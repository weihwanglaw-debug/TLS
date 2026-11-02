$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
Push-Location (Join-Path $projectRoot 'Frontend')
try {
    & npm.cmd run build
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
} finally { Pop-Location }
$target = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'Backend\LiveScore.Api\wwwroot'))
$expected = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'Backend\LiveScore.Api')) + [System.IO.Path]::DirectorySeparatorChar
if (-not $target.StartsWith($expected, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe asset target.' }
if (Test-Path -LiteralPath $target) {
    Get-ChildItem -LiteralPath $target -Force | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }
}
New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item -Path (Join-Path $projectRoot 'Frontend\dist\*') -Destination $target -Recurse
& dotnet build (Join-Path $projectRoot 'Backend\LiveScore.Api\LiveScore.Api.csproj')
if ($LASTEXITCODE -ne 0) { throw 'Backend build failed.' }
