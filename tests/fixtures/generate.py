"""Regenerate synthetic media: Pillow 11.3.0, pillow-heif 1.1.0, ffmpeg.
No real accounts, photographs or user files are used.
"""
from pathlib import Path
import math, subprocess
from PIL import Image, ImageDraw
import pillow_heif

pillow_heif.register_heif_opener()
root = Path(__file__).parent
image = Image.new("RGB", (640, 360))
pixels = image.load()
for y in range(360):
    for x in range(640):
        pixels[x, y] = (int(45 + 95*y/360), int(90+65*y/360), int(140+35*y/360))
draw = ImageDraw.Draw(image)
draw.ellipse((460, 40, 530, 110), fill=(250, 210, 110))
draw.polygon([(0,290),(180,100),(350,360)], fill=(38,85,83))
draw.polygon([(170,360),(420,120),(640,300),(640,360)], fill=(26,65,64))
draw.polygon([(345,202),(420,120),(480,192),(440,175),(413,195),(393,172)], fill=(200,214,204))
draw.polygon([(0,315),(250,290),(640,330),(640,360),(0,360)], fill=(17,39,45))
exif = Image.Exif()
exif[0x8769] = {0x9003: "2020:01:02 03:04:05"}
for extension in ["jpg", "png", "webp", "heic", "heif"]:
    image.save(root / ("landscape." + extension), quality=90, exif=exif)
image.save(root/"undated.jpg", quality=85)
(root/"notes.txt").write_text("Unsupported source fixture, intentionally ignored.\n")
for extension, codec in [("mp4","libx264"),("mov","libx264"),("webm","libvpx")]:
    command = ["ffmpeg","-hide_banner","-loglevel","error","-y","-f","lavfi","-i","testsrc2=size=640x360:rate=12:duration=3",
        "-vf","hue=h=45*t","-c:v",codec,"-threads","2","-pix_fmt","yuv420p","-metadata","creation_time=2020-01-02T03:04:05Z"]
    if extension == "webm": command += ["-metadata","DATE_RECORDED=2020-01-02T03:04:05Z"]
    command += [str(root/("motion."+extension))]
    subprocess.run(command, check=True)
