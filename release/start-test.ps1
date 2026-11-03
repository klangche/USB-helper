# Start tests for release.yml (#130, #147).
# Wait-Window: waits until app\UsbHelper.exe in the given folder shows its window, prints the time, then closes it.
function Wait-Window([string] $folder, [string] $how) {
    $app = Join-Path $folder 'app\UsbHelper.exe'
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt 60) {
        $running = @(Get-Process UsbHelper -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $app -and $_.MainWindowHandle -ne 0 })
        if ($running) {
            "Window of $app after $($watch.ElapsedMilliseconds) ms ($how)"
            $running | Stop-Process -Force
            Wait-Process -Id $running.Id -Timeout 10 -ErrorAction SilentlyContinue
            return
        }
        Start-Sleep -Milliseconds 50
    }
    Get-Process UsbHelper -ErrorAction SilentlyContinue | Stop-Process -Force
    throw "No window of $app within 60 s ($how)."
}

# Test-Start: starts USB-helper.exe and waits until the app it starts, app\UsbHelper.exe next to it, shows its window.
function Test-Start([string] $exe) {
    Start-Process $exe
    Wait-Window (Split-Path $exe) "started through $exe"
}

# Assert-NotRunning: the app in the given folder does not start by itself within a few seconds.
function Assert-NotRunning([string] $folder, [string] $how) {
    $app = Join-Path $folder 'app\UsbHelper.exe'
    Start-Sleep -Seconds 5
    if (Get-Process UsbHelper -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $app }) {
        Get-Process UsbHelper -ErrorAction SilentlyContinue | Stop-Process -Force
        throw "$app started by itself ($how)."
    }
    "$app did not start ($how)"
}
