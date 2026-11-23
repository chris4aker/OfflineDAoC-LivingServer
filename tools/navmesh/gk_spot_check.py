"""The Green Knight's spots on the Forest Sauvage mesh (zone011.nav, read only): for each of his four fight places
(spawn and the three walk points in zone 11), each of 8 directions of his tank and each spot distance (650 / 700 / 850 /
1,300), the first of EpicEncounterTactics.GreenKnightSpotCandidates' turns with a floor within 181 that sees him
(dfprobe snap and los, the server's HasLineOfSight). python3 -I gk_spot_check.py zone011.nav ./dfprobe"""
import subprocess, math, sys
nav = sys.argv[1] if len(sys.argv) > 1 else 'zone011.nav'
anchors = [(592990, 418687, 5012), (593256, 420780, 5050), (596053, 420171, 4918), (590876, 418052, 4942)]
def batch(lines):
    return subprocess.run([sys.argv[2] if len(sys.argv) > 2 else './dfprobe', nav, 'batch'], input='\n'.join(lines) + '\n', capture_output=True, text=True).stdout.splitlines()
res = {}
for ax, ay, az in anchors:
    az = float(batch([f"snap {ax} {ay} {az}"])[0].split()[4])
    for k in range(8):
        base = math.radians(45 * k)
        for d in (650, 700, 850, 1300):
            ok_at = None
            for turn in (0, 30, -30, 60, -60, 90, -90):
                a = base + math.radians(turn)
                x, y = ax + d * math.cos(a), ay + d * math.sin(a)
                if not (565248 <= x < 630784 and 417792 <= y < 483328):
                    continue  # another zone: the server snaps it on that zone's mesh (not checked here)
                _, ref, sx, sy, sz = batch([f"snap {x:.0f} {y:.0f} {az}"])[0].split()
                if ref == '0' or math.hypot(float(sx) - x, float(sy) - y) > 181:
                    continue
                los = batch([f"los {sx} {sy} {float(sz)+60} {ax} {ay} {az+60}"])[0].split()[1]
                if los == '1':
                    ok_at = turn
                    break
            res[(ax, ay, k * 45, d)] = ok_at
tot = len(res); found = sum(1 for v in res.values() if v is not None); straight = sum(1 for v in res.values() if v == 0)
print(f"spots checked {tot}: a floor that sees him found {found} (straight on the line {straight}, turned {found - straight}); none {tot - found}")
for key, v in res.items():
    if v is None: print('  none:', key)
