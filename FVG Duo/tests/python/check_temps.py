import os, shutil

def get_dir_size(path):
    total = 0
    try:
        for root, dirs, files in os.walk(path):
            for f in files:
                try:
                    fp = os.path.join(root, f)
                    total += os.path.getsize(fp)
                except Exception:
                    pass
    except Exception:
        pass
    return total

p1 = r"C:\Users\Administrator\AppData\Local\Temp"
p2 = r"C:\Windows\Temp"
p3 = r"C:\Users\Administrator\.cache"
p4 = r"C:\Users\Administrator\AppData\Local\pip\cache"
p5 = r"C:\Users\Administrator\AppData\Local\npm-cache"

print(f"Local Temp: {get_dir_size(p1) / (1024**3):.2f} GB")
print(f"Windows Temp: {get_dir_size(p2) / (1024**3):.2f} GB")
print(f".cache: {get_dir_size(p3) / (1024**3):.2f} GB")
print(f"pip cache: {get_dir_size(p4) / (1024**3):.2f} GB")
print(f"npm cache: {get_dir_size(p5) / (1024**3):.2f} GB")
