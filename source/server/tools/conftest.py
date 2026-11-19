"""Skip the client-patch emulation tests when their emulator isn't installed (audit 2026-11-13)."""
import importlib.util

collect_ignore = []
if importlib.util.find_spec("unicorn") is None or importlib.util.find_spec("pefile") is None:
    # They run the patched game.dll's code in the unicorn emulator; pip install unicorn pefile to run them.
    collect_ignore += ["test_bot_map_client.py", "test_native_raid80_client.py", "test_native_raid_client.py", "test_native_raid_probe.py"]
