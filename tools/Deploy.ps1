param([switch]$MigrateConfig,[string]$ServerRoot,[string]$DotnetPath)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$server=Split-Path (Split-Path $project -Parent) -Parent
if ($ServerRoot) { $server=(Resolve-Path -LiteralPath $ServerRoot).Path }
$api=Join-Path $server 'game\csgo\addons\counterstrikesharp\api\CounterStrikeSharp.API.dll'
$installed=[Reflection.AssemblyName]::GetAssemblyName($api).Version
if ($installed -lt [version]'1.0.380.0') { throw 'CounterStrikeSharp API 380 or newer is required.' }
$destination=Join-Path $server 'game\csgo\addons\counterstrikesharp\plugins\Superpower1v5'
$configDirectory=Join-Path $server 'game\csgo\addons\counterstrikesharp\configs\plugins\Superpower1v5'
$output=Join-Path $project 'bin\Release\net10.0'
if (!(Test-Path -LiteralPath (Join-Path $output 'Superpower1v5.dll'))) { throw 'Build the plugin before deployment.' }
New-Item -ItemType Directory -Path $destination,$configDirectory -Force | Out-Null
$config=Join-Path $configDirectory 'Superpower1v5.json'
if (!(Test-Path -LiteralPath $config)) { Copy-Item -LiteralPath (Join-Path $project 'Superpower1v5.example.json') -Destination $config }
elseif ($MigrateConfig) {
    $backup=$config+'.before-v7-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.bak'
    Copy-Item -LiteralPath $config -Destination $backup
    $sdk=$DotnetPath
    if (!$sdk) {
        $sdk=Join-Path $server 'development\.dotnet\dotnet.exe'
        if (!(Test-Path -LiteralPath $sdk)) { $sdk='dotnet' }
    }
    & $sdk (Join-Path $project 'Tests\bin\Release\net10.0\Superpower1v5.Tests.dll') --migrate-config $config
    if ($LASTEXITCODE -ne 0) { throw 'Configuration migration failed; deployment stopped before replacing the DLL.' }
}
# Copy the DLL last: CounterStrikeSharp's file watcher can begin a live reload immediately.
foreach ($name in @('Superpower1v5.pdb','Superpower1v5.deps.json','Superpower1v5.dll')) {
    Copy-Item -LiteralPath (Join-Path $output $name) -Destination (Join-Path $destination $name) -Force
}
Copy-Item -LiteralPath (Join-Path $project 'README.md') -Destination (Join-Path $destination 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $project 'Superpower1v5.example.json') -Destination (Join-Path $destination 'Superpower1v5.example.json') -Force
$reportDirectory=Join-Path $destination 'artifacts'
New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
$report=Join-Path $project 'artifacts\VERIFICATION.md'
if (!(Test-Path -LiteralPath $report)) { $report=Join-Path $project 'docs\VERIFICATION.md' }
if (Test-Path -LiteralPath $report) { Copy-Item -LiteralPath $report -Destination (Join-Path $reportDirectory 'VERIFICATION.md') -Force }
$manifest=[ordered]@{ Plugin='Superpower1v5';Version='1.7.0';CounterStrikeSharpAPI=$installed.ToString();
    SHA256=(Get-FileHash -LiteralPath (Join-Path $destination 'Superpower1v5.dll') -Algorithm SHA256).Hash;
    Source=$project;PluginDirectory=$destination;Config=$config }
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'installation.json') -Encoding UTF8
$manifest | ConvertTo-Json
