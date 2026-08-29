# Validate generated PNG chunks (CRC) and WAV headers (PS5.1-safe: int casts before shifts)
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
function Rd32LE([byte[]]$d, [int]$i) { return ([long](([int]$d[$i]) -bor ([int]$d[$i+1] -shl 8) -bor ([int]$d[$i+2] -shl 16) -bor ([int]$d[$i+3] -shl 24))) -band 4294967295 }
$tex = "D:\Users\71011\Documents\ER2_Mods\BattlefieldUI\Assets\Textures"
Get-ChildItem $tex -Filter *.png | ForEach-Object {
    $d = [System.IO.File]::ReadAllBytes($_.FullName)
    $sigOk = ($d[0] -eq 0x89 -and $d[1] -eq 0x50 -and $d[2] -eq 0x4E -and $d[3] -eq 0x47)
    $pos = 8; $ok = $sigOk; $chunks = @()
    while ($pos + 12 -le $d.Length) {
        $len = Rd32 $d $pos
        $type = [System.Text.Encoding]::ASCII.GetString($d, $pos+4, 4)
        if ($pos + 12 + $len -gt $d.Length) { $chunks += ("TRUNC:" + $type); $ok = $false; break }
        $crcData = New-Object byte[] ($len + 4)
        [Array]::Copy($d, $pos+4, $crcData, 0, $len + 4)
        $crc = Get-Crc32 $crcData
        $stored = Rd32 $d ($pos + 8 + $len)
        if ($crc -ne $stored) { $ok = $false; $chunks += ("BADCRC:" + $type) } else { $chunks += $type }
        $pos += 12 + $len
    }
    if ($pos -ne $d.Length) { $ok = $false }
    $w = Rd32 $d 16; $h = Rd32 $d 20
    Write-Host ("{0} {1}x{2} sig={3} chunksOK={4} chunks=[{5}]" -f $_.Name, $w, $h, $sigOk, $ok, ($chunks -join ','))
}
$snd = "D:\Users\71011\Documents\ER2_Mods\BattlefieldUI\Assets\Sounds"
Get-ChildItem $snd -Filter *.wav | ForEach-Object {
    $d = [System.IO.File]::ReadAllBytes($_.FullName)
    $riff = [System.Text.Encoding]::ASCII.GetString($d, 0, 4)
    $wave = [System.Text.Encoding]::ASCII.GetString($d, 8, 4)
    $ch = ([int]$d[22] -shl 8) -bor [int]$d[23]
    $rate = Rd32LE $d 24
    $bits = ([int]$d[34] -shl 8) -bor [int]$d[35]
    $dataLen = Rd32LE $d 40
    $dur = [Math]::Round($dataLen / 2 / $rate, 2)
    Write-Host ("{0} riff={1} wave={2} ch={3} rate={4} bits={5} data={6}B dur={7}s" -f $_.Name, $riff, $wave, $ch, $rate, $bits, $dataLen, $dur)
}