# Rebuilds firmware/dist/wqv_bridge-rp2040.uf2 from the sketch with arduino-cli.
# Uses arduino-cli from PATH, or the copy bundled with Arduino IDE 2.
$ErrorActionPreference = 'Stop'
$CoreVersion = '6.1.1'   # earlephilhower/arduino-pico version these binaries were built with
$cli = (Get-Command arduino-cli -ErrorAction SilentlyContinue).Source
if (-not $cli) { $cli = "$env:LOCALAPPDATA\Programs\Arduino IDE\resources\app\lib\backend\resources\arduino-cli.exe" }
if (-not (Test-Path $cli)) { throw 'arduino-cli not found. Install it or Arduino IDE 2.' }

$here = $PSScriptRoot
& $cli config add board_manager.additional_urls https://github.com/earlephilhower/arduino-pico/releases/download/global/package_rp2040_index.json 2>$null
& $cli core update-index
& $cli core install "rp2040:rp2040@$CoreVersion"

New-Item -ItemType Directory -Force "$here\dist" | Out-Null
foreach ($sketch in 'wqv_bridge') {
    $out = "$here\build\$sketch-rp2040"
    & $cli compile --fqbn rp2040:rp2040:rpipico --output-dir $out "$here\$sketch"
    Copy-Item "$out\$sketch.ino.uf2" "$here\dist\$sketch-rp2040.uf2" -Force
}
Get-ChildItem "$here\dist\*.uf2" | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_ -Algorithm SHA256).Hash.ToLower(), $_.Name }
