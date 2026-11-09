$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$output = Join-Path $root 'abgabe'
$staging = Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString())
New-Item -ItemType Directory -Path $staging -Force | Out-Null
New-Item -ItemType Directory -Path $output -Force | Out-Null
try {
    Get-ChildItem $root -File -Recurse -Force | Where-Object {
        $relative = $_.FullName.Substring($root.Length + 1)
        $relative -notmatch '(^|[\\/])(bin|obj|\.git|\.vs|\.idea|abgabe|artifacts)([\\/]|$)' -and
        $_.Name -notmatch '^\.env($|\.)|\.user$|\.suo$' -and $_.Extension -ne '.pdf'
    } | ForEach-Object {
        $destination = Join-Path $staging $_.FullName.Substring($root.Length + 1)
        New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $destination
    }
    foreach ($report in @('artifacts/test-results/integration.trx', 'artifacts/cluster/httpyac.xml')) {
        $results = Join-Path $root $report
        if (Test-Path $results) {
            $reportDirectory = Join-Path $staging 'test-results'
            New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
            Copy-Item -LiteralPath $results -Destination (Join-Path $reportDirectory (Split-Path $results -Leaf))
        }
    }
    $archive = Join-Path $output 'programmentwurf.zip'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path $archive) { Remove-Item -LiteralPath $archive }
    [IO.Compression.ZipFile]::CreateFromDirectory($staging, $archive)
    Write-Output "Created $archive"
}
finally {
    Remove-Item -LiteralPath $staging -Recurse -Force
}