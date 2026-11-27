# Prosequor test runner: version header, live X/Y progress, short failure cards.
# Full stacks and build warnings go to tests.log. Console keeps the short form.
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

$RepoRoot = $PSScriptRoot
$script:LogPath = Join-Path $RepoRoot 'tests.log'
$LogPath = $script:LogPath
$Solution = Join-Path $RepoRoot 'prosequor.slnx'
$ModInfoPath = Join-Path $RepoRoot 'modinfo.json'
$DllPath = Join-Path $RepoRoot 'bin\Debug\Mods\mod\prosequor.dll'
$PureProject = Join-Path $RepoRoot 'tests\Prosequor.Pure.Tests\Prosequor.Pure.Tests.csproj'
$ScenarioProject = Join-Path $RepoRoot 'tests\Prosequor.Scenarios\Prosequor.Scenarios.csproj'

$script:CurrentProcess = $null
$script:Interrupted = $false
$script:PhaseName = ''
$script:PhaseDone = 0
$script:PhaseTotal = 0
$script:LastClassKey = ''
$script:LastClassDone = 0
$script:LastClassTotal = 0
$script:AnyFailed = $false

# ---------------------------------------------------------------------------
# Output
# ---------------------------------------------------------------------------

function Write-Both {
    param([string]$Text)
    Add-Content -LiteralPath $script:LogPath -Value $Text
    Write-Host $Text
}

function Write-LogOnly {
    param([string]$Text)
    Add-Content -LiteralPath $script:LogPath -Value $Text
}

function Write-ConsoleOnly {
    param([string]$Text)
    Write-Host $Text
}

# ---------------------------------------------------------------------------
# Formatting helpers
# ---------------------------------------------------------------------------

function Get-ShortClassName {
    param([string]$ClassName)
    $n = $ClassName
    if ($n -match 'Scenarios$') { return $n.Substring(0, $n.Length - 'Scenarios'.Length) }
    if ($n -match 'Fixtures$') { return $n.Substring(0, $n.Length - 'Fixtures'.Length) }
    if ($n -match 'Tests$') { return $n.Substring(0, $n.Length - 'Tests'.Length) }
    return $n
}

function ConvertFrom-VsTestDuration {
    param([string]$Text)
    $t = $Text.Trim()
    if ($t -match '<\s*1\s*ms') { return [TimeSpan]::FromMilliseconds(0.5) }

    $ms = 0.0
    if ($t -match '(\d+)\s*h') { $ms += [double]$Matches[1] * 3600000 }
    if ($t -match '(\d+)\s*m(?!\s*s)') { $ms += [double]$Matches[1] * 60000 }
    if ($t -match '(\d+)\s*s(?!\w)') { $ms += [double]$Matches[1] * 1000 }
    if ($t -match '(\d+)\s*ms') { $ms += [double]$Matches[1] }
    if ($ms -le 0) { return [TimeSpan]::Zero }
    return [TimeSpan]::FromMilliseconds($ms)
}

function Format-DurationShort {
    param([TimeSpan]$Duration)
    if ($Duration.TotalSeconds -lt 1) {
        return ('{0}ms' -f [math]::Max(1, [int][math]::Round($Duration.TotalMilliseconds)))
    }
    if ($Duration.TotalSeconds -lt 60) {
        return ('{0}s' -f [math]::Round($Duration.TotalSeconds))
    }
    $m = [int][math]::Floor($Duration.TotalMinutes)
    $s = [int][math]::Round($Duration.TotalSeconds - ($m * 60))
    if ($s -le 0) { return ('{0}m' -f $m) }
    return ('{0}m {1}s' -f $m, $s)
}

function Format-Eta {
    param([TimeSpan]$Remaining)
    if ($Remaining.TotalSeconds -lt 60) {
        return ('~{0}s left' -f [math]::Max(1, [int][math]::Round($Remaining.TotalSeconds)))
    }
    $m = [int][math]::Round($Remaining.TotalMinutes)
    return ('~{0}m left' -f $m)
}

function Split-TestName {
    param([string]$FqName)
    $base = $FqName.Trim()
    if ($base -match '^(?<head>.+?)\(') { $base = $Matches['head'] }
    $parts = $base.Split('.')
    if ($parts.Length -lt 2) {
        return [pscustomobject]@{
            Class        = $base
            Method       = $base
            DisplayClass = $base
            Key          = $base
            Fq           = $FqName
        }
    }
    $method = $parts[-1]
    $class = $parts[-2]
    return [pscustomobject]@{
        Class        = $class
        Method       = $method
        DisplayClass = (Get-ShortClassName $class)
        Key          = "$class.$method"
        Fq           = $FqName
    }
}

function Find-StackFileLine {
    param([System.Collections.Generic.List[string]]$Stack)
    foreach ($st in $Stack) {
        if ($st -match '(?i)(?<file>[^\s"]*?tests[\\/][^\s"]+\.cs):line\s+(?<line>\d+)') {
            return ('{0}:{1}' -f ([System.IO.Path]::GetFileName($Matches['file'])), $Matches['line'])
        }
        if ($st -match '(?i)(?<file>[^\s"]*?tests[\\/][^\s"]+\.cs)\((?<line>\d+),') {
            return ('{0}:{1}' -f ([System.IO.Path]::GetFileName($Matches['file'])), $Matches['line'])
        }
    }
    return $null
}

function Write-FailureCard {
    param(
        [string]$PhaseName,
        [object]$Info,
        [System.Collections.Generic.List[string]]$Messages,
        [System.Collections.Generic.List[string]]$Stack,
        [hashtable]$TraitMap,
        [hashtable]$ClassTotals,
        [hashtable]$ClassDone
    )

    $msgLines = @($Messages | Where-Object { $_.Trim().Length -gt 0 })
    $sentence = if ($msgLines.Count -gt 0) { ($msgLines -join ' ').Trim() } else { '(no assertion message)' }
    $fileLine = Find-StackFileLine -Stack $Stack

    $traitText = ''
    if ($TraitMap.ContainsKey($Info.Key)) {
        $traits = $TraitMap[$Info.Key]
        $bits = @()
        if ($traits.Layer) { $bits += $traits.Layer }
        if ($traits.Kind) { $bits += $traits.Kind }
        if ($bits.Count -gt 0) { $traitText = '    ' + ($bits -join ' / ') }
    }

    $classTotal = 0
    if ($ClassTotals.ContainsKey($Info.Class)) { $classTotal = [int]$ClassTotals[$Info.Class] }
    $classFinished = 0
    if ($ClassDone.ContainsKey($Info.Class)) { $classFinished = [int]$ClassDone[$Info.Class] }
    $classLeft = [math]::Max(0, $classTotal - $classFinished)
    $phaseLeft = [math]::Max(0, $script:PhaseTotal - $script:PhaseDone)

    Write-Both ''
    Write-Both ('FAIL   {0}  {1}/{2}' -f $PhaseName, $script:PhaseDone, $script:PhaseTotal)
    Write-Both ('       {0}  {1}/{2}{3}' -f $Info.DisplayClass, $classFinished, $classTotal, $traitText)
    Write-Both ('       {0}' -f $Info.Method)
    Write-Both ('       {0}' -f $sentence)
    if ($fileLine) { Write-Both ('       {0}' -f $fileLine) }
    Write-Both ('       {0} left in this class, {1} left in {2}' -f $classLeft, $phaseLeft, $PhaseName)
    Write-Both '       stack follows in tests.log'
    Write-Both ''
}

# ---------------------------------------------------------------------------
# Trait map: Class.Method -> Kind / Layer from [Trait(...)] above the method
# ---------------------------------------------------------------------------

function Get-TraitMap {
    param([string]$TestRoot)
    $map = @{}
    if (-not (Test-Path -LiteralPath $TestRoot)) { return $map }

    Get-ChildItem -LiteralPath $TestRoot -Recurse -Filter '*.cs' | ForEach-Object {
        $class = $null
        $kind = $null
        $layer = $null
        foreach ($raw in [System.IO.File]::ReadLines($_.FullName)) {
            $line = $raw
            if ($line -match '(?:^|\s)(?:public\s+|internal\s+|abstract\s+|sealed\s+|partial\s+)*class\s+(?<c>\w+)') {
                $class = $Matches['c']
                $kind = $null
                $layer = $null
                continue
            }
            if ($line -match '\[Trait\(\s*"Kind"\s*,\s*"(?<v>[^"]+)"\s*\)\]') {
                $kind = $Matches['v']
            }
            if ($line -match '\[Trait\(\s*"Layer"\s*,\s*"(?<v>[^"]+)"\s*\)\]') {
                $layer = $Matches['v']
            }
            if ($null -eq $class) { continue }
            if ($line -match '(?:public|internal)\s+(?:static\s+)?(?:async\s+)?(?:Task|ValueTask|void)\s+(?<m>\w+)\s*\(') {
                if ($kind -or $layer) {
                    $map["$class.$($Matches['m'])"] = @{
                        Kind  = $kind
                        Layer = $layer
                    }
                }
                $kind = $null
                $layer = $null
            }
        }
    }
    return $map
}

# ---------------------------------------------------------------------------
# List tests
# ---------------------------------------------------------------------------

function Get-ListedTests {
    param([string]$Project)
    $lines = & dotnet test $Project --list-tests --no-build --nologo 2>&1
    $tests = New-Object System.Collections.Generic.List[string]
    $started = $false
    foreach ($line in $lines) {
        $s = "$line"
        Write-LogOnly $s
        if ($s -match 'The following Tests are available') {
            $started = $true
            continue
        }
        if (-not $started) { continue }
        if ($s -match '^\s+(\S.+)$') {
            [void]$tests.Add($Matches[1].Trim())
        }
    }
    return $tests
}

function Get-ClassTotals {
    param([System.Collections.Generic.List[string]]$Tests)
    $totals = @{}
    foreach ($t in $Tests) {
        $info = Split-TestName $t
        if (-not $totals.ContainsKey($info.Class)) {
            $totals[$info.Class] = 0
        }
        $totals[$info.Class] = [int]$totals[$info.Class] + 1
    }
    return $totals
}

function Write-InterruptedLine {
    if (-not $script:PhaseName) {
        Write-Both 'interrupted'
        return
    }
    $cls = '-'
    if ($script:LastClassKey) {
        $classPart = $script:LastClassKey.Split('.')[0]
        $short = Get-ShortClassName $classPart
        $cls = ('{0} {1}/{2}' -f $short, $script:LastClassDone, $script:LastClassTotal)
    }
    Write-Both ('interrupted   {0} {1}/{2}   {3}' -f `
        $script:PhaseName, $script:PhaseDone, $script:PhaseTotal, $cls)
}

# ---------------------------------------------------------------------------
# Process streaming (file redirect + poll — reliable on Windows PowerShell 5.1)
# ---------------------------------------------------------------------------

function Read-NewProcessLines {
    param(
        [string]$Path,
        [ref]$Offset,
        [scriptblock]$OnLine
    )

    if (-not (Test-Path -LiteralPath $Path)) { return }

    $fs = $null
    try {
        $fs = [System.IO.File]::Open(
            $Path,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::ReadWrite)
        if ($Offset.Value -gt $fs.Length) { $Offset.Value = 0 }
        [void]$fs.Seek($Offset.Value, [System.IO.SeekOrigin]::Begin)
        $reader = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8, $true, 4096, $true)
        while ($null -ne ($line = $reader.ReadLine())) {
            try { & $OnLine $line } catch {
                Write-Both ("runner parse error: {0}" -f $_.Exception.Message)
                Write-LogOnly ($_ | Out-String)
            }
        }
        $Offset.Value = $fs.Position
        $reader.Dispose()
    } catch {
        # Writer may lock briefly; try again next poll.
    } finally {
        if ($null -ne $fs) { $fs.Dispose() }
    }
}

function Stop-TestProcessTree {
    param($Process)
    if ($null -eq $Process) { return }
    try {
        if (-not $Process.HasExited) {
            # /T kills the testhost children with the dotnet parent.
            & taskkill.exe /PID $Process.Id /T /F 2>$null | Out-Null
        }
    } catch { }
    try {
        if (-not $Process.HasExited) { $Process.Kill() }
    } catch { }
}

# ---------------------------------------------------------------------------
# Ctrl+C (after Stop-TestProcessTree so the handler can call it)
# ---------------------------------------------------------------------------

$cancelHandler = $null
try {
    $cancelHandler = [ConsoleCancelEventHandler] {
        param($sender, $e)
        $e.Cancel = $true
        $script:Interrupted = $true
        Stop-TestProcessTree -Process $script:CurrentProcess
    }
    [Console]::add_CancelKeyPress($cancelHandler)
} catch {
    # Non-interactive hosts may lack a console; Ctrl+C handling is best-effort.
    $cancelHandler = $null
}

function Invoke-DotNetStreaming {
    param(
        [string]$ArgumentString,
        [scriptblock]$OnLine
    )

    $tag = [guid]::NewGuid().ToString('N').Substring(0, 8)
    $outFile = Join-Path $env:TEMP ("prosequor-test-out-{0}.txt" -f $tag)
    $errFile = Join-Path $env:TEMP ("prosequor-test-err-{0}.txt" -f $tag)
    Set-Content -LiteralPath $outFile -Value '' -Encoding UTF8
    Set-Content -LiteralPath $errFile -Value '' -Encoding UTF8

    # Single argument string: Start-Process array form splits paths that contain spaces.
    $proc = Start-Process -FilePath 'dotnet' `
        -ArgumentList $ArgumentString `
        -WorkingDirectory $RepoRoot `
        -RedirectStandardOutput $outFile `
        -RedirectStandardError $errFile `
        -NoNewWindow `
        -PassThru
    $script:CurrentProcess = $proc

    $outPos = 0L
    $errPos = 0L

    while ($null -ne $proc -and -not $proc.HasExited) {
        if ($script:Interrupted) {
            Stop-TestProcessTree -Process $proc
            break
        }
        Read-NewProcessLines -Path $outFile -Offset ([ref]$outPos) -OnLine $OnLine
        Read-NewProcessLines -Path $errFile -Offset ([ref]$errPos) -OnLine $OnLine
        Start-Sleep -Milliseconds 100
    }

    try { if ($null -ne $proc -and -not $proc.HasExited) { [void]$proc.WaitForExit(15000) } } catch { }
    if ($null -ne $proc -and -not $proc.HasExited) {
        Stop-TestProcessTree -Process $proc
        try { [void]$proc.WaitForExit(5000) } catch { }
    }

    # Final drain after process exit (buffers flush).
    Start-Sleep -Milliseconds 50
    Read-NewProcessLines -Path $outFile -Offset ([ref]$outPos) -OnLine $OnLine
    Read-NewProcessLines -Path $errFile -Offset ([ref]$errPos) -OnLine $OnLine

    $code = 1
    try { if ($null -ne $proc) { $code = $proc.ExitCode } } catch { $code = 1 }
    if ($null -eq $code) { $code = 1 }
    if ($script:Interrupted) { $code = 130 }

    $script:CurrentProcess = $null
    try { if ($null -ne $proc) { $proc.Dispose() } } catch { }
    Remove-Item -LiteralPath $outFile, $errFile -Force -ErrorAction SilentlyContinue
    return [int]$code
}

# ---------------------------------------------------------------------------
# Phase runner
# ---------------------------------------------------------------------------

# Line callback reads $script:PhaseState so Process streaming can call a named
# function without GetNewClosure isolating script helpers.
function Handle-TestOutputLine {
    param([string]$Line)

    $s = "$Line"
    Write-LogOnly $s
    if ($script:Interrupted) { return }

    $st = $script:PhaseState
    if ($null -eq $st) { return }

    $isResultLine = $s -match '^\s+(Passed|Failed|Skipped)\s+'
    $isSummaryLine = ($s -match '^Test Run ') -or ($s -match '^Total tests:')

    if (($null -ne $st.FailState) -and ($isResultLine -or $isSummaryLine)) {
        $fs = $st.FailState
        $st.FailState = $null
        Write-FailureCard `
            -PhaseName $st.PhaseName `
            -Info $fs.Info `
            -Messages $fs.Messages `
            -Stack $fs.Stack `
            -TraitMap $st.TraitMap `
            -ClassTotals $st.ClassTotals `
            -ClassDone $st.ClassDone
    }

    if ($null -ne $st.FailState) {
        $fs = $st.FailState
        if ($s -match '^\s+Error Message:\s*$') {
            $fs.Mode = 'message'
            return
        }
        if ($s -match '^\s+Stack Trace:\s*$') {
            $fs.Mode = 'stack'
            return
        }
        if ($fs.Mode -eq 'message' -and $s -match '^\s{2,}(\S.*)$') {
            if ($Matches[1] -notmatch '^(Error Message|Stack Trace):') {
                [void]$fs.Messages.Add($Matches[1])
            }
            return
        }
        if ($fs.Mode -eq 'stack' -and $s -match '^\s{2,}(\S.*)$') {
            if ($Matches[1] -notmatch '^(Error Message|Stack Trace):') {
                [void]$fs.Stack.Add($Matches[1])
            }
            return
        }
        if ($s -match '\[xUnit\.net') { return }
    }

    if ($s -match '^Total tests:\s+(?<n>\d+)') {
        $n = [int]$Matches['n']
        $st.ReportedTotal = $n
        if ($n -gt $script:PhaseTotal) { $script:PhaseTotal = $n }
        return
    }
    if ($s -match '^Test Run Successful') {
        $st.RunSuccessful = $true
        return
    }
    if ($s -match '^Test Run Failed') {
        $st.RunFailed = $true
        return
    }

    if ($s -match '^\s+(?<status>Passed|Failed|Skipped)\s+(?<name>.+?)\s+\[(?<dur>[^\]]+)\]\s*$') {
        $status = $Matches['status']
        $fq = $Matches['name'].Trim()
        $durText = $Matches['dur'].Trim()
        $dur = ConvertFrom-VsTestDuration $durText
        $info = Split-TestName $fq

        $script:PhaseDone++
        if ($script:PhaseDone -gt $script:PhaseTotal) {
            $script:PhaseTotal = $script:PhaseDone
        }
        $st.ElapsedSeconds += $dur.TotalSeconds
        [void]$st.FinishedDurations.Add($dur.TotalSeconds)

        if (-not $st.ClassDone.ContainsKey($info.Class)) { $st.ClassDone[$info.Class] = 0 }
        $st.ClassDone[$info.Class] = [int]$st.ClassDone[$info.Class] + 1
        $cTotal = 0
        if ($st.ClassTotals.ContainsKey($info.Class)) { $cTotal = [int]$st.ClassTotals[$info.Class] }
        else { $cTotal = [int]$st.ClassDone[$info.Class] }

        $script:LastClassKey = $info.Key
        $script:LastClassDone = [int]$st.ClassDone[$info.Class]
        $script:LastClassTotal = $cTotal

        if ($status -eq 'Failed') {
            $st.PhaseFailed++
            $script:AnyFailed = $true
            $st.FailState = @{
                Info         = $info
                DurationText = $durText
                Messages     = New-Object System.Collections.Generic.List[string]
                Stack        = New-Object System.Collections.Generic.List[string]
                Mode         = 'idle'
            }
            return
        }

        if ($st.PrintEachPass) {
            $eta = ''
            $remaining = $script:PhaseTotal - $script:PhaseDone
            if ($st.FinishedDurations.Count -gt 0 -and $remaining -gt 0) {
                $avg = ($st.FinishedDurations | Measure-Object -Average).Average
                $eta = '    ' + (Format-Eta ([TimeSpan]::FromSeconds($avg * $remaining)))
            }
            $label = if ($status -eq 'Skipped') { 'skipped' } else { (Format-DurationShort $dur) }
            Write-Both ('{0,-11} {1,3}/{2,-3}   {3,-18} {4,2}/{5,-2}   {6}   {7}{8}' -f `
                $st.PhaseName,
                $script:PhaseDone,
                $script:PhaseTotal,
                $info.DisplayClass,
                [int]$st.ClassDone[$info.Class],
                $cTotal,
                $info.Method,
                $label,
                $eta)
        }
    }
}

function Invoke-TestPhase {
    param(
        [string]$Name,
        [string]$Project,
        [hashtable]$TraitMap,
        [bool]$PrintEachPass
    )

    $script:PhaseName = $Name
    $script:PhaseDone = 0
    $script:LastClassKey = ''
    $script:LastClassDone = 0
    $script:LastClassTotal = 0

    $listed = Get-ListedTests -Project $Project
    $classTotals = Get-ClassTotals -Tests $listed
    $classDone = @{}
    foreach ($k in @($classTotals.Keys)) { $classDone[$k] = 0 }

    $script:PhaseTotal = $listed.Count
    Write-Both ('{0,-11} {1} tests' -f $Name, $script:PhaseTotal)
    if ($Name -eq 'scenarios') {
        Write-Both ('{0,-11} {1} classes' -f $Name, $classTotals.Count)
    }

    $script:PhaseState = @{
        PhaseFailed       = 0
        ElapsedSeconds    = 0.0
        FinishedDurations = New-Object System.Collections.Generic.List[double]
        ClassDone         = $classDone
        ClassTotals       = $classTotals
        FailState         = $null
        TraitMap          = $TraitMap
        PhaseName         = $Name
        PrintEachPass     = $PrintEachPass
        RunSuccessful     = $false
        RunFailed         = $false
        ReportedTotal     = 0
    }

    $dotnetArgs = 'test "{0}" --no-build --nologo -v normal' -f $Project
    $onLine = { param($line) Handle-TestOutputLine -Line $line }
    $code = Invoke-DotNetStreaming -ArgumentString $dotnetArgs -OnLine $onLine

    $state = $script:PhaseState
    if ($null -ne $state.FailState) {
        $fs = $state.FailState
        $state.FailState = $null
        Write-FailureCard `
            -PhaseName $Name `
            -Info $fs.Info `
            -Messages $fs.Messages `
            -Stack $fs.Stack `
            -TraitMap $TraitMap `
            -ClassTotals $classTotals `
            -ClassDone $classDone
    }

    # Prefer the runner summary over Start-Process ExitCode (unreliable on PS 5.1).
    if ($state.ReportedTotal -gt $script:PhaseTotal) {
        $script:PhaseTotal = $state.ReportedTotal
    }
    $phaseOk = $false
    if ($state.RunSuccessful -and $state.PhaseFailed -eq 0) {
        $phaseOk = $true
    } elseif ($state.PhaseFailed -eq 0 -and $script:PhaseDone -gt 0 -and -not $state.RunFailed -and $code -eq 0) {
        $phaseOk = $true
    }

    if (-not $PrintEachPass -and -not $script:Interrupted) {
        $statusWord = if ($phaseOk) { 'passed' } else { 'failed' }
        $elapsed = [TimeSpan]::FromSeconds($state.ElapsedSeconds)
        Write-Both ('{0,-11} {1}/{2}   {3}    {4}' -f `
            $Name,
            $script:PhaseDone,
            $script:PhaseTotal,
            $statusWord,
            (Format-DurationShort $elapsed))
    }

    $script:PhaseState = $null
    if (-not $phaseOk) {
        $script:AnyFailed = $true
        return 1
    }
    return 0
}

# ---------------------------------------------------------------------------
# Lane: live Debug build
# ---------------------------------------------------------------------------

function Invoke-LiveLane {
    Set-Content -LiteralPath $LogPath -Value '' -Encoding UTF8

    Write-Both 'Building prosequor.slnx...'
    $buildLines = & dotnet build $Solution -nologo -v minimal 2>&1
    $buildCode = $LASTEXITCODE
    $warnCount = 0
    foreach ($line in $buildLines) {
        $s = "$line"
        Write-LogOnly $s
        if ($s -match ':\s*warning\s') { $warnCount++ }
    }
    if ($buildCode -ne 0) {
        Write-Both 'Build FAILED. See tests.log'
        foreach ($line in $buildLines) {
            $s = "$line"
            if ($s -match '(?i)error\s') { Write-ConsoleOnly $s }
        }
        return 1
    }
    if ($warnCount -gt 0) {
        Write-Both ("Build ok ({0} warning{1} in tests.log)" -f $warnCount, $(if ($warnCount -eq 1) { '' } else { 's' }))
    } else {
        Write-Both 'Build ok'
    }
    Write-Both ''

    $modinfo = Get-Content -LiteralPath $ModInfoPath -Raw | ConvertFrom-Json
    $version = [string]$modinfo.version
    $game = [string]$modinfo.dependencies.game
    $dllStamp = if (Test-Path -LiteralPath $DllPath) {
        (Get-Item -LiteralPath $DllPath).LastWriteTime.ToString('yyyy-MM-dd HH:mm')
    } else {
        'missing'
    }

    Write-Both ("prosequor {0}    game {1}    Debug" -f $version, $game)
    Write-Both ("dll  {0}    {1}" -f ($DllPath.Substring($RepoRoot.Length).TrimStart('\', '/')), $dllStamp)
    Write-Both ''

    $traitMap = Get-TraitMap -TestRoot (Join-Path $RepoRoot 'tests')

    $pureCode = 0
    $scenarioCode = 0

    if (-not $script:Interrupted) {
        $pureCode = Invoke-TestPhase -Name 'pure' -Project $PureProject -TraitMap $traitMap -PrintEachPass:$false
        Write-Both ''
    }

    if (-not $script:Interrupted) {
        $scenarioCode = Invoke-TestPhase -Name 'scenarios' -Project $ScenarioProject -TraitMap $traitMap -PrintEachPass:$true
        Write-Both ''
    }

    if ($script:Interrupted) {
        Write-InterruptedLine
        return 130
    }

    if ($script:AnyFailed -or $pureCode -ne 0 -or $scenarioCode -ne 0) {
        Write-Both 'Tests FAILED. See tests.log'
        return 1
    }

    Write-Both 'Tests PASSED. See tests.log'
    return 0
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

$exitCode = 1
try {
    $exitCode = Invoke-LiveLane
} catch {
    Write-Both ("runner error: {0}" -f $_.Exception.Message)
    Write-LogOnly ($_ | Out-String)
    $exitCode = 1
} finally {
    if ($null -ne $cancelHandler) {
        try { [Console]::remove_CancelKeyPress($cancelHandler) } catch { }
    }
}

exit $exitCode
