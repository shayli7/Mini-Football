import bpy, sys, numpy as np
OUT = sys.argv[sys.argv.index("--")+1]
# the play-surface rect in uv
U0,U1,V0,V1 = 0.481, 0.952, 0.003, 0.860
names = ["DeepBlue","IndoorCourt","WildWest","PitchPerfect","NeonRave","Chessboard"]
print("\n===== FIELD ALBEDO, pitch rect (bytes as stored) =====")
for n in names:
    p = f"{OUT}/FieldSkin_{n}_Albedo.png"
    img = bpy.data.images.load(p, check_existing=False)
    img.colorspace_settings.name = "Non-Color"
    w,h = img.size
    buf = np.empty(w*h*4, dtype=np.float32); img.pixels.foreach_get(buf)
    px = buf.reshape(h,w,4)[...,:3]
    sub = px[int(V0*h):int(V1*h), int(U0*w):int(U1*w)]
    m = sub.reshape(-1,3).mean(axis=0)*255
    lo = sub.min()*255; hi = sub.max()*255
    print(f"  {n:14} mean=({m[0]:5.1f},{m[1]:5.1f},{m[2]:5.1f})  range[{lo:5.1f},{hi:5.1f}]")
