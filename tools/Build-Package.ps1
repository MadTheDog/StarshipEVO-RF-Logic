param([Parameter(Mandatory)][string]$PluginPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$manifest = Join-Path $root 'sevo.mod.json'
$package = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
$plugin = (Resolve-Path -LiteralPath $PluginPath).Path
$assembly = [Reflection.AssemblyName]::GetAssemblyName($plugin)
if ($assembly.Name -ne 'RadioLogic' -or $assembly.Version.ToString(3) -ne $package.version) {
    throw 'RF assembly name or version differs from the package manifest'
}
$output = Join-Path $root 'dist/releases'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$zip = Join-Path $output "StarshipEVO-RF-Logic-$($package.version).zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $manifest, 'sevo.mod.json') | Out-Null
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $plugin, 'payload/RadioLogic.dll') | Out-Null
} finally {
    $archive.Dispose()
}
Get-FileHash -LiteralPath $zip
