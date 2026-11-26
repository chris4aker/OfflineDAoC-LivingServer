"""Checks the installer's screenshot runs (.github/workflows/installer.yml, "Pictures of every page"; issue #623).

The workflow runs `LivingServer-Setup.exe --screenshots tools/installer/shots/scale-<s> --scale <s>` once per scale and
writes the run's exit code to `tools/installer/shots/scale-<s>.exit`. This script fails the job when any run failed:

    python tools/installer/check_shots.py --root tools/installer/shots --scales 1 1.5

A scale fails when its exit file is missing, its exit code is not 0, or one of the wizard's page pictures (PAGES,
Program.cs `Screenshots`) is missing or empty. The dialog picture (09-dialog) is taken from a timer and only when the
dialog is open, so a missing one is a warning, not a failure. Standard library only.
"""
import argparse
import sys
from pathlib import Path

# The page pictures Program.cs Screenshots() always writes, in its order (test_check_shots.py reads Program.cs to keep
# this in step).
PAGES = ("01-welcome", "02-choose", "03-prepare-new", "04-prepare-update", "05-prepare-installed", "06-install",
         "07-done", "08-failed")
OPTIONAL = ("09-dialog",)


def check_scale(root, scale):
    """(errors, warnings) for one scale's run."""
    errors, warnings = [], []
    exit_file = root / f"scale-{scale}.exit"
    if not exit_file.is_file():
        return [f"scale {scale}: no exit code ({exit_file.name}): the run did not finish"], warnings
    text = exit_file.read_text(encoding="utf-8-sig").strip()
    try:
        code = int(text)
    except ValueError:
        return [f"scale {scale}: exit code {text!r} is not a number"], warnings
    if code != 0:
        errors.append(f"scale {scale}: the screenshot run exited {code}")
    folder = root / f"scale-{scale}"
    for name in PAGES:
        png = folder / f"{name}.png"
        if not png.is_file():
            errors.append(f"scale {scale}: {name}.png is missing")
        elif png.stat().st_size == 0:
            errors.append(f"scale {scale}: {name}.png is empty")
    for name in OPTIONAL:
        png = folder / f"{name}.png"
        if not png.is_file() or png.stat().st_size == 0:
            warnings.append(f"scale {scale}: {name}.png was not taken")
    return errors, warnings


def run(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--root", required=True, help="the shots folder (scale-<s>/ and scale-<s>.exit inside)")
    ap.add_argument("--scales", nargs="+", required=True)
    a = ap.parse_args(argv)
    root = Path(a.root)
    errors, warnings = [], []
    for scale in a.scales:
        e, w = check_scale(root, scale)
        errors += e
        warnings += w
    for w in warnings:
        print(f"::warning title=Installer screenshots::{w}")
    for e in errors:
        print(f"::error title=Installer screenshots::{e}")
    if errors:
        return 1
    print(f"screenshots ok: {len(a.scales)} scale(s), {len(PAGES)} pages each")
    return 0


if __name__ == "__main__":
    sys.exit(run())
