# Pretends to be the phone: traces a circle with the "button held", then does a quick press (a click).
# Usage: .\fake-pen.ps1 [-NoClick]
param([switch]$NoClick)
$udp = New-Object System.Net.Sockets.UdpClient
$inv = [Globalization.CultureInfo]::InvariantCulture   # a locale with a decimal comma would send "0,3"
$send = { param($s) $b = [Text.Encoding]::UTF8.GetBytes("$s`n"); [void]$udp.Send($b, $b.Length, "127.0.0.1", 51516) }
& $send "B down"
0..120 | ForEach-Object { $a = $_ / 20.0; & $send ([string]::Format($inv, "M {0:F3} {1:F3}", [math]::Cos($a) * 0.3, [math]::Sin($a) * 0.3)); Start-Sleep -Milliseconds 16 }
Start-Sleep -Milliseconds 300; & $send "B up"
if (-not $NoClick) { & $send "B down"; Start-Sleep -Milliseconds 80; & $send "B up" }
$udp.Close()
