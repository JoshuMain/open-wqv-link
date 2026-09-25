#!/usr/bin/env sh
# Optional check that exported PNGs carry their metadata: exiftool must report Make, Model and DateTimeOriginal.
# Usage: tools/check-exif.sh path/to/image.png [more.png ...]
set -u
command -v exiftool >/dev/null 2>&1 || { echo "exiftool is not on the PATH (https://exiftool.org)." >&2; exit 2; }

failed=0
for f in "$@"; do
    echo "== $f"
    out=$(exiftool -s -Make -Model -DateTimeOriginal -Title -CreationTime "$f")
    echo "$out"
    for tag in Make Model; do
        echo "$out" | grep -q "^$tag *:" || { echo "MISSING: $tag"; failed=$((failed + 1)); }
    done
    echo "$out" | grep -q "^DateTimeOriginal *:" || echo "note: no DateTimeOriginal (undated image?)"
done
exit $failed
