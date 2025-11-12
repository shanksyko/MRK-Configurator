Param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

& dotnet restore
& dotnet build -c Debug

Write-Host '>> Build OK'
