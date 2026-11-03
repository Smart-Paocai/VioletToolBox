#Requires -Version 5.1
# All Git, dotnet and HTTP commands below are mocks. No repository is committed,
# pushed or queried, and no PR is created by these tests.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$fixtureRoot = Join-Path $repoRoot 'artifacts/submission-script-checks'
New-Item -ItemType Directory -Force (Join-Path $fixtureRoot 'scripts'), (Join-Path $fixtureRoot 'docs') | Out-Null
Copy-Item (Join-Path $repoRoot 'scripts/submit-localization.ps1') (Join-Path $fixtureRoot 'scripts/submit-localization.ps1') -Force
Copy-Item (Join-Path $repoRoot 'scripts/GitHubAuthentication.ps1') (Join-Path $fixtureRoot 'scripts/GitHubAuthentication.ps1') -Force
Copy-Item (Join-Path $repoRoot 'docs/pull-request.ja.md') (Join-Path $fixtureRoot 'docs/pull-request.ja.md') -Force
$originalToken = $env:GH_TOKEN
$env:GH_TOKEN = 'mock-token-for-tests-only'
$global:submissionCalls = [Collections.Generic.List[string]]::new()
$global:submissionPrCount = 0

function git {
    $arguments = @($args | ForEach-Object { [string]$_ })
    if ($arguments[0] -eq '-c') { $arguments = $arguments[2..($arguments.Count - 1)] }
    $global:submissionCalls.Add('git ' + ($arguments -join ' '))
    $global:LASTEXITCODE = 0
    switch ($arguments[0]) {
        'remote' { 'https://github.com/zsnmsl/VioletToolBox.git' }
        'config' { 'mock-identity' }
        'branch' { 'codex/localization-check' }
        'diff' { if ($arguments -contains '--quiet') { $global:LASTEXITCODE = 1 } }
    }
}
function dotnet { $global:LASTEXITCODE = 0; 'Mock build/check passed.' }
function Invoke-RestMethod {
    param($Uri, $Headers, $Method, $ContentType, $Body)
    $global:submissionCalls.Add('api ' + $Method + ' ' + $Uri)
    if ($Uri -eq 'https://api.github.com/user') { return @{ login = 'mock-user' } }
    if ($Method -eq 'Post') {
        $payload = [Text.Encoding]::UTF8.GetString($Body) | ConvertFrom-Json
        if ($payload.head -ne 'zsnmsl:codex/localization-check' -or $payload.base -ne 'main') { throw 'Wrong PR head/base.' }
        return @{ html_url = 'https://github.com/Smart-Paocai/VioletToolBox/pull/123' }
    }
    if ($Uri -like '*pulls?*') {
        $items = @()
        for ($i = 0; $i -lt $global:submissionPrCount; $i++) {
            $items += [pscustomobject]@{ html_url = "https://github.com/Smart-Paocai/VioletToolBox/pull/$(100 + $i)" }
        }
        # Match Invoke-RestMethod: emit the array itself, including an empty array.
        Write-Output -NoEnumerate ([object[]]$items)
        return
    }
    return @{
        fork = $true; default_branch = 'main'; owner = @{ login = 'zsnmsl' }
        parent = @{ full_name = 'Smart-Paocai/VioletToolBox'; default_branch = 'main' }
    }
}

try {
    foreach ($count in @(0, 1, 2)) {
        $global:submissionCalls.Clear()
        $global:submissionPrCount = $count
        $messages = @(& (Join-Path $fixtureRoot 'scripts/submit-localization.ps1') 6>&1)
        $posts = @($global:submissionCalls | Where-Object { $_.StartsWith('api Post ') })
        $expectedPosts = if ($count -eq 0) { 1 } else { 0 }
        if ($posts.Count -ne $expectedPosts) { throw "Wrong PR creation count for $count existing PRs." }
        $expectedUrl = if ($count -eq 0) { '/pull/123' } else { '/pull/100' }
        if (-not (($messages -join "`n").Contains($expectedUrl))) { throw "Missing PR URL for $count existing PRs." }
    }
    $global:submissionCalls.Clear()
    & (Join-Path $fixtureRoot 'scripts/submit-localization.ps1') -CheckOnly
    if ($global:submissionCalls | Where-Object { $_ -match '^git (add|commit|push|switch)\b|^api ' }) {
        throw 'CheckOnly attempted a submission or API operation.'
    }
    $global:submissionCalls.Clear()
    & (Join-Path $fixtureRoot 'scripts/submit-localization.ps1') -CheckAuthentication
    if ($global:submissionCalls.Count -ne 1 -or $global:submissionCalls[0] -ne 'api  https://api.github.com/user') {
        throw 'CheckAuthentication attempted an operation other than account lookup.'
    }
    Write-Host 'PASS: empty/single/multiple PR responses, visible PR URLs, CheckOnly and CheckAuthentication without submission.'
} finally {
    $env:GH_TOKEN = $originalToken
}
