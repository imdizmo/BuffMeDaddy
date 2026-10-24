#!/usr/bin/env python3
"""Thunderstore icon: a mead horn with a sparkle on a dark tile with a brass edge. Own art."""
import sys
from PIL import Image, ImageDraw, ImageFilter
S = 4; W = 256 * S
img = Image.new("RGBA", (W, W), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
d.rounded_rectangle([8*S, 8*S, 248*S, 248*S], radius=36*S, fill=(26, 20, 18, 255), outline=(201, 160, 86, 255), width=6*S)
glow = Image.new("RGBA", (W, W), (0, 0, 0, 0)); g = ImageDraw.Draw(glow)
g.ellipse([60*S, 50*S, 200*S, 190*S], fill=(240, 160, 60, 110)); glow = glow.filter(ImageFilter.GaussianBlur(26*S))
img = Image.alpha_composite(img, glow); d = ImageDraw.Draw(img)
# horn: curved tapering shape
pts = [(70, 190), (90, 140), (120, 104), (160, 82), (196, 74), (200, 96), (168, 108), (136, 132), (112, 168), (100, 204)]
d.polygon([(x*S, y*S) for x, y in pts], fill=(214, 186, 130, 255), outline=(90, 62, 30, 255))
for t in (0.25, 0.55):
    i = int(t * 4); a, b = pts[i], pts[-1 - i]
    d.line([(a[0]*S, a[1]*S), (b[0]*S, b[1]*S)], fill=(160, 112, 40, 255), width=7*S)
d.ellipse([186*S, 70*S, 206*S, 100*S], fill=(232, 150, 40, 255), outline=(90, 62, 30, 255), width=2*S)
# sparkle
cx, cy = 88, 78
for r, wdt in ((30, 5), (18, 9)):
    d.polygon([(cx*S, (cy-r)*S), ((cx+wdt/3)*S, (cy-wdt/3)*S), ((cx+r)*S, cy*S), ((cx+wdt/3)*S, (cy+wdt/3)*S), (cx*S, (cy+r)*S), ((cx-wdt/3)*S, (cy+wdt/3)*S), ((cx-r)*S, cy*S), ((cx-wdt/3)*S, (cy-wdt/3)*S)], fill=(255, 236, 170, 255))
img.resize((256, 256), Image.LANCZOS).save(sys.argv[1])
