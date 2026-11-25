param(
    [Parameter(Mandatory)]
    [string]$DownloaderPath,
    [Parameter(Mandatory)]
    [string]$Destination
)

$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $DownloaderPath).Path)
$package = $assembly.GetType('KlCustom.Downloader.Package', $true)
$prepare = $package.GetMethod('PrepareAsync', [Reflection.BindingFlags]'Static,NonPublic')
$task = $prepare.Invoke($null, [object[]]@($Destination.PSObject.BaseObject, $true))
$task.GetAwaiter().GetResult()
