import os

cache_dir = r"C:\Users\Administrator\.cache"
for item in os.listdir(cache_dir):
    p = os.path.join(cache_dir, item)
    if os.path.isdir(p):
        tot = sum(os.path.getsize(os.path.join(r, f)) for r, d, files in os.walk(p) for f in files)
        print(f"{item:20}: {tot / (1024**2):.1f} MB")
