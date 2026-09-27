# ════════════════════════════════════════════════════════════════════════════
#  sign-release.ps1 — Authenticode signing for the Apex Print OS release
#
#  توقيع الملف التنفيذي والمُثبِّت بشهادة Code Signing، عشان ويندوز ما يعرضش
#  تحذير "Unknown publisher" / SmartScreen عند العملاء.
#
#  الاستخدام (بشهادة مثبّتة في المخزن، بالبصمة):
#     powershell -ExecutionPolicy Bypass -File installer\sign-release.ps1 `
#         -Thumbprint  A1B2C3...  `
#         -Files       "InstallerOutput\ApexPrintOS-Setup-2.9.0.exe","Publish\ApexPrintOS.exe"
#
#  أو بملف .pfx:
#     ... -PfxPath "C:\certs\apex.pfx" -PfxPassword (Read-Host -AsSecureString)
#
#  لا يوقّع أي حاجة لو مفيش شهادة — بيقف برسالة واضحة (مفيش توقيع وهمي).
# ════════════════════════════════════════════════════════════════════════════
[CmdletBinding(DefaultParameterSetName='Store')]
param(
    [Parameter(Mandatory, ParameterSetName='Store')]
    [string]$Thumbprint,

    [Parameter(Mandatory, ParameterSetName='Pfx')]
    [string]$PfxPath,
    [Parameter(ParameterSetName='Pfx')]
    [System.Security.SecureString]$PfxPassword,

    [Parameter(Mandatory)]
    [string[]]$Files,

    # RFC-3161 timestamp server — يخلّي التوقيع صالح حتى بعد انتهاء الشهادة.
    [string]$TimestampUrl = 'http://timestamp.sectigo.com'
)

$ErrorActionPreference = 'Stop'
function ok($m){ Write-Host "  [+] $m" -ForegroundColor Green }
function err($m){ Write-Host "  [x] $m" -ForegroundColor Red; exit 1 }

# ── 1. locate signtool.exe (Windows SDK) ─────────────────────────────────────
$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe",
                          "${env:ProgramFiles(x86)}\Windows Kits\10\bin\x64\signtool.exe" `
            -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $signtool) {
    err "signtool.exe not found. Install the Windows SDK (App Certification Kit / Signing Tools)."
}
ok "signtool: $($signtool.FullName)"

# ── 2. resolve the target files ──────────────────────────────────────────────
$targets = @()
foreach ($f in $Files) {
    $full = Resolve-Path $f -ErrorAction SilentlyContinue
    if (-not $full) { err "file not found: $f" }
    $targets += $full.Path
}
ok "files to sign: $($targets.Count)"

# ── 3. build the signtool argument list ──────────────────────────────────────
# SHA-256 digest + RFC-3161 SHA-256 timestamp (the modern requirement).
$common = @('sign','/fd','SHA256','/td','SHA256','/tr',$TimestampUrl,'/v')
if ($PSCmdlet.ParameterSetName -eq 'Store') {
    $args = $common + @('/sha1',$Thumbprint)
} else {
    if (-not (Test-Path $PfxPath)) { err "pfx not found: $PfxPath" }
    $plain = ''
    if ($PfxPassword) {
        $b = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($PfxPassword)
        $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($b)
    }
    $args = $common + @('/f',$PfxPath) + $(if($plain){@('/p',$plain)}else{@()})
}

# ── 4. sign each file, then VERIFY ───────────────────────────────────────────
foreach ($t in $targets) {
    Write-Host "`nSigning: $t" -ForegroundColor Cyan
    & $signtool.FullName @args $t
    if ($LASTEXITCODE -ne 0) { err "signing failed for $t (exit $LASTEXITCODE)" }
    & $signtool.FullName verify /pa /v $t
    if ($LASTEXITCODE -ne 0) { err "verification failed for $t" }
    ok "signed + verified: $(Split-Path $t -Leaf)"
}

Write-Host "`nAll files signed and verified." -ForegroundColor Green
Write-Host "Next: run the installer on a CLEAN Windows VM and confirm no SmartScreen 'Unknown publisher'." -ForegroundColor Yellow
