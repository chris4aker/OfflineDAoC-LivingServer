"""Checks Living Server's new-client content patches the way a player gets them: the released 0.37c scripts with
tools/fork-build/content-patches.json applied, run in Godot. No 0.37c download is needed.

    python installed_check.py <Godot 4.7.2 executable> <work folder> [--released-only]

1. The released scripts: source/new-client/godot at the v0.37 release commit (ab4df02a), only the files 0.37c shipped, each checked against
   source/tools/release-baselines/0.37/0.37c-new-client.sha256 (they are the released bytes).
2. Godot imports that folder once, so its global class cache is the released scripts' (a player's game never rescans
   it: a script that names a class the release doesn't have fails to parse).
3. Every content patch of a script is applied as the installer does (released fingerprint, then the fixed one), and
   its result must be the script in source/new-client (a changed script without a new patch fails here).
4. tests/load_check.gd compiles every script with the autoloads present, and tests/issue34_probe.gd checks issue #34
   (skills onto the quickbar, Stealth, the merchant's SELL) with the real interface and a stand-in network client.

--released-only skips step 3: the probe then shows what the released 0.37c scripts do.
The plain Godot build is enough: the scripts are GDScript, and the probe replaces the C# network client.
"""
import base64
import hashlib
import io
import json
import shutil
import subprocess
import sys
import tarfile
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
MANIFEST = REPO / 'source/tools/release-baselines/0.37/0.37c-new-client.sha256'
PATCHES = REPO / 'tools/fork-build/content-patches.json'
# shadowofze's "Release 0.37 / 0.37b / 0.37c" commit (tag v0.37 upstream; on Living Server's branches too)
TAG = 'ab4df02a1ad26192357477dbc912756c6703fb66'
TESTS = ['load_check.gd', 'issue34_probe.gd']


def sha(b: bytes) -> str:
    return hashlib.sha256(b).hexdigest()


def released_tree(out: Path) -> int:
    manifest = {}
    for line in MANIFEST.read_text(encoding='utf-8').splitlines():
        digest, path = line.split('  ', 1)
        manifest[path] = digest
    tar = subprocess.run(['git', '-C', str(REPO), 'archive', TAG, 'source/new-client/godot'],
                         capture_output=True, check=True).stdout
    count = 0
    with tarfile.open(fileobj=io.BytesIO(tar)) as t:
        for m in t.getmembers():
            rel = 'new-client/' + m.name[len('source/new-client/'):]
            if not m.isfile() or rel not in manifest:
                continue
            data = t.extractfile(m).read()
            if sha(data) != manifest[rel]:
                raise SystemExit(f'{TAG[:8]}:{m.name} is not the released 0.37c file')
            dst = out / rel
            dst.parent.mkdir(parents=True, exist_ok=True)
            dst.write_bytes(data)
            count += 1
    return count


def apply_patches(out: Path) -> list:
    done = []
    for e in json.loads(PATCHES.read_text(encoding='utf-8')):
        path = e['Path']
        if not path.startswith('new-client/godot/') or '/content/' in path or 'Ops' not in e:
            continue
        dst = out / path
        old = dst.read_bytes()
        if sha(old) != e['Released']:
            raise SystemExit(f'{path}: the patch is not for the released file')
        new = b''.join(old[o['copy'][0]:o['copy'][0] + o['copy'][1]] if 'copy' in o else base64.b64decode(o['data'])
                       for o in e['Ops'])
        if sha(new) != e['Fixed']:
            raise SystemExit(f'{path}: the patch does not give its fixed file')
        source = REPO / 'source' / path
        if source.read_bytes() != new:
            raise SystemExit(f'{path}: source/{path} changed without a new content patch (or the other way round)')
        dst.write_bytes(new)
        done.append(path)
    return done


def godot(exe: str, project: Path, *args: str) -> subprocess.CompletedProcess:
    try:
        return subprocess.run([exe, '--headless', '--path', str(project), *args], capture_output=True, text=True,
                              errors='replace', timeout=600)
    except subprocess.TimeoutExpired as e:
        out = e.stdout.decode(errors='replace') if isinstance(e.stdout, bytes) else (e.stdout or '')
        return subprocess.CompletedProcess(e.cmd, 124, out + '\n[installed_check] Godot timed out', '')


def main(argv) -> int:
    if len(argv) < 3:
        print(__doc__)
        return 2
    exe, work = argv[1], Path(argv[2])
    released_only = '--released-only' in argv[3:]
    if work.exists():
        shutil.rmtree(work)
    print(f'released 0.37c files from {TAG[:8]}: {released_tree(work)}')
    project = work / 'new-client/godot'
    r = godot(exe, project, '--import')
    if not (project / '.godot/global_script_class_cache.cfg').exists():
        print(r.stdout[-3000:], r.stderr[-3000:])
        raise SystemExit('Godot made no class cache')
    if not released_only:
        for p in apply_patches(work):
            print(f'patched {p}')
    subprocess.run([sys.executable, str(REPO / 'tools/newclient-compat/placeholders.py'), str(project)], check=True)
    (project / 'tests').mkdir(exist_ok=True)
    for t in TESTS:
        shutil.copy(REPO / 'source/new-client/godot/tests' / t, project / 'tests' / t)
    scripts = sorted('res://' + p.relative_to(project).as_posix() for p in project.rglob('*.gd')
                     if 'tests' not in p.relative_to(project).parts)
    r = godot(exe, project, '-s', 'res://tests/load_check.gd', '--', *scripts)
    bad = [l for l in r.stdout.splitlines() if l.startswith('[load_check] FAIL')]
    good = [l for l in r.stdout.splitlines() if l.startswith('[load_check] ok')]
    print(f'load_check: {len(good)} scripts compile, {len(bad)} fail')
    for l in bad:
        print(l)
    if bad or len(good) != len(scripts):
        print('\n'.join(l for l in (r.stdout + r.stderr).splitlines() if 'ERROR' in l)[:6000])
    r = godot(exe, project, '-s', 'res://tests/issue34_probe.gd')
    lines = [l for l in r.stdout.splitlines() if l.startswith('[issue34]')]
    print('\n'.join(lines))
    if r.returncode != 0:
        print('\n'.join(l for l in (r.stdout + r.stderr).splitlines() if 'ERROR' in l)[:6000])
    failed = len(bad) + (len(scripts) - len(good) - len(bad)) + (1 if r.returncode != 0 or not lines else 0)
    print('installed_check: ' + ('ok' if failed == 0 else 'FAILED'))
    return 0 if failed == 0 else 1


if __name__ == '__main__':
    sys.exit(main(sys.argv))
