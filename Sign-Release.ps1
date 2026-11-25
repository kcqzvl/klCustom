param(
    [Parameter(Mandatory)]
    [string]$ReleaseDirectory,
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$CertificateThumbprint,
    [uri]$TimestampServer,
    [switch]$AllowSelfSigned,
    [string[]]$BinaryNames = @('klCustom.exe', 'klCustom.dll', 'kzlStartMenuHost5.dll')
)

$ErrorActionPreference = 'Stop'
$release = (Resolve-Path -LiteralPath $ReleaseDirectory).Path
$binaries = foreach ($name in $BinaryNames) {
    $file = Get-Item -LiteralPath (Join-Path $release $name)
    if ($file.PSIsContainer) { throw "Expected a binary file at $($file.FullName)" }
    $file.FullName
}

$now = Get-Date
$certificate = Get-ChildItem Cert:\CurrentUser\My,Cert:\LocalMachine\My -CodeSigningCert |
    Where-Object {
        $_.Thumbprint -eq $CertificateThumbprint -and $_.HasPrivateKey -and
            $_.NotBefore -le $now -and $_.NotAfter -gt $now
    } | Select-Object -First 1
if (-not $certificate) { throw 'No current code signing certificate with an accessible private key matches that thumbprint.' }

$selfSigned = $certificate.SubjectName.Name -eq $certificate.IssuerName.Name
if ($selfSigned -and -not $AllowSelfSigned) { throw 'Specify -AllowSelfSigned to use a self-signed certificate.' }
if ($TimestampServer -and (-not $TimestampServer.IsAbsoluteUri -or $TimestampServer.Scheme -notin 'http', 'https')) {
    throw 'The timestamp server must be an absolute HTTP or HTTPS URL.'
}

$options = @{ Certificate = $certificate; HashAlgorithm = 'SHA256'; IncludeChain = 'NotRoot' }
if ($TimestampServer) { $options.TimestampServer = $TimestampServer.AbsoluteUri }

foreach ($binary in $binaries) {
    $null = Set-AuthenticodeSignature -LiteralPath $binary @options
    $signature = Get-AuthenticodeSignature -LiteralPath $binary
    $untrustedSelfSigned = $selfSigned -and $signature.Status -in 'NotTrusted', 'UnknownError'
    if ($signature.SignatureType -ne 'Authenticode' -or
        $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint -or
        ($signature.Status -ne 'Valid' -and -not $untrustedSelfSigned) -or
        ($TimestampServer -and -not $signature.TimeStamperCertificate)) {
        throw "Authenticode verification failed for $binary ($($signature.Status): $($signature.StatusMessage))"
    }
    $signature | Select-Object Path, Status, @{Name = 'Signer'; Expression = { $_.SignerCertificate.Subject }},
        @{Name = 'Timestamped'; Expression = { [bool]$_.TimeStamperCertificate }}
}
