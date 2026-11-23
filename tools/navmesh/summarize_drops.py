#!/usr/bin/env python3
"""Summarize a scan_all_zones.py result into the committed drop table (tools/navmesh/navmesh-drops.json) and a
per-zone site list for docs/NAVMESH-DROPS.md.

A "site" is a cluster of off-mesh links within 600 units (XY) of each other. A link direction is "stuck-prone" when at
least one of its 51 lip-to-landing samples is on no polygon within the server's pick box (game 32, 32, 64): a bot
replotting from there gets NoPathFound (#438). "Lift" links (rise or drop >= 300 and >= twice the run) are the ones
Pathfinder.IsVerticalLink already carries a bot over in one step.
"""
import argparse, json, math

def lift(x):
    r = abs(x["drop"]); return r >= 300 and r >= 2 * x["xy"]

def commit_leg(x):  # NavLinkLeg.IsCommitLeg (1.2.2 guard: lifts ride as in 1.2 and are never commit legs)
    r = abs(x["drop"]); return r >= 64 and r >= x["xy"] and x["xy"] <= 320 and not lift(x)

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("scan"); ap.add_argument("--table", required=True); ap.add_argument("--sites", required=True)
    a = ap.parse_args()
    zones = sorted((json.loads(l) for l in open(a.scan) if l.strip()), key=lambda z: z["zone"])
    table = {"source": "OfflineDAoC v0.36b full download (the navmeshes 1.2 ships), runtime/server/navmesh",
             "pickBox": [32, 32, 64], "samples": 51, "zones": []}
    sites_out = []
    for z in zones:
        dead = [x for x in z["offmesh"] if x["dead"] > 0]
        entry = {"zone": z["zone"], "name": z["name"], "sha256": z["sha256"],
                 "offmeshDirections": len(z["offmesh"]), "oneWay": sum(1 for x in z["offmesh"] if not x["bidir"]),
                 "steps": len(z["steps"]), "stepsWithDeadSamples": sum(1 for s in z["steps"] if s["dead"] > 0),
                 "stuckProne": [{"from": x["from"], "to": x["to"], "drop": x["drop"], "xy": x["xy"], "dead": x["dead"],
                                 "deadT": x["deadT"], "bidir": x["bidir"], "used": x["used"],
                                 "onlyWay": not x["noJumpComplete"], "lift": lift(x), "commit": commit_leg(x)}
                                for x in dead]}
        table["zones"].append(entry)
        # cluster
        clusters = []
        for x in dead:
            p = x["from"]
            for c in clusters:
                if math.hypot(c["at"][0] - p[0], c["at"][1] - p[1]) <= 600: c["links"].append(x); break
            else: clusters.append({"at": p, "links": [x]})
        for c in clusters:
            L = c["links"]
            sites_out.append({"zone": z["zone"], "name": z["name"], "at": [round(v) for v in c["at"]], "directions": len(L),
                              "lift": sum(lift(x) for x in L), "commit": sum(commit_leg(x) for x in L),
                              "other": sum(1 for x in L if not lift(x) and not commit_leg(x)),
                              "maxDead": max(x["dead"] for x in L), "dz": [round(min(abs(x["drop"]) for x in L)), round(max(abs(x["drop"]) for x in L))],
                              "used": sum(x["used"] for x in L), "oneWay": sum(not x["bidir"] for x in L)})
    with open(a.table, "w") as f:  # one link per line: small diffs, readable
        f.write("{\n" + ",\n".join(f' "{k}": {json.dumps(v)}' for k, v in table.items() if k != "zones") + ',\n "zones": [\n')
        zs = []
        for z in table["zones"]:
            head = {k: v for k, v in z.items() if k != "stuckProne"}
            links = ",\n".join("   " + json.dumps(x) for x in z["stuckProne"])
            zs.append("  " + json.dumps(head)[:-1] + ', "stuckProne": [' + ("\n" + links + "\n  " if links else "") + "]}")
        f.write(",\n".join(zs) + "\n ]\n}\n")
    json.dump(sites_out, open(a.sites, "w"), indent=1)

if __name__ == "__main__":
    main()
