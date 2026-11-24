# Create docker\.env.local with random local passwords when it does not exist (never committed; see .gitignore).
# Existing files are kept, because the PostgreSQL volume was initialized with those passwords. Keys added after a
# file was created (the RecallDB admin key and server token) are appended to an existing file.
$ErrorActionPreference = 'Stop'
$path = Join-Path $PSScriptRoot '.env.local'
function New-Secret { -join ((1..24) | ForEach-Object { '{0:x2}' -f (Get-Random -Minimum 0 -Maximum 256) }) }
if (Test-Path $path) {
  $existing = Get-Content -Path $path
  foreach ($key in @('TK_RECALLDB_ADMIN_API_KEY', 'TK_RECALLDB_TOKEN')) {
    if (-not ($existing | Where-Object { $_ -like "$key=*" })) {
      Add-Content -Path $path -Value "$key=$(New-Secret)" -Encoding ascii
      Write-Output "Added $key to docker\.env.local."
    }
  }
  exit 0
}
$stamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
@(
  "# Local stack secrets, generated $stamp. Never commit this file.",
  "TK_POSTGRES_PASSWORD=$(New-Secret)",
  "TK_DB_PASSWORD=$(New-Secret)",
  "TK_RECALLDB_DB_PASSWORD=$(New-Secret)",
  "TK_RECALLDB_ADMIN_API_KEY=$(New-Secret)",
  "TK_RECALLDB_TOKEN=$(New-Secret)",
  "TK_PLATFORM_BOOTSTRAP_ADMIN_PASSWORD=$(New-Secret)",
  "TK_SECRETS_ENCRYPTION_KEY=$([Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)))"
) | Set-Content -Path $path -Encoding ascii
Write-Output 'Created docker\.env.local with random local passwords.'
