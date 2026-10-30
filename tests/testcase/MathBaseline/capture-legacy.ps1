[CmdletBinding()]
param(
    [string]$RepoRoot = (Join-Path $PSScriptRoot '../../..'),
    [Parameter(Mandatory = $true)]
    [string]$OutputDir,
    [string]$D3dx9Dll
)
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Historical capture requires Windows with CLR2 x86 installed.' }
if (![Environment]::UserInteractive -or (Get-Process -Id $PID).SessionId -eq 0) {
    throw 'Run in an interactive Windows desktop; SSH/session 0 cannot enumerate D3D adapters.'
}
$RepoRoot = (Resolve-Path $RepoRoot).Path
$compiler = 'C:\Windows\Microsoft.NET\Framework\v2.0.50727\csc.exe'
if (!(Test-Path $compiler)) { throw "CLR2 x86 compiler is unavailable: $compiler (no features will be installed)." }
$work = Join-Path ([IO.Path]::GetTempPath()) ('thsss-math-baseline-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $work
$null = New-Item -ItemType Directory -Force -Path $OutputDir
$capture = Join-Path (Resolve-Path $OutputDir).Path ('capture-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $capture

function Invoke-Capture([string]$Program, [string[]]$Arguments, [string]$Log) {
    $lines = @(& $Program @Arguments 2>&1 | Tee-Object -FilePath (Join-Path $capture $Log) |
        ForEach-Object { Write-Host $_; $_ })
    $code = $LASTEXITCODE
    if ($code -ne 0) { throw "$Program failed with exit code $code; see $Log" }
    if (!($lines | Where-Object { "$_" -match '^CLR=2\.0\.50727\.[0-9]+; pointer=4$' })) {
        throw "$Program did not confirm actual CLR2 x86 execution."
    }
}

Start-Transcript -Path (Join-Path $capture 'metadata.txt')
try {
    Write-Host ('CAPTURE_UTC=' + [DateTime]::UtcNow.ToString('o'))
    Write-Host ('OS=' + [Environment]::OSVersion + '; SESSION=' + (Get-Process -Id $PID).SessionId)
    Write-Host ('REPO=' + $RepoRoot + '; CAPTURE=' + $capture)
    Get-Item $compiler,(Join-Path (Split-Path $compiler) 'mscorwks.dll') |
        ForEach-Object { Write-Host ($_.FullName + ' ' + $_.VersionInfo.FileVersion) }
    foreach ($source in @('Probe.cs', 'ActualProbe.cs', 'D3dProbe.cs', 'RandomProbe.cs', 'inputs.bin')) {
        Copy-Item (Join-Path $PSScriptRoot $source) $work
    }
    foreach ($binary in @('SlimDX.dll', 'THSSS.exe')) {
        Copy-Item (Join-Path $RepoRoot ('reference/original/' + $binary)) $work
    }
    if ($D3dx9Dll) {
        $dll = Get-Item (Resolve-Path $D3dx9Dll).Path
        if ($dll.PSIsContainer -or $dll.Name -notmatch '^d3dx9_[0-9]+\.dll$') {
            throw 'D3dx9Dll must be an app-local d3dx9_NN.dll file, not an installer.'
        }
        Copy-Item $dll.FullName $work
    }
    $inputPath = Join-Path $work 'inputs.bin'
    if ((Get-Item $inputPath).Length -ne 98304 -or
        (Get-FileHash $inputPath -Algorithm SHA256).Hash -ne '6bd574af15179cca05f8f249ae0bd8218b22a82ddd01c0b3cd506c00b5a2ba6d') {
        throw 'Historical inputs.bin length/SHA256 mismatch.'
    }
    Get-ChildItem $work -File | Get-FileHash -Algorithm SHA256 | Format-List
    & $compiler /nologo /optimize+ /platform:x86 "/r:$work\SlimDX.dll" "/out:$work\Probe.exe" "$work\Probe.cs"
    if ($LASTEXITCODE -ne 0) { throw "Probe compilation failed: $LASTEXITCODE" }
    & $compiler /nologo /optimize+ /platform:x86 "/out:$work\ActualProbe.exe" "$work\ActualProbe.cs"
    if ($LASTEXITCODE -ne 0) { throw "ActualProbe compilation failed: $LASTEXITCODE" }
    & $compiler /nologo /optimize+ /platform:x86 "/out:$work\RandomProbe.exe" "$work\RandomProbe.cs"
    if ($LASTEXITCODE -ne 0) { throw "RandomProbe compilation failed: $LASTEXITCODE" }
    & $compiler /nologo /optimize+ /platform:x86 /r:System.Windows.Forms.dll "/r:$work\SlimDX.dll" "/out:$work\D3dProbe.exe" "$work\D3dProbe.cs"
    if ($LASTEXITCODE -ne 0) { throw "D3dProbe compilation failed: $LASTEXITCODE" }
    Get-ChildItem $work -Filter '*.exe' | Get-FileHash -Algorithm SHA256 | Format-List
    Invoke-Capture (Join-Path $work 'Probe.exe') @((Join-Path $capture 'windows-clr2-x86.csv'), $inputPath) 'before-probe.txt'
    Invoke-Capture (Join-Path $work 'ActualProbe.exe') @((Join-Path $work 'THSSS.exe'), (Join-Path $capture 'actual-windows-clr2-x86.csv'), $inputPath) 'before-actual.txt'
    Invoke-Capture (Join-Path $work 'RandomProbe.exe') @((Join-Path $work 'THSSS.exe'), (Join-Path $capture 'random-windows-clr2-x86.csv')) 'before-random.txt'
    # Both probes execute on the device-creating STA thread in the SAME process.
    Invoke-Capture (Join-Path $work 'D3dProbe.exe') @((Join-Path $work 'Probe.exe'), (Join-Path $capture 'windows-clr2-d3d-x86.csv'), $inputPath, (Join-Path $capture 'actual-windows-clr2-d3d-x86.csv'), (Join-Path $capture 'random-windows-clr2-d3d-x86.csv')) 'after-d3d.txt'
    Copy-Item $inputPath $capture
    Get-ChildItem $capture -File | Where-Object { $_.Name -ne 'metadata.txt' } |
        Get-FileHash -Algorithm SHA256 | Format-List
    Write-Host 'LEGACY_BASELINE_COMPLETE'
} catch {
    Write-Host ('BASELINE_ERROR=' + $_.Exception.GetType().FullName + ': ' + $_.Exception.Message)
    throw
} finally {
    Stop-Transcript
    Remove-Item -Recurse -Force $work
}
