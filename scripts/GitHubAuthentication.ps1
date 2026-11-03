#Requires -Version 5.1

function Get-GitCredentialToken {
    # GitArguments is overridable for regression tests using an in-memory fake
    # credential helper. Normal callers always use the default command.
    param([string]$GitArguments = 'credential fill')

    $gitPath = (Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $gitPath
    $info.Arguments = $GitArguments
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = New-Object System.Text.UTF8Encoding($false)
    $info.StandardErrorEncoding = New-Object System.Text.UTF8Encoding($false)
    $info.EnvironmentVariables['GIT_TERMINAL_PROMPT'] = '0'
    $info.EnvironmentVariables['GCM_INTERACTIVE'] = 'never'
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $info
    $originalInputEncoding = [Console]::InputEncoding
    $restoreInputEncoding = $false
    try {
        $noBomUtf8 = New-Object System.Text.UTF8Encoding($false)
        if ($info.GetType().GetProperty('StandardInputEncoding')) {
            $info.StandardInputEncoding = $noBomUtf8
        } else {
            # .NET Framework uses Console.InputEncoding and enables AutoFlush when
            # starting the process. Under chcp 65001 this can emit a BOM before
            # our first byte, even when writing through BaseStream afterwards.
            $restoreInputEncoding = $true
            [Console]::InputEncoding = $noBomUtf8
        }
        if (-not $process.Start()) { throw 'Unable to start Git credential lookup.' }
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()
        # Bypass PowerShell's native-command pipeline and its console encoding.
        # Git requires literal protocol/host fields followed by a blank line.
        $request = [Text.Encoding]::ASCII.GetBytes("protocol=https`nhost=github.com`n`n")
        $inputStream = $process.StandardInput.BaseStream
        $inputStream.Write($request, 0, $request.Length)
        $inputStream.Flush()
        $inputStream.Close()
        if (-not $process.WaitForExit(30000)) {
            $process.Kill()
            throw 'Git credential lookup timed out. Sign in with Git Credential Manager or set GH_TOKEN.'
        }
        $credentialOutput = $outputTask.GetAwaiter().GetResult()
        # Consume stderr without echoing credential helper output to logs.
        $null = $errorTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw "Git credential lookup failed (exit $($process.ExitCode)). Sign in with Git Credential Manager or set GH_TOKEN."
        }
        foreach ($line in ($credentialOutput -split '\r?\n')) {
            if ($line.StartsWith('password=', [StringComparison]::Ordinal)) { return $line.Substring(9) }
        }
        throw 'No GitHub API credential found. Sign in with Git Credential Manager or set GH_TOKEN.'
    } finally {
        $credentialOutput = $null
        $process.Dispose()
        if ($restoreInputEncoding) { [Console]::InputEncoding = $originalInputEncoding }
    }
}

function Get-GitHubToken {
    if ($env:GH_TOKEN) { return $env:GH_TOKEN }
    if ($env:GITHUB_TOKEN) { return $env:GITHUB_TOKEN }
    return Get-GitCredentialToken
}
