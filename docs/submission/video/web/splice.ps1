param(
    [Parameter(Mandatory)] [string]$Ffmpeg,   # a full ffmpeg, with libx264
    [Parameter(Mandatory)] [string]$Frames,   # the folder film-web.cjs filled
    [Parameter(Mandatory)] [string]$Desktop,  # the film of the desktop app, as film/ makes it (29.6 seconds)
    [Parameter(Mandatory)] [string]$Out       # where the two films go
)

# Makes the two films that use the web viewer's frames:
#
#   vigil-views.mp4   the globe's canvas alone, 9.6 seconds, ending where it starts. The slide loops it.
#   vigil-demo.mp4    the film of the desktop app with 3.3 seconds of the web viewer put in, and the
#                     end card drawn again. It must stay under 30 seconds.
#
# The frame numbers below are for the desktop film as committed before the web shot went in.
# Film it again and they move: find the new ones from where the caption band changes,
#   ffmpeg -i <film> -vf "crop=1920:104:0:976,select='gt(scene,0.004)',showinfo" -an -f null -
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
New-Item -ItemType Directory -Force $Out | Out-Null
$shot = Join-Path $Out 'shot'

# The slide's clip. The canvas is at (32, 143) on the page and 1456 by 805: one row is left off so the height is even.
& $Ffmpeg -hide_banner -loglevel error -y -framerate 30 -start_number 0 -i "$Frames\w%05d.png" -frames:v 288 `
    -vf "crop=1456:804:32:143,format=yuv420p" -c:v libx264 -preset slow -crf 18 -movflags +faststart "$Out\vigil-views.mp4"
& $Ffmpeg -hide_banner -loglevel error -y -start_number 150 -i "$Frames\w%05d.png" -frames:v 1 -vf "crop=1456:804:32:143" "$Out\slide-views.png"

# The web shot: the relief standing on the leaned-over globe, the unroll, and the flat map held.
powershell -NoProfile -ExecutionPolicy Bypass -File "$here\dress.ps1" -Frames $Frames -Out $shot -From 66 -To 164 `
    -Caption "Relief: each cell rises by the size of its trend. In the browser, the globe unrolls into a flat map."

# What is kept of the desktop film, in frames at 30 a second:
#   0 to 42      the title, 1.4 of its 2 seconds
#   60 to 197    the overview and the three layers, less the last 0.4 seconds
#   209 to 445   the top finding, its evidence and the re-test
#   (the web shot goes here, in place of the desktop's 1.3 seconds of relief)
#   485 to 744   the years, the zone being drawn, and its trends, less the last 0.4 seconds
#   756 to 816   the Detective
#   (the end card, 2 seconds)
$cut = { param($from, $to, $name) "[0:v]trim=start_frame=${from}:end_frame=${to},setpts=PTS-STARTPTS,format=yuvj420p[$name]" }
$graph = @(
    (& $cut 0 42 'a'), (& $cut 60 197 'b'), (& $cut 209 445 'c'),
    '[1:v]format=yuvj420p,setsar=1[w]',
    (& $cut 485 744 'd'), (& $cut 756 816 'e'),
    '[2:v]format=yuvj420p,setsar=1[f]',
    '[a][b][c][w][d][e][f]concat=n=7:v=1:a=0,fps=30[v]'
) -join ';'
& $Ffmpeg -hide_banner -loglevel error -y -i $Desktop -framerate 30 -i "$shot\s%05d.jpg" -loop 1 -framerate 30 -t 2 -i "$shot\end.jpg" `
    -filter_complex $graph -map "[v]" -c:v libx264 -preset slow -crf 17 -pix_fmt yuvj420p -movflags +faststart "$Out\vigil-demo.mp4"

(& $Ffmpeg -hide_banner -i "$Out\vigil-demo.mp4" 2>&1 | Out-String) -split "`n" | Select-String 'Duration' | ForEach-Object { $_.Line.Trim() }
