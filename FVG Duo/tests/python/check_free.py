import shutil
total, used, free = shutil.disk_usage("C:\\")
print(f"C: Total={total/(1024**3):.2f} GB, Used={used/(1024**3):.2f} GB, Free={free/(1024**3):.2f} GB ({free/(1024**2):.1f} MB)")
