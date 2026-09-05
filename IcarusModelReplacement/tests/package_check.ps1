param([string]$Package)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content -LiteralPath (Join-Path $project 'IcarusModelReplacement.csproj') -Raw)).Project.PropertyGroup.Version
if (-not $Package) { $Package = Join-Path $project "package\IcarusModelReplacement-$version.zip" }
$Package = (Resolve-Path -LiteralPath $Package).Path
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Drawing
$archive = [IO.Compression.ZipFile]::OpenRead($Package)
try {
    $prefix = 'BepInEx/plugins/IcarusModelReplacement/'
    $expected = @('manifest.json', 'icon.png', 'README.md', 'MODEL_FORMAT.md') + @(
        'IcarusModelReplacement.dll', 'README.md', 'MODEL_FORMAT.md', 'tools/export_model.py',
        'model/model.json', 'model/mesh.bin.gz', 'model/texture.png', 'model/README.md'
    ).ForEach({ $prefix + $_ })
    $actual = @($archive.Entries.ForEach({ $_.FullName.Replace('\', '/') }) | Where-Object { -not $_.EndsWith('/') })
    # BepInEx/plugins preserves model subfolders in r2modman; arbitrary ZIP folders are flattened.
    if (Compare-Object $expected $actual) { throw 'Wrong ZIP layout or missing bundled files' }
} finally { $archive.Dispose() }

$temporary = Join-Path ([IO.Path]::GetTempPath()) ('icarus-package-check-' + [Guid]::NewGuid().ToString('N'))
try {
    Expand-Archive -LiteralPath $Package -DestinationPath $temporary
    $manifest = Get-Content -LiteralPath (Join-Path $temporary 'manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.name -ne 'IcarusModelReplacement' -or $manifest.version_number -ne $version) { throw 'Manifest identity/version mismatch' }
    if (-not $manifest.description -or $manifest.description.Length -gt 250) { throw 'Invalid package description' }
    if (@($manifest.dependencies).Count -ne 1 -or $manifest.dependencies[0] -ne 'xiaoye97-BepInEx-5.4.17') { throw 'Only the BepInEx dependency should be required' }
    $icon = [Drawing.Image]::FromFile((Join-Path $temporary 'icon.png'))
    try {
        if ($icon.Width -ne 256 -or $icon.Height -ne 256 -or $icon.RawFormat.Guid -ne [Drawing.Imaging.ImageFormat]::Png.Guid) { throw 'Icon must be a 256x256 PNG' }
    } finally { $icon.Dispose() }
    $plugin = Join-Path $temporary 'BepInEx\plugins\IcarusModelReplacement'
    $dll = Join-Path $plugin 'IcarusModelReplacement.dll'
    if ([Reflection.AssemblyName]::GetAssemblyName($dll).Version -ne [Version]"$version.0") { throw 'DLL/manifest version mismatch' }
    foreach ($name in @('model.json', 'mesh.bin.gz', 'texture.png')) {
        $source = Join-Path $project ("..\GuguGaga\model\" + $name)
        if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath (Join-Path $plugin ("model\" + $name))).Hash) { throw "Bundled model differs: $name" }
    }
    & (Join-Path $PSScriptRoot 'bin\Release\net472\Checks.exe') (Join-Path $plugin 'model') $dll
    if ($LASTEXITCODE -ne 0) { throw 'Extracted model checks failed' }
    Write-Output 'PASS: release ZIP layout, sole BepInEx dependency, version, icon and bundled model loaded outside the workspace.'
} finally {
    if (Test-Path -LiteralPath $temporary) {
        $resolved = (Resolve-Path -LiteralPath $temporary).Path
        if ($resolved -ne [IO.Path]::GetFullPath($temporary) -or -not (Split-Path $resolved -Leaf).StartsWith('icarus-package-check-')) { throw 'Unexpected cleanup directory' }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
