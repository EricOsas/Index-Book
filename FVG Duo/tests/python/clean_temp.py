import os, time

temp_dir = os.environ.get("TEMP", r"C:\Users\Administrator\AppData\Local\Temp")
now = time.time()
deleted_bytes = 0
deleted_files = 0

for root, dirs, files in os.walk(temp_dir):
    for f in files:
        fp = os.path.join(root, f)
        try:
            # If older than 1 hour or temporary download / .tmp / .log
            st = os.stat(fp)
            if now - st.st_mtime > 3600:
                os.remove(fp)
                deleted_bytes += st.st_size
                deleted_files += 1
        except Exception:
            pass

print(f"Deleted {deleted_files} files, freed {deleted_bytes / (1024**2):.1f} MB in Temp")
