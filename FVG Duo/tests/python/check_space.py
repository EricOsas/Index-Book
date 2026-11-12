import os, shutil

def get_dir_size(path):
    total = 0
    try:
        with os.scandir(path) as it:
            for entry in it:
                if entry.is_file(follow_symlinks=False):
                    total += entry.stat().st_size
                elif entry.is_dir(follow_symlinks=False):
                    total += get_dir_size(entry.path)
    except Exception:
        pass
    return total

# Check C:\Users\Administrator subdirectories
admin_path = r"C:\Users\Administrator"
print("Scanning C:\\Users\\Administrator subdirectories...")
sizes = []
try:
    with os.scandir(admin_path) as it:
        for entry in it:
            if entry.is_dir(follow_symlinks=False):
                s = get_dir_size(entry.path)
                sizes.append((s, entry.name))
except Exception as e:
    print("Error:", e)

sizes.sort(reverse=True)
for s, name in sizes[:20]:
    print(f"{name:30}: {s / (1024**3):.2f} GB")
