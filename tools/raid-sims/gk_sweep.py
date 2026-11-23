"""Sweep of gk_mirror.py: python3 -I gk_sweep.py <sizes> <rules 0,1,2> <dps list> <seeds> [cases].
The 1.2.2 table: python3 -I gk_sweep.py 40,48,56,64,72,80,104,120 0,1,2 50,80,120 32"""
import sys
import os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gk_mirror as g
from multiprocessing import Pool
def job(a):
    seed, rule, n, dps, case = a
    return (rule, n, dps, case, g.fight(seed, rule, n, dps, case))
if __name__ == "__main__":
    sizes = [int(x) for x in sys.argv[1].split(",")]
    rules = [int(x) for x in sys.argv[2].split(",")]
    dpss = [float(x) for x in sys.argv[3].split(",")]
    seeds = int(sys.argv[4])
    cases = sys.argv[5].split(",") if len(sys.argv) > 5 else ["unbuffed", "buffed"]
    jobs = [(s, r, n, d, c) for r in rules for n in sizes for d in dpss for c in cases for s in range(seeds)]
    with Pool(2) as p:
        res = p.map(job, jobs, chunksize=4)
    agg = {}
    for rule, n, d, c, x in res:
        a = agg.setdefault((rule, n, d, c), [0, 0.0, 0.0, 0, 0])
        a[0] += x[0]; a[1] += x[1]; a[2] += 100 - x[4]; a[3] += 1; a[4] = max(a[4], x[2])
    for k in sorted(agg, key=lambda k: (k[0], k[3], k[2], k[1])):
        a = agg[k]
        print(f"{g.RULE_NAMES[k[0]]:8s} {k[3]:4s} dps={k[2]:4.0f} n={k[1]:3d}: won {a[0]:2d}/{a[3]} mean {a[1]/a[3]:5.1f} min, dealt {a[2]/a[3]:5.1f}%, max trees {a[4]}")
