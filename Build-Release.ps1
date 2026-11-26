param(
    [Parameter(Mandatory)]
    [string]$NativeDllPath,
    [Parameter(Mandatory)]
    [string]$SevenZipPath,
    [Parameter(Mandatory)]
    [string]$OutputDirectory,
    [string]$WixPath = 'wix',
    [string]$SigningCertificateThumbprint,
    [uri]$TimestampServer,
    [switch]$AllowSelfSigned
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'klCustom.csproj'
$projectXml = [xml](Get-Content -LiteralPath $project -Raw)
$version = $projectXml.SelectSingleNode('/Project/PropertyGroup/Version').InnerText.Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Unexpected release version: $version" }
$native = Get-Item -LiteralPath $NativeDllPath
if ($native.Name -ne 'kzlStartMenuHost5.dll') { throw 'Expected kzlStartMenuHost5.dll.' }
$sevenZip = (Resolve-Path -LiteralPath $SevenZipPath).Path
$wix = (Get-Command $WixPath -CommandType Application -ErrorAction Stop).Source
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new output directory for the release.' }
$output = (New-Item -ItemType Directory -Path $OutputDirectory).FullName
$sign = @{
    CertificateThumbprint = $SigningCertificateThumbprint
    AllowSelfSigned = $AllowSelfSigned
}
if ($TimestampServer) { $sign.TimestampServer = $TimestampServer }

$light = Join-Path $output 'framework-dependent'
dotnet publish $project -c Release -r win-x64 --no-self-contained -p:PortableRuntimeLayout=true -p:NuGetAudit=false -p:DebugType=None --disable-build-servers -p:UseSharedCompilation=false -o $light
if ($LASTEXITCODE) { throw "Desktop publish failed ($LASTEXITCODE)." }
Copy-Item -LiteralPath $native.FullName,(Join-Path $PSScriptRoot 'README.md'),(Join-Path $PSScriptRoot 'README.zh-CN.md'),(Join-Path $PSScriptRoot 'LICENSE') -Destination $light
if ($SigningCertificateThumbprint) {
    & (Join-Path $PSScriptRoot 'Sign-Release.ps1') -ReleaseDirectory $light @sign | Out-Host
}

$source = Join-Path $PSScriptRoot 'installer'
$intermediate = (New-Item -ItemType Directory -Path (Join-Path $output 'installer-build')).FullName
$runtimeSetup = Join-Path $intermediate 'RuntimeSetup.exe'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$references = 'System.dll', 'System.Core.dll',
    'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll', 'System.Net.Http.dll', 'System.Web.Extensions.dll'
$referenceArgs = $references | ForEach-Object { "/reference:$(Join-Path $framework $_)" }
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$runtimeSetup" @referenceArgs (Join-Path $source 'RuntimeSetup.cs') (Join-Path $source 'RuntimePackage.cs')
if ($LASTEXITCODE) { throw "Runtime setup build failed ($LASTEXITCODE)." }
if ($SigningCertificateThumbprint) {
    & (Join-Path $PSScriptRoot 'Sign-Release.ps1') -ReleaseDirectory $intermediate -BinaryNames 'RuntimeSetup.exe' @sign | Out-Host
}

$installer = "klCustom-$version-installer-x64.msi"
$installerPath = Join-Path $output $installer
Push-Location -LiteralPath (Split-Path -Parent $wix)
try {
    & $wix build (Join-Path $source 'Installer.wxs') -arch x64 -ext WixToolset.UI.wixext/5.0.2 -d "Version=$version" -d "PayloadDir=$light" -d "RuntimeSetup=$runtimeSetup" -pdbtype none -out $installerPath
    if ($LASTEXITCODE) { throw "MSI build failed ($LASTEXITCODE)." }
}
finally { Pop-Location }
if ($SigningCertificateThumbprint) {
    & (Join-Path $PSScriptRoot 'Sign-Release.ps1') -ReleaseDirectory $output -BinaryNames $installer @sign | Out-Host
}

$layout = Join-Path $output 'archive-layout'
$portable = New-Item -ItemType Directory -Path (Join-Path $layout "klCustom-$version")
Copy-Item -Path (Join-Path $light '*') -Destination $portable.FullName -Recurse
$runtimeProcess = Start-Process -FilePath $runtimeSetup -ArgumentList ('archive "{0}"' -f $portable.FullName) -WindowStyle Hidden -Wait -PassThru
if ($runtimeProcess.ExitCode) { throw "Could not prepare the archive with its private runtime ($($runtimeProcess.ExitCode))." }
$archivePath = Join-Path $output "klCustom-$version-win-x64.7z"
Push-Location -LiteralPath $layout
try {
    & $sevenZip a -t7z -mx=9 -m0=LZMA2 -md=128m -mfb=273 -mmt=2 -ms=on -bso0 $archivePath "klCustom-$version"
    if ($LASTEXITCODE) { throw "7z compression failed ($LASTEXITCODE)." }
    & $sevenZip t -bso0 $archivePath
    if ($LASTEXITCODE) { throw "7z verification failed ($LASTEXITCODE)." }
}
finally { Pop-Location }

Get-Item -LiteralPath $archivePath,$installerPath
