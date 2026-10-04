"""DS5Bridge icon v4: matched gamepad geometry.
Left = GameSir filled silhouette (orange, clean gamepad shape).
Right = DualSense outline (blue) with touchpad/sticks/dpad/buttons."""
from PIL import Image, ImageDraw
import os

S = 512
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
LEFT_BG  = (26, 20, 15, 255)
RIGHT_BG = (13, 23, 42, 255)
ORANGE = (238, 118, 32, 255)
BLUE = (86, 166, 255, 255)

d = ImageDraw.Draw(img)
d.rounded_rectangle([0, 0, S, S], radius=100, fill=RIGHT_BG)
maskL = Image.new("L", (S, S), 0)
ImageDraw.Draw(maskL).rectangle([0, 0, S//2, S], fill=255)
img.paste(Image.new("RGBA", (S, S), LEFT_BG), (0, 0), maskL)
d = ImageDraw.Draw(img)
d.line([S//2, 64, S//2, S-64], fill=(110, 116, 130, 210), width=5)


def gamepad(cx, cy, W, filled, color, line=12):
    H = W * 0.62
    gr = W * 0.230                      # grip radius
    glx = cx - W * 0.300
    grx = cx + W * 0.300
    gcy = cy + H * 0.22                 # grips sit low
    body = [cx - W*0.470, cy - H*0.44, cx + W*0.470, cy + H*0.40]
    body_r = int(H * 0.46)

    if filled:
        d.rounded_rectangle(body, radius=body_r, fill=color)
        d.ellipse([glx-gr, gcy-gr, glx+gr, gcy+gr], fill=color)
        d.ellipse([grx-gr, gcy-gr, grx+gr, gcy+gr], fill=color)
        return

    d.rounded_rectangle(body, radius=body_r, outline=color, width=line)
    d.ellipse([glx-gr, gcy-gr, glx+gr, gcy+gr], outline=color, width=line)
    d.ellipse([grx-gr, gcy-gr, grx+gr, gcy+gr], outline=color, width=line)
    # touchpad (DS5 signature)
    tw, th = W*0.195, H*0.24
    d.rounded_rectangle([cx-tw, cy-H*0.34, cx+tw, cy-H*0.34+th], radius=int(th*0.32),
                        outline=color, width=line)
    # sticks
    sr = W*0.108
    for sx in (cx - W*0.175, cx + W*0.175):
        d.ellipse([sx-sr, cy+H*0.13-sr, sx+sr, cy+H*0.13+sr], outline=color, width=line)
    # dpad on left grip
    dp, dz = W*0.068, W*0.025
    d.rounded_rectangle([glx-dp, gcy-dz, glx+dp, gcy+dz], radius=6, outline=color, width=line)
    d.rounded_rectangle([glx-dz, gcy-dp, glx+dz, gcy+dp], radius=6, outline=color, width=line)
    # face buttons on right grip
    fb = W*0.026
    for ox, oy in [(0,-W*0.068),(0,W*0.068),(-W*0.068,0),(W*0.068,0)]:
        d.ellipse([grx+ox-fb, gcy+oy-fb, grx+ox+fb, gcy+oy+fb], outline=color, width=line)


W = 176
cyc = 266
gamepad(cx=140, cy=cyc, W=W, filled=True, color=ORANGE)     # left filled GameSir
gamepad(cx=372, cy=cyc, W=W, filled=False, color=BLUE, line=12)  # right DS5 outline

os.makedirs("assets", exist_ok=True)
img.save("assets/icon.png")
img.save("assets/icon.ico", sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
print("ok")
