from PIL import Image
import os

out = r"d:\src\DreamGenClone\artifacts\tmp\matrix-masks"
os.makedirs(out, exist_ok=True)
W = H = 1024

def save(name, left, right, top=0, bottom=H):
    img = Image.new("L", (W, H), 0)
    px = img.load()
    for y in range(top, bottom):
        for x in range(left, right):
            px[x, y] = 255
    img.save(os.path.join(out, name))
    print("wrote", name)

# C1 side-by-side
save("c1_left.png", 0, W // 2)
save("c1_right.png", W // 2, W)
# C2 facing (vertical bands: left third / right third)
save("c2_left.png", 0, int(W * 0.33))
save("c2_right.png", int(W * 0.67), W)
# C3 embrace (overlapping bands: left half-ish / right half-ish, slightly inward)
save("c3_left.png", 0, int(W * 0.55))
save("c3_right.png", int(W * 0.45), W)
# C4 seated/standing (upper band / lower band)
save("c4_top.png", 0, W, 0, int(H * 0.45))
save("c4_bottom.png", 0, W, int(H * 0.45), H)
# C5 one behind (depth bands: left/right thirds)
save("c5_left.png", 0, int(W * 0.4))
save("c5_right.png", int(W * 0.6), W)
# C6 two-shot side (horizontal split)
save("c6_left.png", 0, W // 2)
save("c6_right.png", W // 2, W)
print("done")
