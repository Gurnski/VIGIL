param([string]$Frames, [string]$Out, [int]$From, [int]$To, [string]$Caption)

# Puts the web viewer's frames into the film's frame: the page above, and under it the band
# with the caption, drawn as film/Film.cs draws it for the desktop app. Then draws the end card.
Add-Type -AssemblyName System.Drawing
$background = [System.Drawing.ColorTranslator]::FromHtml('#131415')
$border = [System.Drawing.ColorTranslator]::FromHtml('#383B41')
$primary = [System.Drawing.ColorTranslator]::FromHtml('#EDECE8')
$secondary = [System.Drawing.ColorTranslator]::FromHtml('#A6A5A0')
$muted = [System.Drawing.ColorTranslator]::FromHtml('#7C7B77')
$width = 1920; $height = 1080; $band = 108
$centre = New-Object System.Drawing.StringFormat
$centre.Alignment = [System.Drawing.StringAlignment]::Center
$centre.LineAlignment = [System.Drawing.StringAlignment]::Center
$jpeg = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.FormatID -eq [System.Drawing.Imaging.ImageFormat]::Jpeg.Guid }
$quality = New-Object System.Drawing.Imaging.EncoderParameters 1
$quality.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter ([System.Drawing.Imaging.Encoder]::Quality), 95L

New-Item -ItemType Directory -Force $Out | Out-Null
Get-ChildItem $Out -Filter 's*.jpg' | Remove-Item
$n = 0
for ($i = $From; $i -le $To; $i++) {
    $page = [System.Drawing.Image]::FromFile(("{0}\w{1:00000}.png" -f $Frames, $i))
    $frame = New-Object System.Drawing.Bitmap $width, $height
    $g = [System.Drawing.Graphics]::FromImage($frame)
    $g.DrawImage($page, 0, 0, $width, $height - $band)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
    $g.FillRectangle((New-Object System.Drawing.SolidBrush $background), 0, $height - $band, $width, $band)
    $g.DrawLine((New-Object System.Drawing.Pen $border, 1), 0, $height - $band + 0.5, $width, $height - $band + 0.5)
    $font = New-Object System.Drawing.Font 'Segoe UI Variable Display', ($height / 38), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $box = New-Object System.Drawing.RectangleF ($width * 0.06), ($height - $band), ($width * 0.88), $band
    $g.DrawString($Caption, $font, (New-Object System.Drawing.SolidBrush $primary), $box, $centre)
    $g.Dispose(); $page.Dispose()
    $frame.Save(("{0}\s{1:00000}.jpg" -f $Out, $n), $jpeg, $quality)
    $frame.Dispose(); $n++
}
"web shot: $n frames, $([math]::Round($n / 30, 2)) seconds"

# The end card, as Film.cs draws it, with the last line saying what the frames are now.
function Line($g, [string]$text, [double]$size, $colour, [double]$centreY, $style = [System.Drawing.FontStyle]::Regular) {
    $font = New-Object System.Drawing.Font 'Segoe UI Variable Display', $size, $style, ([System.Drawing.GraphicsUnit]::Pixel)
    $box = New-Object System.Drawing.RectangleF ($width * 0.08), ($centreY - $size * 1.5), ($width * 0.84), ($size * 3)
    $g.DrawString($text, $font, (New-Object System.Drawing.SolidBrush $colour), $box, $centre)
}
$card = New-Object System.Drawing.Bitmap $width, $height
$g = [System.Drawing.Graphics]::FromImage($card)
$g.Clear($background)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
Line $g 'VIGIL' ($height / 9) $primary ($height * 0.34) ([System.Drawing.FontStyle]::Bold)
Line $g 'Discovery first. Evidence for every finding.' ($height / 24) $secondary ($height * 0.47)
Line $g 'Data: NASA MERRA-2, GPM IMERG, GLDAS, MODIS Terra and Aqua, GISTEMP, GPCP. Earthquakes: USGS.' ($height / 44) $muted ($height * 0.62)
Line $g 'Every frame of this film is VIGIL itself, the desktop app or its web viewer, showing the real scan.' ($height / 44) $muted ($height * 0.68)
$g.Dispose()
$card.Save("$Out\end.jpg", $jpeg, $quality)
$card.Dispose()
"end card drawn"
