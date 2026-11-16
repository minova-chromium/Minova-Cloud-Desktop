param([Parameter(Mandatory=$true)][string]$Manifest,[Parameter(Mandatory=$true)][string]$PublicKey,[Parameter(Mandatory=$true)][string]$Signature)
$ErrorActionPreference='Stop'
$pem=$env:MINOVA_UPDATE_SIGNING_KEY
$privateBytes=$null
if(-not $pem){
    $keyPath=Join-Path $env:LOCALAPPDATA 'Minova Cloud\Update Signing\desktop-update-key.dpapi'
    if(-not(Test-Path -LiteralPath $keyPath)){throw 'Configure MINOVA_UPDATE_SIGNING_KEY or restore the protected desktop updater signing key.'}
    $privateBytes=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($keyPath),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
    $pem=[Text.Encoding]::UTF8.GetString($privateBytes)
}
$rsa=[Security.Cryptography.RSA]::Create()
$expected=[Security.Cryptography.RSA]::Create()
try {
    $rsa.ImportFromPem($pem)
    $expected.ImportFromPem([IO.File]::ReadAllText($PublicKey))
    if([Convert]::ToBase64String($rsa.ExportSubjectPublicKeyInfo()) -ne [Convert]::ToBase64String($expected.ExportSubjectPublicKeyInfo())){throw 'The update signing key does not match the public key embedded in the app.'}
    $signed=$rsa.SignData([IO.File]::ReadAllBytes($Manifest),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
    [IO.File]::WriteAllText($Signature,[Convert]::ToBase64String($signed),[Text.Encoding]::ASCII)
} finally {
    $rsa.Dispose();$expected.Dispose();$pem=$null
    if($privateBytes){[Security.Cryptography.CryptographicOperations]::ZeroMemory($privateBytes)}
}
