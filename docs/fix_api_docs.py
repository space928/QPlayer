import os
import sys

base_path = sys.argv[1]
for p in os.listdir(base_path):
 p = os.path.join(base_path, p)
 with open(p, "rt") as f:
  lines = f.readlines()
 with open(p, "wt") as f:
  f.writelines(['---\n', f'title: {p[:-3]}\n', '---\n', '\n'] + lines)