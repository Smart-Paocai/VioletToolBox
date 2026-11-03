#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Message = 'feat: add Japanese UI localization and preserve operation identifiers',
    [string]$Title = 'Add Japanese UI localization and fix language-dependent operations',
    [switch]$CheckOnly
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$GitArgs)
    $result = & git -c core.quotepath=false @GitArgs
    if ($LASTEXITCODE -ne 0) { throw "git $($GitArgs[0]) failed (exit $LASTEXITCODE)." }
    return $result
}

Push-Location $repoRoot
try {
    foreach ($command in @('git', 'dotnet')) {
        if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
            throw "Required command not found: $command"
        }
    }
    Invoke-Git diff --check
    Invoke-Git diff --cached --check
    New-Item -ItemType Directory -Force artifacts | Out-Null
    $env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
    Write-Host 'Building Release...'
    & dotnet build VioletToolBox/SmartTool.csproj -c Release -p:GeneratePackageOnBuild=false *> artifacts/submit-build.log
    if ($LASTEXITCODE -ne 0) { throw 'Build failed. See artifacts/submit-build.log.' }
    Write-Host 'Running localization regression checks...'
    & dotnet run --project tests/LocalizationChecks/LocalizationChecks.csproj -c Release *> artifacts/submit-tests.log
    if ($LASTEXITCODE -ne 0) { throw 'Regression checks failed. See artifacts/submit-tests.log.' }
    Get-Content artifacts/submit-tests.log | Select-Object -Last 1
    if ($CheckOnly) {
        Write-Host 'Checks passed. No commit, push or PR operation was performed.'
        return
    }

    $origin = [string](Invoke-Git remote get-url origin)
    if ($origin -notmatch '^(?:https://github\.com/|git@github\.com:)([\w.-]+/[\w.-]+?)(?:\.git)?/?$') {
        throw 'origin must be a GitHub repository using HTTPS or SSH.'
    }
    $fork = $Matches[1]
    # Credentials remain in memory; nothing is printed or written to the repository.
    $token = $env:GH_TOKEN
    if (-not $token) { $token = $env:GITHUB_TOKEN }
    if (-not $token) {
        $credentialLines = "protocol=https`nhost=github.com`n`n" | & git credential fill
        if ($LASTEXITCODE -ne 0) { throw 'GitHub authentication is required. Sign in with Git Credential Manager or set GH_TOKEN.' }
        foreach ($line in $credentialLines) {
            if ($line.StartsWith('password=')) { $token = $line.Substring(9) }
        }
    }
    if (-not $token) { throw 'No GitHub API credential found. Sign in with Git Credential Manager or set GH_TOKEN.' }
    $headers = @{
        Authorization = "Bearer $token"
        Accept = 'application/vnd.github+json'
        'User-Agent' = 'VioletToolBox-submit-localization'
        'X-GitHub-Api-Version' = '2022-11-28'
    }
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $api = 'https://api.github.com'
    $forkInfo = Invoke-RestMethod -Uri "$api/repos/$fork" -Headers $headers
    if (-not $forkInfo.fork -or -not $forkInfo.parent) { throw 'origin must point to your fork.' }
    $target = $forkInfo.parent.full_name
    $base = $forkInfo.parent.default_branch
    if (-not (Invoke-Git config user.name) -or -not (Invoke-Git config user.email)) {
        throw 'Configure git user.name and user.email before submitting.'
    }

    # Only files belonging to this localization change are staged automatically.
    $paths = @(
        'VioletToolBox/MainWindow.Localization.cs',
        'VioletToolBox/MainWindow.xaml',
        'VioletToolBox/MainWindow.xaml.cs',
        'VioletToolBox/MainWindow.EdlFlash.cs',
        'VioletToolBox/oujiaflash.cs',
        'VioletToolBox/RomDownload.cs',
        'VioletToolBox/SmartTool.csproj',
        'VioletToolBox/images/android.svg',
        'VioletToolBox/images/备份.svg',
        'docs/README.ja.md',
        'docs/submit-localization.md',
        'docs/pull-request.ja.md',
        'scripts/submit-localization.ps1',
        'submit-localization.cmd',
        'tests/LocalizationChecks/LocalizationChecks.csproj',
        'tests/LocalizationChecks/Program.cs'
    )
    $unexpected = @(Invoke-Git diff --cached --name-only | Where-Object { $_ -notin $paths })
    if ($unexpected.Count) { throw "Unrelated staged files found. Unstage them first: $($unexpected -join ', ')" }
    $branch = [string](Invoke-Git branch --show-current)
    if (-not $branch) { throw 'Detached HEAD is not supported. Switch to a branch first.' }
    if ($branch -eq $forkInfo.default_branch -or $branch -eq $base) {
        $branch = 'codex/japanese-localization-' + (Get-Date -Format 'yyyyMMddHHmmss')
        Invoke-Git switch -c $branch
    }
    Invoke-Git add -- @paths
    & git diff --cached --quiet
    if ($LASTEXITCODE -eq 1) {
        Invoke-Git commit -m $Message
    } elseif ($LASTEXITCODE -ne 0) {
        throw 'Unable to inspect staged changes.'
    }
    Invoke-Git push --set-upstream origin $branch
    $head = $forkInfo.owner.login + ':' + $branch
    $headQuery = [Uri]::EscapeDataString($head)
    $baseQuery = [Uri]::EscapeDataString($base)
    $existing = @(Invoke-RestMethod -Uri "$api/repos/$target/pulls?state=open&head=$headQuery&base=$baseQuery" -Headers $headers)
    if ($existing.Count) {
        Write-Host "Updated existing PR: $($existing[0].html_url)"
    } else {
        $body = Get-Content (Join-Path $repoRoot 'docs/pull-request.ja.md') -Raw -Encoding UTF8
        $payload = @{ title = $Title; head = $head; base = $base; body = $body } | ConvertTo-Json
        $pr = Invoke-RestMethod -Method Post -Uri "$api/repos/$target/pulls" -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($payload))
        Write-Host "Created PR: $($pr.html_url)"
    }
} catch {
    Write-Host "Submission stopped: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
} finally {
    $token = $null
    $headers = $null
    Pop-Location
}
