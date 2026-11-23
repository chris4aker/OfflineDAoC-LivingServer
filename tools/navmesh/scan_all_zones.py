#!/usr/bin/env python3
"""Scan every server navmesh for off-mesh drops and sheer steps (1.2.2, #438 generalized).

Input: a folder of zoneNNN.nav files (the release the player runs, read only), or --zip-entries to pull each navmesh
from the public v0.36b full download a few at a time with /tmp/navtools/zipcd.py (HTTP ranges, nothing else fetched).
Runs `navscan` (tools/navmesh/navscan.cpp) on each file and writes one JSON object per zone:
  {"zone": id, "name": ..., "region": ..., "sha256": ..., "summary": {...}, "offmesh": [...], "steps": [...]}
to --out (JSON lines). With --delete, each pulled .nav is removed after its scan (the full set is 2.1 GB).

No navmesh is ever written into the repository.
"""
import argparse, hashlib, json, os, re, struct, subprocess, sys, zlib
from concurrent.futures import ThreadPoolExecutor

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ZONES_XML = os.path.join(ROOT, "source/server/GameServer/scripts/dbupdater/insert/Zones.xml")


def zone_names():
    text = open(ZONES_XML, encoding="utf-8").read()
    out = {}
    for block in re.findall(r"<Zones>(.*?)</Zones>", text, re.S):
        z = re.search(r"<ZoneID>(\d+)</ZoneID>", block)
        r = re.search(r"<RegionID>(\d+)</RegionID>", block)
        n = re.search(r"<Name>(.*?)</Name>", block)
        if z:
            out[int(z.group(1))] = (n.group(1) if n else "", int(r.group(1)) if r else -1)
    return out


def pull(entry, dest, zipcd):
    """Extract one zip entry (stored or deflate) to dest via HTTP ranges."""
    head = zipcd.read(entry["lho"], 30)
    nl, el = struct.unpack("<HH", head[26:30])
    data = zipcd.read(entry["lho"] + 30 + nl + el, entry["csz"])
    raw = zlib.decompress(data, -15) if entry["meth"] == 8 else data
    if len(raw) != entry["usz"]:
        raise RuntimeError(f"{entry['name']}: size {len(raw)} != {entry['usz']}")
    tmp = dest + ".part"
    with open(tmp, "wb") as f:
        f.write(raw)
    os.replace(tmp, dest)


def scan(navscan, path, zone, names):
    sha = hashlib.sha256(open(path, "rb").read()).hexdigest()
    lines = subprocess.run([navscan, path, str(zone)], capture_output=True, text=True, check=True).stdout.splitlines()
    rows = [json.loads(line) for line in lines if line.startswith("{")]
    name, region = names.get(zone, ("", -1))
    return {
        "zone": zone, "name": name, "region": region, "sha256": sha, "bytes": os.path.getsize(path),
        "summary": next((r for r in rows if r["kind"] == "summary"), None),
        "offmesh": [r for r in rows if r["kind"] == "offmesh"],
        "steps": [r for r in rows if r["kind"] == "step"],
    }


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--navscan", required=True, help="built tools/navmesh/navscan.cpp")
    ap.add_argument("--out", required=True, help="output JSON lines")
    ap.add_argument("--dir", help="folder with zoneNNN.nav (read only)")
    ap.add_argument("--zip-entries", help="entries.json of the v0.36b download (zipcd.py)")
    ap.add_argument("--work", default="/tmp/navtools/nav", help="where pulled .nav files go")
    ap.add_argument("--delete", action="store_true", help="delete each pulled .nav after its scan")
    ap.add_argument("--jobs", type=int, default=4)
    ap.add_argument("--zones", help="comma-separated zone ids (default: all)")
    args = ap.parse_args()
    names = zone_names()
    want = {int(z) for z in args.zones.split(",")} if args.zones else None
    done = set()
    if os.path.exists(args.out):
        done = {json.loads(line)["zone"] for line in open(args.out) if line.strip()}

    jobs = []
    if args.dir:
        for f in sorted(os.listdir(args.dir)):
            m = re.fullmatch(r"zone(\d+)\.nav", f)
            if m:
                jobs.append((int(m.group(1)), os.path.join(args.dir, f), None))
    elif args.zip_entries:
        sys.path.insert(0, os.path.dirname(os.path.abspath(args.zip_entries)))
        import zipcd  # noqa: E402  (/tmp/navtools/zipcd.py)
        os.makedirs(args.work, exist_ok=True)
        for e in json.load(open(args.zip_entries)):
            m = re.fullmatch(r"[^/]+/runtime/server/navmesh/zone(\d+)\.nav", e["name"])
            if m:
                jobs.append((int(m.group(1)), os.path.join(args.work, f"zone{int(m.group(1)):03d}.nav"), (e, zipcd)))
    else:
        ap.error("--dir or --zip-entries")
    jobs = [j for j in jobs if (want is None or j[0] in want) and j[0] not in done]

    def run(job):
        zone, path, src = job
        if src and not os.path.exists(path):
            pull(src[0], path, src[1])
        result = scan(args.navscan, path, zone, names)
        if src and args.delete:
            os.remove(path)
        return result

    with open(args.out, "a") as out, ThreadPoolExecutor(args.jobs) as pool:
        for result in pool.map(run, jobs):
            out.write(json.dumps(result) + "\n")
            out.flush()
            s = result["summary"] or {}
            print(f"zone {result['zone']:3d} {result['name'][:28]:28s} offmesh={s.get('offmesh')} steps={s.get('steps')}",
                  file=sys.stderr)


if __name__ == "__main__":
    main()
