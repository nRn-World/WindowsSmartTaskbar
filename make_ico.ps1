Add-Type -AssemblyName System.Drawing
$pngPath = "logo.png"
$icoPath = "logo.ico"
$src = [System.Drawing.Image]::FromFile($pngPath)
$size = 256
$bmp = new-object System.Drawing.Bitmap($size, $size)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.DrawImage($src, 0, 0, $size, $size)
$ms = new-object System.IO.MemoryStream
$bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$pngBytes = $ms.ToArray()

$fs = new-object System.IO.FileStream($icoPath, [System.IO.FileMode]::Create)
$bw = new-object System.IO.BinaryWriter($fs)
$bw.Write([int16]0)
$bw.Write([int16]1)
$bw.Write([int16]1)
$bw.Write([byte]0)
$bw.Write([byte]0)
$bw.Write([byte]0)
$bw.Write([byte]0)
$bw.Write([int16]1)
$bw.Write([int16]32)
$bw.Write([int32]$pngBytes.Length)
$bw.Write([int32]22)
$bw.Write($pngBytes)
$bw.Close()
$fs.Close()
