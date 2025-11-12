Param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Write-Host '>> Scan: possíveis refs órfãs'

$patterns = @(
    'CloseTestWindow(',
    'StopCoreUnsafe(',
    'SuspendPreviewCapture(',
    'On[A-Z]\w+_Click(',
    'On[A-Z]\w+_Changed(',
    'On[A-Z]\w+_Paint('
)

$results = New-Object System.Collections.Generic.List[string]

& git ls-files -- '*.cs' | ForEach-Object {
    $path = $_
    Select-String -Path $path -Pattern $patterns -SimpleMatch -ErrorAction SilentlyContinue |
        ForEach-Object {
            $results.Add("{0}:{1}:{2}" -f $path, $_.LineNumber, $_.Line.Trim())
        }
}

$artifactPath = Join-Path 'artifacts' 'missing-refs.txt'
$results | Sort-Object | Set-Content -Path $artifactPath -Encoding UTF8

Write-Host ">> Resultado: $artifactPath"
