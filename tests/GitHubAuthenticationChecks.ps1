#Requires -Version 5.1
# Real Git parser + temporary helper settings. No stored credentials are read.
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'scripts/GitHubAuthentication.ps1')
$oldPipelineEncoding = $OutputEncoding
$oldConsoleEncoding = [Console]::OutputEncoding
$oldInputEncoding = [Console]::InputEncoding
$oldGhToken = $env:GH_TOKEN
$oldGitHubToken = $env:GITHUB_TOKEN
try {
    # Reset inherited helpers for this command and return only dummy credentials.
    $mockArguments = '-c credential.helper= -c "credential.helper=!f() { echo username=mock-user; echo password=mock-password; }; f" credential fill'
    foreach ($encoding in @([Text.Encoding]::ASCII, (New-Object Text.UTF8Encoding($true)), [Text.Encoding]::Unicode)) {
        $OutputEncoding = $encoding
        [Console]::OutputEncoding = $encoding
        # Match the BOM-producing stdin used by .NET Framework after chcp 65001.
        [Console]::InputEncoding = New-Object Text.UTF8Encoding($true)
        $value = Get-GitCredentialToken -GitArguments $mockArguments
        if ($value -ne 'mock-password') { throw 'Credential protocol was corrupted by console encoding.' }
    }
    $env:GH_TOKEN = 'mock-gh-token'
    $env:GITHUB_TOKEN = 'mock-github-token'
    if ((Get-GitHubToken) -ne 'mock-gh-token') { throw 'GH_TOKEN precedence failed.' }
    $env:GH_TOKEN = $null
    if ((Get-GitHubToken) -ne 'mock-github-token') { throw 'GITHUB_TOKEN fallback failed.' }
} finally {
    $OutputEncoding = $oldPipelineEncoding
    [Console]::OutputEncoding = $oldConsoleEncoding
    [Console]::InputEncoding = $oldInputEncoding
    $env:GH_TOKEN = $oldGhToken
    $env:GITHUB_TOKEN = $oldGitHubToken
}
Write-Host 'PASS: real Git credential protocol with ASCII/UTF-8 BOM/UTF-16 consoles; environment token precedence.'
