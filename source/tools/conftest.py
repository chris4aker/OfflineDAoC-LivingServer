"""Skip the upstream scripts that need files only their author's PC has (audit 2026-11-13)."""
from pathlib import Path

collect_ignore = []
if not Path("C:/Users/thedo/Desktop/Offline DAOC extra files").exists():
    collect_ignore.append("test_portable_import.py")  # reads the original author's v0.2 release folder
