"""DS5Bridge icon — split logo.
Left half : white background + BLACK silhouette of the GameSir 九尾狐 pad's left half
            (with a fox ear and a dpad cut-out so it reads as a controller).
Right half: black background + WHITE outline of the DualSense's right half
            (right grip, right stick, face buttons, touchpad edge)."""
from PIL import Image, ImageDraw, ImageChops
import os

S = 512; HALF = S // 2; RADIUS = 96
full = Image.new("L", (S, S), 0)
ImageDraw.Draw(full).rounded_rectangle([1, 1, S-2, S-2], radius=RADIUS, fill=255)
left_mask = full.copy();  ImageDraw.Draw(left_mask).rectangle([HALF, 0, S, S], fill=0)
right_mask = full.copy(); ImageDraw.Draw(right_mask).rectangle([0, 0, HALF, S], fill=0)

img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
img.paste(Image.new("RGBA", (S, S), (255, 255, 255, 255)), (0, 0), left_mask)
img.paste(Image.new("RGBA", (S, S), (9, 9, 11, 255)),    (0, 0), right_mask)

BLACK = (13, 13, 15, 255)
WHITE = (255, 255, 255, 255)


def body_parts(cx, cy, W):
    """Return geometry for a flatter gamepad with pronounced lower grips."""
    H = W * 0.56
    gr = W * 0.235
    glx, grx = cx - W * 0.305, cx + W * 0.305
    gcy = cy + H * 0.30                      # grips sit low
    bar = [cx - W*0.470, cy - H*0.34, cx + W*0.470, cy + H*0.10]
    return H, gr, glx, grx, gcy, bar


def outline_pad(W, CY, line=16):
    ds = Image.new("RGBA", (S, S), (0, 0, 0, 0)); d = ImageDraw.Draw(ds)
    H, gr, glx, grx, gcy, bar = body_parts(HALF, CY, W)
    d.rounded_rectangle(bar, radius=int(H*0.42), outline=WHITE, width=line)
    d.ellipse([glx-gr, gcy-gr, glx+gr, gcy+gr], outline=WHITE, width=line)
    d.ellipse([grx-gr, gcy-gr, grx+gr, gcy+gr], outline=WHITE, width=line)
    # touchpad (its right edge lands in the right half)
    tw, th = W*0.20, H*0.26
    d.rounded_rectangle([HALF-tw, CY-H*0.30, HALF+tw, CY-H*0.30+th], radius=int(th*0.32),
                        outline=WHITE, width=line)
    # sticks
    sr = W*0.105
    for sx in (HALF - W*0.175, HALF + W*0.175):
        d.ellipse([sx-sr, CY+H*0.16-sr, sx+sr, CY+H*0.16+sr], outline=WHITE, width=line)
    # face buttons (right grip)
    fb = W*0.026
    for ox, oy in [(0,-W*0.070),(0,W*0.070),(-W*0.070,0),(W*0.070,0)]:
        d.ellipse([grx+ox-fb, gcy+oy-fb, grx+ox+fb, gcy+oy+fb], outline=WHITE, width=line)
    return ds


def fox_pad(W, CY):
    fox = Image.new("RGBA", (S, S), (0, 0, 0, 0)); d = ImageDraw.Draw(fox)
    H, gr, glx, grx, gcy, bar = body_parts(HALF, CY, W)
    # thinner body bar so the grip reads as a distinct grip
    bar = [bar[0], CY - H*0.44, bar[2], CY - H*0.06]
    d.rounded_rectangle(bar, radius=int(H*0.34), fill=BLACK)
    d.ellipse([glx-gr, gcy-gr, glx+gr, gcy+gr], fill=BLACK)
    d.ellipse([grx-gr, gcy-gr, grx+gr, gcy+gr], fill=BLACK)
    # negative-space cut-outs: a stick (upper) + a dpad (lower) on the grip
    hole = Image.new("L", (S, S), 0); hd = ImageDraw.Draw(hole)
    srx = glx + W*0.02; sry = gcy - H*0.16; sr = W*0.072
    hd.ellipse([srx-sr, sry-sr, srx+sr, sry+sr], fill=255)
    dp, dz = W*0.070, W*0.027
    dpx, dpy = glx - W*0.02, gcy + H*0.14
    hd.rounded_rectangle([dpx-dp, dpy-dz, dpx+dp, dpy+dz], radius=7, fill=255)
    hd.rounded_rectangle([dpx-dz, dpy-dp, dpx+dz, dpy+dp], radius=7, fill=255)
    a = ImageChops.subtract(fox.split()[3], hole)
    return fox, a


W = 316; CY = 250
fox, fa = fox_pad(W, CY)
fa = ImageChops.multiply(fa, left_mask)
img.paste(fox, (0, 0), fa)

ds = outline_pad(W, CY)
dm = ImageChops.multiply(ds.split()[3], right_mask)
img.paste(ds, (0, 0), dm)

os.makedirs("assets", exist_ok=True)
img.save("assets/icon.png")
img.save("assets/icon.ico", sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
print("ok")
