"""Rebuild steps for the RvR check-spot catalog (source/server/GameServer/bots/autonomous/data/rvr-checkspots.json).

The catalog is its own seed: spots (id, type, label, x,y,z, radius, levels, via) and circuits are edited by hand in
the JSON. These steps refresh what is derived from the meshes and seal it:

  request  --db <clean release world .db> --catalog <json> --out <request.json>
           Writes the standing-point search request. A spot's own monsters are the aggressive realm-0 spawns within
           its radius whose level is inside its level band; other aggressive spawns within radius + 3,000 count
           against a candidate but do not exclude it. Clearance = aggro range (500 when unset, at most 900) + roaming
           range (at most 800) + 150. Reads only static tables (Mob, Zones), never saved progress.
  stands   --catalog <json> --search <search-out.txt>
           Writes each spot's standing point from UT_RvrCheckSpotCatalog's search output (OFFLINE_DAOC_CHECKSPOT_SEARCH).
  seal     --catalog <json> --report <proof-out.txt>
           Writes proof_hash from a UT_RvrCheckSpotCatalog validation report, only when every check passed.

Use a clean release world (build_release_037.py output, or `rvr_catalog_inputs.py release-world` on a copy of the
static tables), never a played database.
"""
import argparse
import json
import math
import re
import sqlite3


def load(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def save(path, doc):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(doc, indent=1, ensure_ascii=False) + "\n")


def clearance(aggro_range, roam):
    r = aggro_range if aggro_range and aggro_range > 0 else 500
    return min(r, 900) + min(max(roam or 0, 0), 800) + 150


def request(args):
    doc = load(args.catalog)
    db = sqlite3.connect(f"file:{args.db}?mode=ro", uri=True)
    out = []
    for fr in doc["frontiers"]:
        region = fr["region"]
        rows = db.execute("select X,Y,Z,Level,AggroLevel,AggroRange,RoamingRange,ClassType from Mob "
                          "where Region=? and Realm=0 and Level>0", (region,)).fetchall()
        rows = [r for r in rows if "Guard" not in (r[7] or "") and (r[4] or 0) > 0]
        for s in fr["spots"]:
            zone = next(z for z in fr["zones"] if z["x"] <= s["x"] < z["x"] + z["width"] and z["y"] <= s["y"] < z["y"] + z["height"])
            lo, hi = s.get("levels", [0, 0])
            own, other = [], []
            for x, y, z, level, _, aggro_range, roam, _ in rows:
                d = math.hypot(x - s["x"], y - s["y"])
                item = [x, y, z, clearance(aggro_range, roam)]
                if d <= s["radius"] and lo <= level <= hi:
                    own.append(item)
                elif d <= s["radius"] + 3000:
                    other.append(item)
            out.append({"id": f"{region}:{s['id']}", "zone": [zone["id"], zone["x"], zone["y"], zone["width"], zone["height"]],
                        "x": s["x"], "y": s["y"], "z": s["z"], "radius": s["radius"], "own": own, "other": other})
    save(args.out, {"spots": out})


def stands(args):
    doc = load(args.catalog)
    found = {}
    with open(args.search, encoding="utf-8") as f:
        for line in f:
            m = re.match(r"stand (\d+):(\S+) (-?\d+),(-?\d+),(-?\d+)", line)
            if m:
                found[(int(m.group(1)), m.group(2))] = [int(m.group(i)) for i in (3, 4, 5)]
    missing = []
    for fr in doc["frontiers"]:
        for s in fr["spots"]:
            p = found.get((fr["region"], s["id"]))
            if p is None:
                missing.append(s["id"])
            else:
                s["stand"] = {"x": p[0], "y": p[1], "z": p[2]}
    doc["proof_hash"] = ""
    save(args.catalog, doc)
    print(f"{sum(len(f['spots']) for f in doc['frontiers']) - len(missing)} standing points; none found for {missing}")


def seal(args):
    doc = load(args.catalog)
    with open(args.report, encoding="utf-8") as f:
        last = f.read().splitlines()[-1]
    m = re.match(r"proof_hash (sha256:[0-9a-f]{64}) recorded \S*: \S+; (\d+) failed checks", last)
    if not m or m.group(2) != "0":
        raise SystemExit(f"not sealed: {last}")
    doc["proof_hash"] = m.group(1)
    save(args.catalog, doc)
    print(doc["proof_hash"])


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    p = sub.add_parser("request"); p.add_argument("--db", required=True); p.add_argument("--catalog", required=True); p.add_argument("--out", required=True)
    p.set_defaults(run=request)
    p = sub.add_parser("stands"); p.add_argument("--catalog", required=True); p.add_argument("--search", required=True)
    p.set_defaults(run=stands)
    p = sub.add_parser("seal"); p.add_argument("--catalog", required=True); p.add_argument("--report", required=True)
    p.set_defaults(run=seal)
    args = parser.parse_args()
    args.run(args)


if __name__ == "__main__":
    main()
