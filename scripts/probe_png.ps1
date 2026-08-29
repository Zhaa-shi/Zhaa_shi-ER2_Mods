# Decode palette28 PNG and sample pixels
function Get-Crc32([byte[]]$data) {
    $crc = 4294967295
    foreach ($b in $data) {
        $crc = $crc -bxor [long]$b
        for ($k = 0; $k -lt 8; $k++) {
            if (($crc -band 1) -ne 0) { $crc = ($crc -shr 1) -bxor 3988292384 } else { $crc = $crc -shr 1 }
        }
    }
    return ($crc -bxor 4294967295) -band 4294967295
}
function Rd32([byte[]]$d, [int]$i) { return ([long](([int]$d[$i] -shl 24) -bor ([int]$d[$i+1] -shl 16) -bor ([int]$d[$i+2] -shl 8) -bor ([int]$d[$i+3]))) -band 4294967295 }
function Sample-Png([string]$path, [int[]]$xs, [int[]]$ys) {
    $d = [System.IO.File]::ReadAllBytes($path)
    $pos = 8; $w = 0; $h = 0; $bitDepth = 0; $colorType = 0
    $pal = New-Object byte[] 0; $trns = New-Object byte[] 0; $idat = New-Object byte[] 0; $idatLen = 0
    while ($pos + 12 -le $d.Length) {
        $len = Rd32 $d $pos
        $type = [System.Text.Encoding]::ASCII.GetString($d, $pos+4, 4)
        if ($type -eq "IHDR") {
            $w = Rd32 $d ($pos + 8); $h = Rd32 $d ($pos + 12)
            $bitDepth = $d[$pos+16]; $colorType = $d[$pos+17]
        }
        elseif ($type -eq "PLTE") { $pal = New-Object byte[] $len; [Array]::Copy($d, $pos+8, $pal, 0, $len) }
        elseif ($type -eq "tRNS") { $trns = New-Object byte[] $len; [Array]::Copy($d, $pos+8, $trns, 0, $len) }
        elseif ($type -eq "IDAT") { $idat = New-Object byte[] $len; [Array]::Copy($d, $pos+8, $idat, 0, $len) }
        $pos += 12 + $len
    }
    # inflate zlib
    $z = New-Object System.IO.MemoryStream
    $z.Write($idat, 2, $idat.Length - 6)  # skip zlib header + adler
    $z.Position = 0
    $ds = New-Object System.IO.Compression.DeflateStream($z, [System.IO.Compression.CompressionMode]::Decompress)
    $out = New-Object System.IO.MemoryStream
    $ds.CopyTo($out)
    $raw = $out.ToArray()
    $bpp = 1
    $stride = [int]($w * $bpp)
    $idx = New-Object byte[] ($w * $h)
    for ($y = 0; $y -lt $h; $y++) {
        $rowOff = $y * ($stride + 1) + 1
        for ($x = 0; $x -lt $w; $x++) { $idx[$y * $w + $x] = $raw[$rowOff + $x] }
    }
    Write-Host ("{0}: {1}x{2} bitDepth={3} colorType={4} palette={5}" -f (Split-Path $path -Leaf), $w, $h, $bitDepth, $colorType, ($pal.Length / 3))
    for ($i = 0; $i -lt $xs.Length; $i++) {
        $px = $xs[$i]; $py = $ys[$i]
        $palIdx = $idx[$py * $w + $px]
        $r = $pal[$palIdx * 3]; $g = $pal[$palIdx * 3 + 1]; $b = $pal[$palIdx * 3 + 2]
        $a = 255; if ($palIdx -lt $trns.Length) { $a = $trns[$palIdx] }
        Write-Host ("  ({0},{1}) idx={2} rgba=({3},{4},{5},{6})" -f $px, $py, $palIdx, $r, $g, $b, $a)
    }
}
$tex = "D:\Users\71011\Documents\ER2_Mods\BattlefieldUI\Assets\Textures"
Sample-Png "$tex\hitmarker.png" @(32, 32, 20, 20) @(32, 32, 32, 40)
Sample-Png "$tex\panel_bg.png" @(256, 256, 256, 5, 10, 60) @(64, 6, 64, 64, 3, 90)
Sample-Png "$tex\panel_sm.png" @(128, 128, 128) @(32, 5, 32)
Sample-Png "$tex\bar_bg.png" @(128, 128, 10) @(10, 10, 5)
Sample-Png "$tex\bar_fill.png" @(128, 128, 128) @(10, 5, 15)
Sample-Png "$tex\killcard_bg.png" @(3, 200, 200, 20) @(42, 10, 42, 42)
Sample-Png "$tex\vignette.png" @(127, 127, 127, 250, 250, 30, 30) @(127, 127, 240, 250, 250, 30, 240)