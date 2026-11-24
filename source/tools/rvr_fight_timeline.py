#!/usr/bin/env python3
"""Per-fight and per-siege timelines from one server.log (issue #505). A front door to rvr_hour.py:

    python3 source/tools/rvr_fight_timeline.py server.log            # every fight
    python3 source/tools/rvr_fight_timeline.py server.log 12         # fight 12 (r12: a fight rebuilt from an older log)
    python3 source/tools/rvr_fight_timeline.py server.log --sieges   # every siege, march to end
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import rvr_hour  # noqa: E402


def main(argv=None) -> int:
    argv = list(sys.argv[1:] if argv is None else argv)
    if len(argv) >= 2 and not argv[1].startswith("-"):
        return rvr_hour.main([argv[0], "--fight", argv[1], *argv[2:]])
    if "--sieges" in argv:
        return rvr_hour.main(argv)
    return rvr_hour.main([*argv, "--fights"])


if __name__ == "__main__":
    sys.exit(main())
