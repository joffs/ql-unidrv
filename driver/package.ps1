<#
.SYNOPSIS
  Stages the built driver (..\out\driver: GPDs, INIs, INF; ..\out\arm64 and/or ..\out\x64: the plug-in)
  into ..\out\package (plug-ins in arm64\ and amd64\), creates the catalog and signs it. The package holds
  the architectures build.cmd built last (..\out\driver\architectures.txt), or -Arch x64|arm64|both; a
  package for one architecture lists only that one in the INF.
  Signing uses a private CA: a self-signed root certificate, restricted to code signing (EKU), that
  issues the code-signing certificate (both created on first run in CurrentUser\My). install.ps1
  puts the root into LocalMachine\Root and the signer into LocalMachine\TrustedPublisher. Windows on
  ARM64 accepts such a package only while driver signature enforcement is off (test mode or a
  one-time "Disable driver signature enforcement" boot); the installed driver then also runs with
  Secure Boot on. Run build.cmd first. No admin rights needed.
  -Stage: only stage the unsigned package (for a release zip: the user signs it on their own PC).
  -SignOnly: only sign the package already in ..\out\package (the release zip's Setup.exe uses this; needs
  nothing but Windows).
  -OneTimeKeys (release installs): a new root CA and signer for this package only; both private keys are
  deleted right after signing, so nothing on the PC can sign code it trusts later (a developer's own pair
  on the same PC is not touched). The next update brings a new pair; install.ps1 replaces the old certificates.
  Without it (developer PC) the pair stays for the next build; both keys are non-exportable (a signer
  with an exportable key is replaced on the next run).
  -Out <folder>: the package folder (default ..\out\package; tools\make-release.ps1 stages elsewhere).
  Signing needs no Windows SDK: the catalog comes from
  wintrust.dll's CDF functions (what makecat calls, same CDF, identical catalog), the signatures from
  Set-AuthenticodeSignature (SHA256). Installs on x64 with signature enforcement on.
#>
param(
    [string]$RootSubject = "CN=QL Unidrv driver root CA (code signing)",
    [string]$Subject = "CN=QL Unidrv driver",
    [ValidateSet("", "x64", "arm64", "both")][string]$Arch = "",
    [switch]$Stage,
    [switch]$SignOnly,
    [switch]$OneTimeKeys,
    [string]$Out
)
$ErrorActionPreference = "Stop"
# Started as 32-bit PowerShell on a 64-bit Windows (from a 32-bit program): run again in the native PowerShell, so
# System32, the registry and the driver tools are the real ones. Only strings and switches here.
if ([IntPtr]::Size -eq 4 -and $env:PROCESSOR_ARCHITEW6432) {
    $native = Join-Path $env:WINDIR "Sysnative\WindowsPowerShell\v1.0\powershell.exe"
    $argv = foreach ($k in $PSBoundParameters.Keys) {
        $v = $PSBoundParameters[$k]
        if ($v -is [System.Management.Automation.SwitchParameter]) { if ($v) { "-$k" } } else { "-$k"; "$v" }
    }
    & $native -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath @argv
    exit $LASTEXITCODE
}
$root = Split-Path $PSScriptRoot -Parent
$pkg = if ($Out) { [IO.Path]::GetFullPath($Out) } else { Join-Path $root "out\package" }
$infArch = @{ arm64 = "arm64"; x64 = "amd64" }   # package folder and INF decoration per architecture
# Staging: the built files into out\package, with a new DriverVer
if (-not $SignOnly) {
    $build = Join-Path $root "out\driver"
    if (-not (Test-Path (Join-Path $build "qlunidrv.inf"))) { throw "Nothing built yet: run build.cmd first." }
    $want = if ($Arch) { $Arch } else { (Get-Content (Join-Path $build "architectures.txt") -ErrorAction SilentlyContinue) -join " " }
    $archs = @(if ($want -eq "both") { "arm64", "x64" } else { -split $want })
    if (-not $archs) { throw "Unknown architecture: run build.cmd first or pass -Arch x64|arm64|both." }
    foreach ($a in $archs) {
        if (-not (Test-Path (Join-Path $root "out\$a\QLUnidrv.dll"))) { throw "out\$a\QLUnidrv.dll missing: run build.cmd $a (or both) first." }
    }

    $files = "qlunidrv.inf", "QLUnidrv500.GPD", "QLUnidrv500.INI", "QLUnidrv550.GPD", "QLUnidrv550.INI",
             "QLUnidrv560.GPD", "QLUnidrv560.INI", "QLUnidrv570.GPD", "QLUnidrv570.INI", "QLUnidrv580.GPD", "QLUnidrv580.INI",
             "QLUnidrv600.GPD", "QLUnidrv600.INI", "QLUnidrv650.GPD", "QLUnidrv650.INI",
             "QLUnidrv700.GPD", "QLUnidrv700.INI", "QLUnidrv710.GPD", "QLUnidrv710.INI", "QLUnidrv720.GPD", "QLUnidrv720.INI",
             "QLUnidrv800.GPD", "QLUnidrv800.INI", "QLUnidrv810.GPD", "QLUnidrv810.INI", "QLUnidrv820.GPD", "QLUnidrv820.INI",
             "QLUnidrv1050.GPD", "QLUnidrv1050.INI", "QLUnidrv1060.GPD", "QLUnidrv1060.INI",
             "QLUnidrv1100.GPD", "QLUnidrv1100.INI", "QLUnidrv1110.GPD", "QLUnidrv1110.INI", "QLUnidrv1115.GPD", "QLUnidrv1115.INI"
    $dlls = @($archs | ForEach-Object { "$($infArch[$_])\QLUnidrv.dll" })   # INF: SourceDisksNames.<arch>, disk 2
    if (Test-Path $pkg) { Remove-Item $pkg -Recurse -Force }
    New-Item -ItemType Directory $pkg | Out-Null
    foreach ($f in $files) { Copy-Item (Join-Path $build $f) $pkg }
    foreach ($a in $archs) {
        New-Item -ItemType Directory (Join-Path $pkg $infArch[$a]) | Out-Null
        Copy-Item (Join-Path $root "out\$a\QLUnidrv.dll") (Join-Path $pkg $infArch[$a])
    }
    $files += $dlls

    # The driver version is build.cmd's (driver\gen\version.py: driver\VERSION, or a development stamp while the
    # sources differ from it); identical sources give an identical DriverVer.
    $infPath = Join-Path $pkg "qlunidrv.inf"
    $inf = Get-Content $infPath -Raw
    if ($inf -notmatch "DriverVer=[^,]+,(\d+\.\d+\.\d+\.\d+)") { throw "DriverVer not found in qlunidrv.inf" }
    $version = $Matches[1]
    # A package for one architecture: only it in [Manufacturer], and the other one's sections
    # ([Brother.NTamd64], [SourceDisksFiles.amd64], ...) removed.
    $decorations = ($archs | ForEach-Object { "NT" + $infArch[$_] }) -join ","
    if ($inf -notmatch "%Mfg%=Brother,NTarm64,NTamd64") { throw "[Manufacturer] line not found in qlunidrv.inf" }
    $inf = $inf -replace "%Mfg%=Brother,NTarm64,NTamd64", "%Mfg%=Brother,$decorations"
    foreach ($a in "arm64", "x64") {
        if ($archs -contains $a) { continue }
        $inf = [regex]::Replace($inf, "(?ms)^\[[^\]\r\n]+\.(NT)?$($infArch[$a])\]\r?\n.*?(?=^\[|\z)", "")
    }
    [IO.File]::WriteAllText($infPath, $inf, [Text.Encoding]::ASCII)
    "Driver version $version for $($archs -join ' + ')"
}
if ($Stage) {
    "Unsigned package staged: $pkg"
    return
}

# Signing: the package in out\package as it is (staged above or unpacked from a release zip)
if (-not (Test-Path (Join-Path $pkg "qlunidrv.inf"))) { throw "No package in $pkg to sign." }
Get-ChildItem $pkg -File | Where-Object Extension -in ".cat", ".cer" | Remove-Item
$files = @(Get-ChildItem $pkg -Recurse -File | ForEach-Object { $_.FullName.Substring($pkg.Length + 1) })
$dlls = @($files | Where-Object { $_ -like "*\QLUnidrv.dll" })
if (-not $dlls) { throw "No plug-in (arm64\ or amd64\QLUnidrv.dll) in $pkg." }
# makecat's work: wintrust.dll reads the CDF; enumerating the catalog attributes and the members adds them,
# closing writes the catalog.
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class QlCatalog {
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    delegate void ParseError(uint area, uint localError, IntPtr line);
    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CryptCATCDFOpen(string path, ParseError onError);
    [DllImport("wintrust.dll", SetLastError = true)]
    static extern IntPtr CryptCATCDFEnumCatAttributes(IntPtr cdf, IntPtr prevAttr, ParseError onError);
    [DllImport("wintrust.dll", SetLastError = true)]
    static extern IntPtr CryptCATCDFEnumMembersByCDFTagEx(IntPtr cdf, IntPtr prevTag, ParseError onError, out IntPtr member, bool continueOnError, IntPtr reserved);
    [DllImport("wintrust.dll", SetLastError = true)]
    static extern bool CryptCATCDFClose(IntPtr cdf);
    static readonly List<string> errors = new List<string>();
    static readonly ParseError onError = (area, error, line) =>
        errors.Add(string.Format("0x{0:X}/0x{1:X}: {2}", area, error, Marshal.PtrToStringUni(line)));
    // Returns the number of catalog members.
    public static int Make(string cdfPath) {
        errors.Clear();
        IntPtr cdf = CryptCATCDFOpen(cdfPath, onError);
        if (cdf == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "CryptCATCDFOpen: " + string.Join("; ", errors));
        int n = 0;
        try {
            IntPtr attr = IntPtr.Zero, tag = IntPtr.Zero, member;
            while ((attr = CryptCATCDFEnumCatAttributes(cdf, attr, onError)) != IntPtr.Zero) { }
            while ((tag = CryptCATCDFEnumMembersByCDFTagEx(cdf, tag, onError, out member, true, IntPtr.Zero)) != IntPtr.Zero) n++;
        } finally {
            if (!CryptCATCDFClose(cdf)) errors.Add("CryptCATCDFClose: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
        }
        if (errors.Count > 0) throw new Exception("Catalog: " + string.Join("; ", errors));
        return n;
    }
}
'@
function Sign-File([string]$path, $cert) {
    $s = Set-AuthenticodeSignature -FilePath $path -Certificate $cert -HashAlgorithm SHA256
    # Not "Valid" before install.ps1 trusts the root; only a missing signature is a failure.
    if (-not $s.SignerCertificate -or $s.SignerCertificate.Thumbprint -ne $cert.Thumbprint) {
        throw "Signing $(Split-Path $path -Leaf) failed: $($s.StatusMessage)"
    }
}

# Certificates: root CA + code-signing certificate issued by it
function Remove-CertAndKey($c) { Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($c.Thumbprint)" -DeleteKey }
function Test-KeyExportable($c) {
    $k = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($c)
    if ($k -is [Security.Cryptography.RSACng]) {
        return [bool]($k.Key.ExportPolicy -band ([Security.Cryptography.CngExportPolicies]::AllowExport -bor [Security.Cryptography.CngExportPolicies]::AllowPlaintextExport))
    }
    if ($k -is [Security.Cryptography.RSACryptoServiceProvider]) { return $k.CspKeyContainerInfo.Exportable }
    return $false
}
# -OneTimeKeys: a fresh pair, marked by its friendly name, so only such pairs are ever deleted (leftovers of
# an interrupted run too) and a developer's own pair on the same PC stays.
$oneTimeName = "QL Unidrv one-time signing"
if ($OneTimeKeys) {
    $old = @(Get-ChildItem Cert:\CurrentUser\My | Where-Object FriendlyName -eq $oneTimeName)
    foreach ($c in $old) { Remove-CertAndKey $c; Remove-Item -LiteralPath "Cert:\CurrentUser\CA\$($c.Thumbprint)" -ErrorAction SilentlyContinue }
    if ($old) { "Removed $($old.Count) certificate(s) of an interrupted install from your user account." }
    # Copies of earlier one-time roots among the intermediate CAs: a root of this
    # name whose key is not in this user's store any more can't sign anything; its copy is only clutter.
    $roots = @(Get-ChildItem Cert:\CurrentUser\My | Where-Object Subject -eq $RootSubject | ForEach-Object Thumbprint)
    Get-ChildItem Cert:\CurrentUser\CA | Where-Object { $_.Subject -eq $RootSubject -and $roots -notcontains $_.Thumbprint } | Remove-Item -ErrorAction SilentlyContinue
    $rootCert = $null; $cert = $null
} else {
    $rootCert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $RootSubject -and $_.FriendlyName -ne $oneTimeName } |
        Sort-Object NotAfter -Descending | Select-Object -First 1
}
$friendly = @{}
if ($OneTimeKeys) { $friendly.FriendlyName = $oneTimeName }
if (-not $rootCert) {
    $rootCert = New-SelfSignedCertificate -Type Custom -Subject $RootSubject @friendly `
        -CertStoreLocation Cert:\CurrentUser\My -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 `
        -KeyUsage CertSign, CRLSign, DigitalSignature -KeyExportPolicy NonExportable `
        -TextExtension @("2.5.29.19={critical}{text}ca=1&pathlength=0", "2.5.29.37={text}1.3.6.1.5.5.7.3.3") `
        -NotAfter (Get-Date).AddYears(15)
    "Created root CA certificate $($rootCert.Thumbprint)"
}
if (-not $OneTimeKeys) {
    $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object {
        $_.Subject -eq $Subject -and $_.Issuer -eq $RootSubject -and $_.FriendlyName -ne $oneTimeName } |
        Sort-Object NotAfter -Descending | Select-Object -First 1
    if ($cert -and (Test-KeyExportable $cert)) {
        Remove-CertAndKey $cert
        "Replaced signing certificate $($cert.Thumbprint): its private key was exportable."
        $cert = $null
    }
}
if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $Subject -Signer $rootCert @friendly `
        -CertStoreLocation Cert:\CurrentUser\My -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 `
        -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddYears(10)
    "Created signing certificate $($cert.Thumbprint)"
}
# -OneTimeKeys: the private keys are deleted after signing, also when signing fails.
try {
    Export-Certificate -Cert $rootCert -FilePath (Join-Path $pkg "qlunidrv-root.cer") | Out-Null
    Export-Certificate -Cert $cert -FilePath (Join-Path $pkg "qlunidrv.cer") | Out-Null

    # Sign the plug-in DLLs, then catalog over all package files
    foreach ($d in $dlls) { Sign-File (Join-Path $pkg $d) $cert }

    $cdf = @("[CatalogHeader]", "Name=qlunidrv.cat", "ResultDir=$pkg", "PublicVersion=0x0000001",
             "EncodingType=0x00010001", "CATATTR1=0x10010001:OSAttr:2:10.0", "", "[CatalogFiles]")
    foreach ($f in $files) { $cdf += "<hash>$f=$(Join-Path $pkg $f)" }
    $cdfPath = Join-Path (Split-Path $pkg -Parent) "qlunidrv.cdf"
    $cdf | Set-Content -Encoding ascii $cdfPath
    [void][QlCatalog]::Make($cdfPath)
    Sign-File (Join-Path $pkg "qlunidrv.cat") $cert
} finally {
    if ($OneTimeKeys) {
        # The .cer files in the package are all install.ps1 needs; without the private keys nothing can sign
        # with this CA any more.
        Remove-CertAndKey $cert
        Remove-CertAndKey $rootCert
        # Windows also files the root among this user's intermediate CAs when it issues the signer: one copy per run.
        # Only this run's copy goes; a developer's own root stays.
        Remove-Item -LiteralPath "Cert:\CurrentUser\CA\$($rootCert.Thumbprint)" -ErrorAction SilentlyContinue
        "Private keys deleted after signing (a new pair is made for the next update)."
    }
}

"Package ready: $pkg"
Get-ChildItem $pkg -Recurse -File | Select-Object @{ n = "Name"; e = { $_.FullName.Substring($pkg.Length + 1) } }, Length | Format-Table -AutoSize | Out-String
