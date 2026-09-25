# Optional check that exported PNGs carry their metadata: exiftool must report Make, Model and DateTimeOriginal.
# Usage: ./tools/check-exif.ps1 path/to/image.png [more.png ...]
param([Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)][string[]]$Files)

if (-not (Get-Command exiftool -ErrorAction SilentlyContinue)) {
    Write-Error "exiftool is not on the PATH (https://exiftool.org)."
    exit 2
}

$failed = 0
foreach ($f in $Files) {
    $out = exiftool -s -Make -Model -DateTimeOriginal -Title -CreationTime $f
    Write-Output "== $f"
    Write-Output $out
    foreach ($tag in 'Make', 'Model') {
        if (-not ($out -match "^$tag\s*:")) { Write-Output "MISSING: $tag"; $failed++ }
    }
    if (-not ($out -match '^DateTimeOriginal\s*:')) { Write-Output "note: no DateTimeOriginal (undated image?)" }
}
exit $failed
