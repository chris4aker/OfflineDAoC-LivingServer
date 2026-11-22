// Darkness Falls stair probe: loads a RecastDemo 'MSET' navmesh set exactly as the server's Detour.dll does
// (DetourInterface.cpp LoadNavMesh, 16384 nodes, DT_POLYREF64) and queries it with the server's defaults:
// game (x, y, z) -> Detour (x/32, z/32, y/32); half extents game (32, 32, 64) -> Detour (1, 2, 1); include
// All ^ Disabled, exclude 0; findStraightPath with DT_STRAIGHTPATH_ALL_CROSSINGS.
#include "DetourInterface.hpp"
#include "DetourNavMesh.h"
#include "DetourNavMeshQuery.h"
#include "DetourCommon.h"
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <cmath>
#include <string>
#include <vector>

static const float F = 1.0f / 32.0f;
static void toDt(const float g[3], float d[3]) { d[0] = g[0] * F; d[1] = g[2] * F; d[2] = g[1] * F; }
static void toGame(const float d[3], float g[3]) { g[0] = d[0] / F; g[1] = d[2] / F; g[2] = d[1] / F; }
static float ext[3] = {1.0f, 2.0f, 1.0f};
static dtPolyFlags filt[2] = {(dtPolyFlags)(ALL ^ DISABLED), (dtPolyFlags)0};
static dtNavMesh* mesh; static dtNavMeshQuery* q;

static dtQueryFilter filter() { dtQueryFilter f; f.setIncludeFlags(filt[0]); f.setExcludeFlags(filt[1]); return f; }

static void nearest(const float g[3]) {
    float d[3], p[3], gp[3]; toDt(g, d); dtPolyRef ref = 0; dtQueryFilter f = filter();
    dtStatus s = q->findNearestPoly(d, ext, &f, &ref, p); toGame(p, gp);
    unsigned short fl = 0; unsigned char area = 0; if (ref) { mesh->getPolyFlags(ref, &fl); mesh->getPolyArea(ref, &area); }
    printf("nearest %.0f,%.0f,%.0f -> ref=%llu flags=0x%x area=%u at %.1f,%.1f,%.1f status=0x%x\n", g[0], g[1], g[2],
        (unsigned long long)ref, fl, area, gp[0], gp[1], gp[2], s);
}

static int path(const float a[3], const float b[3], bool print) {
    float s[3], e[3]; toDt(a, s); toDt(b, e); dtQueryFilter f = filter();
    dtPolyRef sr = 0, er = 0; q->findNearestPoly(s, ext, &f, &sr, nullptr); q->findNearestPoly(e, ext, &f, &er, nullptr);
    if (!sr || !er) { printf("path: no start/end poly (start=%llu end=%llu)\n", (unsigned long long)sr, (unsigned long long)er); return -1; }
    dtPolyRef polys[256]; int np = 0; dtStatus ps = q->findPath(sr, er, s, e, &f, polys, &np, 256);
    bool partial = (ps & DT_PARTIAL_RESULT) || (np > 0 && polys[np - 1] != er);
    float pts[256 * 3]; unsigned char sf[256]; dtPolyRef sp[256]; int n = 0;
    q->findStraightPath(s, e, polys, np, pts, sf, sp, &n, 256, DT_STRAIGHTPATH_ALL_CROSSINGS);
    printf("path %.0f,%.0f,%.0f -> %.0f,%.0f,%.0f: polys=%d nodes=%d %s\n", a[0], a[1], a[2], b[0], b[1], b[2], np, n, partial ? "PARTIAL" : "complete");
    float prev[3] = {0, 0, 0};
    for (int i = 0; i < n && print; i++) {
        float g[3]; toGame(&pts[i * 3], g); unsigned short fl = 0; if (sp[i]) mesh->getPolyFlags(sp[i], &fl);
        float dz = i ? g[2] - prev[2] : 0, dxy = i ? std::hypot(g[0] - prev[0], g[1] - prev[1]) : 0;
        printf("  %3d %8.1f %8.1f %8.1f flags=0x%x %s dxy=%.1f dz=%.1f%s\n", i, g[0], g[1], g[2], fl,
            (sf[i] & DT_STRAIGHTPATH_OFFMESH_CONNECTION) ? "OFFMESH" : "", dxy, dz, std::fabs(dz) > 64 ? "  <-- drop" : "");
        memcpy(prev, g, sizeof prev);
    }
    return partial ? 1 : 0;
}

// Every polygon surface under a column (x, y): the heights of the floors a bot could stand on there.
static void column(float x, float y, float zlo, float zhi) {
    float c[3] = {x * F, (zlo + zhi) * 0.5f * F, y * F}, he[3] = {4.0f * F, (zhi - zlo) * 0.5f * F, 4.0f * F};
    dtPolyRef refs[64]; int n = 0; dtQueryFilter f = filter(); q->queryPolygons(c, he, &f, refs, &n, 64);
    printf("col %7.0f %7.0f:", x, y);
    for (int i = 0; i < n; i++) {
        float h; float pt[3] = {x * F, c[1], y * F};
        if (dtStatusSucceed(q->getPolyHeight(refs[i], pt, &h))) { unsigned short fl; mesh->getPolyFlags(refs[i], &fl); printf(" %.0f(0x%x,%llu)", h / F, fl, (unsigned long long)(refs[i] & 0xffffffffull)); }
    }
    printf("\n");
}

// moveAlongSurface at a bot's step: from a to b in steps of `step` game units (what a follower's FormationDestination asks).
static void walk(const float a[3], const float b[3], float step) {
    float s[3], e[3]; toDt(a, s); toDt(b, e); dtQueryFilter f = filter(); dtPolyRef ref = 0; q->findNearestPoly(s, ext, &f, &ref, s);
    float dir[3]; dtVsub(dir, e, s); float len = dtVlen(dir); int steps = (int)std::ceil(len / (step * F));
    float cur[3]; dtVcopy(cur, s);
    printf("walk step=%.0f from %.0f,%.0f,%.0f to %.0f,%.0f,%.0f\n", step, a[0], a[1], a[2], b[0], b[1], b[2]);
    for (int i = 1; i <= steps; i++) {
        float t[3]; dtVlerp(t, s, e, std::fmin(1.0f, (float)i / steps));
        float r[3]; dtPolyRef vis[64]; int nv = 0;
        dtStatus st = q->moveAlongSurface(ref, cur, t, &f, r, vis, &nv, 64);
        if (dtStatusFailed(st) || nv == 0) { printf("  step %d failed 0x%x\n", i, st); return; }
        ref = vis[nv - 1]; float h; if (dtStatusSucceed(q->getPolyHeight(ref, r, &h))) r[1] = h;
        float g[3], gt[3], gc[3]; toGame(r, g); toGame(t, gt); toGame(cur, gc);
        float moved = std::hypot(g[0] - gc[0], g[1] - gc[1]);
        float want = std::hypot(gt[0] - gc[0], gt[1] - gc[1]);
        if (moved + 1 < want * 0.5f) printf("  step %d BLOCKED at %.1f,%.1f,%.1f (moved %.1f of %.1f)\n", i, g[0], g[1], g[2], moved, want);
        dtVcopy(cur, r);
    }
    float g[3]; toGame(cur, g); printf("  end %.1f,%.1f,%.1f\n", g[0], g[1], g[2]);
}

static void offmesh(float x0, float y0, float x1, float y1) {
    int count = 0;
    for (int t = 0; t < mesh->getMaxTiles(); t++) {
        const dtMeshTile* tile = ((const dtNavMesh*)mesh)->getTile(t);
        if (!tile || !tile->header) continue;
        for (int i = 0; i < tile->header->offMeshConCount; i++) {
            const dtOffMeshConnection& c = tile->offMeshCons[i]; float a[3], b[3]; toGame(&c.pos[0], a); toGame(&c.pos[3], b);
            if (a[0] < x0 || a[0] > x1 || a[1] < y0 || a[1] > y1) continue;
            printf("offmesh %.1f,%.1f,%.1f -> %.1f,%.1f,%.1f rad=%.1f flags=0x%x bidir=%d\n", a[0], a[1], a[2], b[0], b[1], b[2], c.rad / F, c.flags, c.flags & 1);
            count++;
        }
    }
    printf("offmesh count in box: %d\n", count);
}

int main(int argc, char** argv) {
    if (argc < 3) { fprintf(stderr, "usage: dfprobe <zone.nav> <cmd> args...\n"); return 2; }
    if (!LoadNavMesh(argv[1], &mesh) || !CreateNavMeshQuery(mesh, &q)) { fprintf(stderr, "load failed\n"); return 1; }
    const dtNavMeshParams* p = mesh->getParams();
    fprintf(stderr, "loaded: tiles max=%d tileW=%.1f orig=%.1f,%.1f,%.1f\n", p->maxTiles, p->tileWidth / F, p->orig[0] / F, p->orig[2] / F, p->orig[1] / F);
    std::string cmd = argv[2]; auto v = [&](int i) { return (float)atof(argv[i]); };
    if (cmd == "nearest") { float g[3] = {v(3), v(4), v(5)}; nearest(g); }
    else if (cmd == "path") { float a[3] = {v(3), v(4), v(5)}, b[3] = {v(6), v(7), v(8)}; return path(a, b, true) < 0 ? 3 : 0; }
    else if (cmd == "column") { float x0 = v(3), y0 = v(4), x1 = v(5), y1 = v(6), st = v(7), zlo = v(8), zhi = v(9);
        float len = std::hypot(x1 - x0, y1 - y0); int n = (int)(len / st);
        for (int i = 0; i <= n; i++) column(x0 + (x1 - x0) * i / std::max(1, n), y0 + (y1 - y0) * i / std::max(1, n), zlo, zhi); }
    else if (cmd == "walk") { float a[3] = {v(3), v(4), v(5)}, b[3] = {v(6), v(7), v(8)}; walk(a, b, v(9)); }
    else if (cmd == "fall") {
        // fall <lip xyz> <air xyz> <landing xyz> <goal xyz>: the diagonal slide the generic mover did (before) against the
        // certified walk-off-the-lip + descend (after), sampled every 2%; then the path on from the landing.
        float lip[3] = {v(3), v(4), v(5)}, air[3] = {v(6), v(7), v(8)}, land[3] = {v(9), v(10), v(11)}, goal[3] = {v(12), v(13), v(14)};
        auto dead = [&](const float* a, const float* b, const char* name) {
            int n = 0, first = -1, last = -1; dtQueryFilter f = filter();
            for (int i = 0; i <= 50; i++) { float g[3], d[3]; for (int k = 0; k < 3; k++) g[k] = a[k] + (b[k] - a[k]) * i / 50.0f;
                toDt(g, d); dtPolyRef r = 0; q->findNearestPoly(d, ext, &f, &r, nullptr); if (!r) { n++; if (first < 0) first = i; last = i; } }
            printf("  %-28s %2d of 51 samples on no polygon%s", name, n, n ? "" : "\n");
            if (n) printf(" (t=%.2f..%.2f)\n", first / 50.0f, last / 50.0f);
            return n; };
        printf("fall %.0f,%.0f,%.0f -> %.0f,%.0f,%.0f\n", lip[0], lip[1], lip[2], land[0], land[1], land[2]);
        dead(lip, land, "before: lip->landing slide");
        dead(lip, air, "after: lip->air (walk off)");
        dead(air, land, "after: air->landing (drop)");
        int r = path(land, goal, false); printf("  from the landing on: %s\n", r == 0 ? "complete path" : r > 0 ? "PARTIAL" : "no poly");
    }
    else if (cmd == "offmesh") offmesh(v(3), v(4), v(5), v(6));
    return 0;
}
