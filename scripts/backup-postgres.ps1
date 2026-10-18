<#
.SYNOPSIS
    Daily PostgreSQL backup of Stockly with retention (docs/operations/backup.md).

.DESCRIPTION
    Reads the connection settings from the repository's .env (POSTGRES_DB, POSTGRES_USER, POSTGRES_PASSWORD,
    POSTGRES_HOST, POSTGRES_PORT); nothing is hard-coded and no secret is printed or put on a command line.
    -Mode Docker (default): runs pg_dump inside the compose service "postgres" (no client tools needed on the host).
    -Mode Local: runs pg_dump from PATH against POSTGRES_HOST:POSTGRES_PORT (default 127.0.0.1:5432), password through
    the PGPASSWORD environment variable of the child process only.
    Writes stockly-<db>-<UTC timestamp>.dump (custom format, compressed; restore with pg_restore) and deletes this
    script's own dumps older than -RetentionDays. Exit code 0 on success.

.EXAMPLE
    pwsh ./scripts/backup-postgres.ps1 -OutputDirectory D:\backups\stockly -RetentionDays 14
#>
param(
    [string] $EnvFile = (Join-Path (Split-Path -Parent $PSScriptRoot) '.env'),
    [string] $OutputDirectory = (Join-Path ([Environment]::GetFolderPath('UserProfile')) 'stockly-backups'),
    [ValidateRange(1, 3650)][int] $RetentionDays = 14,
    [ValidateSet('Docker', 'Local')][string] $Mode = 'Docker',
    [string] $ComposeService = 'postgres'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $EnvFile)) {
    throw "No .env at $EnvFile. Copy .env.example to .env and set the values first."
}

$settings = @{}
foreach ($line in Get-Content -LiteralPath $EnvFile) {
    if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)=(.*)$') {
        $settings[$Matches[1]] = $Matches[2].Trim().Trim('"')
    }
}

foreach ($required in 'POSTGRES_DB', 'POSTGRES_USER', 'POSTGRES_PASSWORD') {
    if ([string]::IsNullOrWhiteSpace($settings[$required])) {
        throw "$required is missing in $EnvFile."
    }
}

$database = $settings['POSTGRES_DB']
$user = $settings['POSTGRES_USER']
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$target = Join-Path $OutputDirectory "stockly-$database-$stamp.dump"
$partial = "$target.partial"

if ($Mode -eq 'Docker') {
    # pg_dump inside the container connects over the local socket; the password never leaves the container.
    $repository = Split-Path -Parent $PSScriptRoot
    $arguments = @('compose', '--project-directory', $repository, 'exec', '-T', $ComposeService,
        'pg_dump', '--format=custom', '--no-owner', '--username', $user, '--dbname', $database)
    $process = Start-Process -FilePath 'docker' -ArgumentList $arguments -NoNewWindow -Wait -PassThru `
        -RedirectStandardOutput $partial
} else {
    $hostName = if ($settings['POSTGRES_HOST']) { $settings['POSTGRES_HOST'] } else { '127.0.0.1' }
    $port = if ($settings['POSTGRES_PORT']) { $settings['POSTGRES_PORT'] } else { '5432' }
    $previous = $env:PGPASSWORD
    try {
        $env:PGPASSWORD = $settings['POSTGRES_PASSWORD']
        $arguments = @('--format=custom', '--no-owner', '--host', $hostName, '--port', $port,
            '--username', $user, '--dbname', $database, '--file', $partial)
        $process = Start-Process -FilePath 'pg_dump' -ArgumentList $arguments -NoNewWindow -Wait -PassThru
    } finally {
        $env:PGPASSWORD = $previous
    }
}

if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $partial) -or (Get-Item -LiteralPath $partial).Length -eq 0) {
    Remove-Item -LiteralPath $partial -ErrorAction SilentlyContinue
    throw "pg_dump failed (exit code $($process.ExitCode)); no backup written."
}

Move-Item -LiteralPath $partial -Destination $target
Write-Host "Backup written: $target ($([math]::Round((Get-Item -LiteralPath $target).Length / 1MB, 2)) MB)"

# Retention: only this script's own files in the output directory.
$cutoff = (Get-Date).ToUniversalTime().AddDays(-$RetentionDays)
Get-ChildItem -LiteralPath $OutputDirectory -Filter "stockly-$database-*.dump" |
    Where-Object { $_.LastWriteTimeUtc -lt $cutoff } |
    ForEach-Object {
        Remove-Item -LiteralPath $_.FullName
        Write-Host "Removed old backup: $($_.Name)"
    }
