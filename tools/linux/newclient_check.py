"""Living Server 1.2.1 Linux prototype (docs/LINUX-1.2.1.md): the 0.37c new client as a player gets it, on the Linux
Godot .NET engine. No game content is needed (the converted content stays the player's download).

    python3 -I tools/linux/newclient_check.py <Godot 4.7.2 mono linux executable> <work folder>

1. The released 0.37c scripts and C# sources (tools/newclient-compat/installed_check.py's released tree), with
   Living Server's content patches applied.
2. The C# network client (MMOEngine.dll) built on Linux with the .NET SDK on PATH, against the engine's own packages
   (godot/nuget.config -> ../tools/godot_net, here a link to the Linux engine's folder).
3. Every script compiles in the mono engine with the autoloads and the real C# class present (tests/load_check.gd).
The project it leaves in <work folder>/new-client/godot is what smoke-server.sh starts with `offlinedaoc play --headless`.
"""
import os
import shutil
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / 'tools/newclient-compat'))
import installed_check as ic  # noqa: E402  (released_tree, apply_patches, TAG)


def run(cmd, env, timeout, cwd=None):
    try:
        return subprocess.run(cmd, env=env, cwd=cwd, capture_output=True, text=True, errors='replace', timeout=timeout)
    except subprocess.TimeoutExpired as e:
        out = e.stdout.decode(errors='replace') if isinstance(e.stdout, bytes) else (e.stdout or '')
        return subprocess.CompletedProcess(cmd, 124, out, '')


def main(argv) -> int:
    if len(argv) < 3:
        print(__doc__)
        return 2
    exe, work = Path(argv[1]).resolve(), Path(argv[2]).resolve()
    if work.exists():
        shutil.rmtree(work)
    print(f'released 0.37c files from {ic.TAG[:8]}: {ic.released_tree(work)}')
    project = work / 'new-client/godot'
    (work / 'new-client/tools').mkdir(parents=True, exist_ok=True)
    os.symlink(exe.parent, work / 'new-client/tools/godot_net')
    env = dict(os.environ, XDG_DATA_HOME=str(work / 'xdg/data'), XDG_CONFIG_HOME=str(work / 'xdg/config'),
               XDG_CACHE_HOME=str(work / 'xdg/cache'), NUGET_PACKAGES=str(work / 'nuget'),
               DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1')

    r = run(['dotnet', 'build', str(project / 'MMOEngine.csproj'), '-c', 'Debug', '-v', 'q'], env, 600)
    dll = project / '.godot/mono/temp/bin/Debug/MMOEngine.dll'
    print(f'C# network client built on Linux: {"yes" if r.returncode == 0 and dll.exists() else "NO"}')
    if r.returncode != 0 or not dll.exists():
        print(r.stdout[-4000:], r.stderr[-2000:])
        return 1

    r = run([str(exe), '--headless', '--path', str(project), '--import'], env, 600)
    if not (project / '.godot/global_script_class_cache.cfg').exists():
        print(r.stdout[-3000:], r.stderr[-3000:])
        return 1
    for p in ic.apply_patches(work):
        print(f'patched {p}')
    subprocess.run([sys.executable, '-I', str(REPO / 'tools/newclient-compat/placeholders.py'), str(project)], check=True)
    (project / 'tests').mkdir(exist_ok=True)
    shutil.copy(REPO / 'source/new-client/godot/tests/load_check.gd', project / 'tests/load_check.gd')
    scripts = sorted('res://' + p.relative_to(project).as_posix() for p in project.rglob('*.gd')
                     if 'tests' not in p.relative_to(project).parts)
    r = run([str(exe), '--headless', '--path', str(project), '-s', 'res://tests/load_check.gd', '--', *scripts], env, 600)
    out = r.stdout + r.stderr
    good = [l for l in out.splitlines() if l.startswith('[load_check] ok')]
    bad = [l for l in out.splitlines() if l.startswith('[load_check] FAIL')]
    dotnet_line = next((l for l in out.splitlines() if '.NET' in l), '')
    print(f'load_check on the Linux mono engine: {len(good)} of {len(scripts)} scripts compile, {len(bad)} fail')
    for l in bad:
        print(l)
    if bad or len(good) != len(scripts):
        print('\n'.join(l for l in out.splitlines() if 'ERROR' in l)[:6000])
    failed = len(scripts) - len(good)
    print('newclient_check: ' + ('ok' if failed == 0 else 'FAILED'))
    return 0 if failed == 0 else 1


if __name__ == '__main__':
    sys.exit(main(sys.argv))
