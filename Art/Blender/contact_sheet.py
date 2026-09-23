import bpy, sys, os, numpy as np
d = sys.argv[sys.argv.index("--")+1]; out = sys.argv[sys.argv.index("--")+2]
files = sorted(f for f in os.listdir(d) if f.endswith(".png"))
COLS = 5; W, H = 512, 256
rows = (len(files)+COLS-1)//COLS
sheet = np.zeros((rows*H, COLS*W, 4)); sheet[...,:3] = 0.12; sheet[...,3] = 1.0
opaque = []
for i, f in enumerate(files):
    img = bpy.data.images.load(os.path.join(d, f), check_existing=False)
    img.colorspace_settings.name = "Non-Color"
    w,h = img.size
    buf = np.empty(w*h*4, dtype=np.float32); img.pixels.foreach_get(buf)
    px = buf.reshape(h,w,4)
    a = px[...,3:4]
    if a.min() > 0.99: opaque.append(f)
    r, c = i//COLS, i%COLS
    y0 = (rows-1-r)*H
    dst = sheet[y0:y0+H, c*W:(c+1)*W, :3]
    sheet[y0:y0+H, c*W:(c+1)*W, :3] = px[...,:3]*a + dst*(1-a)
print("fully opaque (transparency missing):", opaque or "none - all have alpha")
o = bpy.data.images.new("sheet", COLS*W, rows*H, alpha=True)
o.colorspace_settings.name = "Non-Color"
o.pixels.foreach_set(np.ascontiguousarray(sheet, dtype=np.float32).ravel())
o.filepath_raw = out; o.file_format = "PNG"; o.save()
print("sheet ->", out)
