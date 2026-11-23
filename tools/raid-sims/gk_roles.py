"""What each role of the raid did in gk_mirror.py's fights (no benched roles: owner, 2026-11-23).
python3 -I gk_roles.py <size> <unbuffed|buffed> <seeds> [dps]"""
import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gk_mirror as g

if __name__ == "__main__":
    n, case, seeds = int(sys.argv[1]), sys.argv[2], int(sys.argv[3])
    dps = float(sys.argv[4]) if len(sys.argv) > 4 else 80.0
    st = {}
    r = [g.fight(s, g.TAGBURN, n, dps, case, stats=st) for s in range(seeds)]
    print(f"{n} {case}: won {sum(x[0] for x in r)}/{seeds}, mean {sum(x[1] for x in r) / seeds:.1f} min, his walks "
          f"{sum(st['walks']) / seeds:.1f}; raiders who did something: at least {min(st['contributed'])} of {n}")
    for k, v in st.items():
        if k in ("contributed", "raiders", "walks"):
            continue
        print(f"  {k:10s} on him {v[0] / seeds:7.0f}  on trees {v[1] / seeds:7.0f}  trees killed {v[2] / seeds:5.1f}  "
              f"healed {v[3] / seeds:7.0f}")
