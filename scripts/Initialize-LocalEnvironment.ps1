$ErrorActionPreference = 'Stop'
$target = Join-Path $PSScriptRoot '../.env'
if (Test-Path -LiteralPath $target) {
    throw '.env already exists. Keep the existing credentials for the persisted local database.'
}
function New-LocalSecret {
    'F1!' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
}
$values = @(
    'FLOWOPS_SA_PASSWORD=' + (New-LocalSecret)
    'FLOWOPS_DB_PASSWORD=' + (New-LocalSecret)
    'FLOWOPS_JWT_SECRET=' + [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(64))
)
[IO.File]::WriteAllLines([IO.Path]::GetFullPath($target), $values, [Text.UTF8Encoding]::new($false))
Write-Host 'Created ignored local .env credentials. Start the loopback-only stack with docker compose up --build -d.'
