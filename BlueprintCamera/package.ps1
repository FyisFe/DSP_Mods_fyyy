param([Parameter(Mandatory=$true)][string]$Dll)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.Drawing
$packageDir = Join-Path $PSScriptRoot 'package'
$manifest = Get-Content -LiteralPath (Join-Path $packageDir 'manifest.json') -Raw | ConvertFrom-Json
$version = [Reflection.AssemblyName]::GetAssemblyName($Dll).Version
if ($version.ToString(3) -ne $manifest.version_number) { throw 'Assembly/manifest version mismatch' }
$icon = [Drawing.Image]::FromFile((Join-Path $packageDir 'icon.png'))
try {
    if ($icon.Width -ne 256 -or $icon.Height -ne 256 -or $icon.RawFormat.Guid -ne [Drawing.Imaging.ImageFormat]::Png.Guid) { throw 'Expected a 256 x 256 PNG icon' }
} finally { $icon.Dispose() }
$destination = Join-Path $packageDir ('BlueprintCamera-' + $manifest.version_number + '.zip')
$temporary = $destination + '.tmp'
$outputStream = [IO.File]::Open($temporary, [IO.FileMode]::Create, [IO.FileAccess]::ReadWrite)
$archive = New-Object IO.Compression.ZipArchive $outputStream, ([IO.Compression.ZipArchiveMode]::Create), $false
try {
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $Dll, 'BlueprintCamera.dll') | Out-Null
    foreach ($name in @('manifest.json','README.md','CHANGELOG.md','icon.png')) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $packageDir $name), $name) | Out-Null
    }
} finally { $archive.Dispose() }
$archive = [IO.Compression.ZipFile]::OpenRead($temporary)
try {
    if ($archive.Entries.Count -ne 5) { throw 'Unexpected archive entries' }
    foreach ($entry in $archive.Entries) {
        $source = if ($entry.FullName -eq 'BlueprintCamera.dll') { $Dll } else { Join-Path $packageDir $entry.FullName }
        $stream = $entry.Open()
        $sourceStream = [IO.File]::OpenRead($source)
        $sha = [Security.Cryptography.SHA256]::Create()
        try {
            $actual = [BitConverter]::ToString($sha.ComputeHash($stream))
            $expected = [BitConverter]::ToString($sha.ComputeHash($sourceStream))
        }
        finally { $sha.Dispose(); $stream.Dispose(); $sourceStream.Dispose() }
        if ($actual -ne $expected) { throw ('Archive mismatch: ' + $entry.FullName) }
    }
} finally { $archive.Dispose() }
Move-Item -LiteralPath $temporary -Destination $destination -Force
Write-Output ('PASS: package entries, SHA-256, version and 256 x 256 icon: ' + $destination)
