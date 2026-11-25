#!/usr/bin/env python3
"""Puts a transparent render on a dark studio backdrop: a soft radial gradient, lighter behind
the block, as Workshop images usually are.

  python3 tools/model/backdrop.py IN.png OUT.png
"""
import sys

from PIL import Image, ImageDraw, ImageFilter

CENTRE = (58, 66, 76)
EDGE = (14, 17, 21)


def backdrop(size):
    w, h = size
    bg = Image.new('RGB', size, EDGE)
    glow = Image.new('L', size, 0)
    d = ImageDraw.Draw(glow)
    rx, ry = w * 0.62, h * 0.85
    cx, cy = w / 2, h * 0.47
    d.ellipse((cx - rx, cy - ry, cx + rx, cy + ry), fill=255)
    glow = glow.filter(ImageFilter.GaussianBlur(min(w, h) * 0.28))
    return Image.composite(Image.new('RGB', size, CENTRE), bg, glow)


def main(src, out):
    fg = Image.open(src).convert('RGBA')
    bg = backdrop(fg.size).convert('RGBA')
    bg.alpha_composite(fg)
    bg.convert('RGB').save(out)
    print('wrote', out)


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
