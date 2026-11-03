# Test-Start (#130): starts USB-helper.exe and waits until the app it starts, app\UsbHelper.exe next to it, shows its
# window; prints the time, then closes the app. Used by release.yml for the zip and the installed .msi.
function Test-Start([string] $exe) {
    $app = Join-Path (Split-Path $exe) 'app\UsbHelper.exe'
    $watch = [Diagnostics.Stopwatch]::StartNew()
    Start-Process $exe
    while ($watch.Elapsed.TotalSeconds -lt 60) {
        $running = @(Get-Process UsbHelper -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $app -and $_.MainWindowHandle -ne 0 })
        if ($running) {
            "Window of $app after $($watch.ElapsedMilliseconds) ms (started through $exe)"
            $running | Stop-Process -Force
            Wait-Process -Id $running.Id -Timeout 10 -ErrorAction SilentlyContinue
            return
        }
        Start-Sleep -Milliseconds 50
    }
    Get-Process UsbHelper -ErrorAction SilentlyContinue | Stop-Process -Force
    throw "No window of $app within 60 s after starting $exe."
}
