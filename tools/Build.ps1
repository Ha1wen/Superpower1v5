param([switch]$SkipTests,[string]$CounterStrikeSharpPath,[string]$DotnetPath)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$server=Split-Path (Split-Path $project -Parent) -Parent
$sdk=$DotnetPath
if (!$sdk) {
    $sdk=Join-Path $server 'development\.dotnet\dotnet.exe'
    if (!(Test-Path -LiteralPath $sdk)) { $sdk='dotnet' }
}
$env:DOTNET_CLI_HOME=Join-Path $server 'development\.dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$buildArguments=@('-c','Release','-p:ServerSmoke=false','--nologo')
if ($CounterStrikeSharpPath) {
    $apiDirectory=(Resolve-Path -LiteralPath $CounterStrikeSharpPath).Path
    if (!(Test-Path -LiteralPath (Join-Path $apiDirectory 'CounterStrikeSharp.API.dll'))) { throw 'CounterStrikeSharpPath must point to the API directory containing CounterStrikeSharp.API.dll.' }
    $buildArguments+='-p:CounterStrikeSharpPath='+$apiDirectory
}
& $sdk build (Join-Path $project 'Superpower1v5.csproj') @buildArguments
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
if (!$SkipTests) {
    & $sdk build (Join-Path $project 'Tests\Superpower1v5.Tests.csproj') @buildArguments
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    & $sdk (Join-Path $project 'Tests\bin\Release\net10.0\Superpower1v5.Tests.dll') --write-config (Join-Path $project 'Superpower1v5.example.json')
    if ($LASTEXITCODE -ne 0) { throw 'Automated tests failed.' }
}
