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
#include <unordered_set>
#include <deque>
#include <array>

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


// Every polygon reachable from a start polygon over the mesh's links (directed: an off-mesh link is followed only
// from its start), with the server's filter. Bounded at 4 million polygons.
static std::unordered_set<dtPolyRef> flood(dtPolyRef start) {
    std::unordered_set<dtPolyRef> seen; std::deque<dtPolyRef> open; dtQueryFilter f = filter();
    if (!start) return seen; seen.insert(start); open.push_back(start);
    while (!open.empty() && seen.size() < 4000000) {
        dtPolyRef ref = open.front(); open.pop_front(); const dtMeshTile* tile = nullptr; const dtPoly* poly = nullptr;
        if (dtStatusFailed(mesh->getTileAndPolyByRef(ref, &tile, &poly))) continue;
        for (unsigned int i = poly->firstLink; i != DT_NULL_LINK; i = tile->links[i].next) {
            dtPolyRef next = tile->links[i].ref; if (!next || seen.count(next)) continue;
            const dtMeshTile* nt = nullptr; const dtPoly* np = nullptr;
            if (dtStatusFailed(mesh->getTileAndPolyByRef(next, &nt, &np)) || !(np->flags & f.getIncludeFlags()) || (np->flags & f.getExcludeFlags())) continue;
            seen.insert(next); open.push_back(next);
        }
    }
    return seen;
}

static dtPolyRef pick(const float g[3]) {
    float d[3]; toDt(g, d); dtPolyRef ref = 0; dtQueryFilter f = filter(); q->findNearestPoly(d, ext, &f, &ref, nullptr); return ref;
}

// The polygons under a column (x, y) within [zlo, zhi], with their floor heights.
static std::vector<std::pair<dtPolyRef, float>> floors(float x, float y, float zlo, float zhi) {
    std::vector<std::pair<dtPolyRef, float>> out;
    float c[3] = {x * F, (zlo + zhi) * 0.5f * F, y * F}, he[3] = {4.0f * F, (zhi - zlo) * 0.5f * F, 4.0f * F};
    dtPolyRef refs[64]; int n = 0; dtQueryFilter f = filter(); q->queryPolygons(c, he, &f, refs, &n, 64);
    for (int i = 0; i < n; i++) { float h; float pt[3] = {x * F, c[1], y * F};
        if (dtStatusSucceed(q->getPolyHeight(refs[i], pt, &h))) out.push_back({refs[i], h / F}); }
    return out;
}

// The server's corridor check (AutonomousZoneItinerary.CalculateCompleteCorridorCore): up to 16 findPath +
// findStraightPath segments, each partial one continued from its last node, complete when a segment ends within 48.
static int corridor(const float a[3], const float b[3], bool print) {
    float cur[3] = {a[0], a[1], a[2]}; std::vector<std::array<float, 3>> visited{{a[0], a[1], a[2]}};
    for (int seg = 0; seg < 16; seg++) {
        float s[3], e[3]; toDt(cur, s); toDt(b, e); dtQueryFilter f = filter();
        dtPolyRef sr = 0, er = 0; q->findNearestPoly(s, ext, &f, &sr, nullptr); q->findNearestPoly(e, ext, &f, &er, nullptr);
        if (!sr || !er) { if (print) printf("  segment %d: no start/end poly\n", seg); return -1; }
        dtPolyRef polys[256]; int np = 0; q->findPath(sr, er, s, e, &f, polys, &np, 256);
        float pts[512 * 3]; unsigned char sf[512]; dtPolyRef sp[512]; int n = 0;
        q->findStraightPath(s, e, polys, np, pts, sf, sp, &n, 512, DT_STRAIGHTPATH_ALL_CROSSINGS);
        if (n < 1) return -1; float last[3]; toGame(&pts[(n - 1) * 3], last);
        float d2 = (last[0] - b[0]) * (last[0] - b[0]) + (last[1] - b[1]) * (last[1] - b[1]) + (last[2] - b[2]) * (last[2] - b[2]);
        if (print) printf("  segment %d: %d polys, ends %.0f,%.0f,%.0f (%.0f from the goal)\n", seg, np, last[0], last[1], last[2], std::sqrt(d2));
        if (d2 <= 48 * 48) return 0;
        for (auto& v : visited) if ((v[0] - last[0]) * (v[0] - last[0]) + (v[1] - last[1]) * (v[1] - last[1]) + (v[2] - last[2]) * (v[2] - last[2]) < 1) return 1;
        visited.push_back({last[0], last[1], last[2]}); memcpy(cur, last, sizeof cur);
    }
    return 1;
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
    else if (cmd == "climb") {
        // climb <landing xyz> <air xyz> <lip xyz> <goal xyz>: one certified fall walked backwards (#438, 1.2.2). The path
        // the navmesh gives up from the landing (before), the rise over the landing and the step onto the lip (after),
        // sampled every 2% (a bot stopped there: on a polygon, or inside the drop's landing repair), then the path on.
        float land[3] = {v(3), v(4), v(5)}, air[3] = {v(6), v(7), v(8)}, lip[3] = {v(9), v(10), v(11)}, goal[3] = {v(12), v(13), v(14)};
        auto dead = [&](const float* a, const float* b, const char* name) {
            int n = 0; dtQueryFilter f = filter();
            for (int i = 0; i <= 50; i++) { float g[3], d[3]; for (int k = 0; k < 3; k++) g[k] = a[k] + (b[k] - a[k]) * i / 50.0f;
                toDt(g, d); dtPolyRef r = 0; q->findNearestPoly(d, ext, &f, &r, nullptr); if (!r) n++; }
            printf("  %-28s %2d of 51 samples on no polygon\n", name, n); return n; };
        printf("climb %.0f,%.0f,%.0f -> %.0f,%.0f,%.0f\n", land[0], land[1], land[2], lip[0], lip[1], lip[2]);
        int before = path(land, goal, false); printf("  before: landing to goal: %s\n", before == 0 ? "complete path" : before > 0 ? "PARTIAL" : "no poly");
        dead(land, air, "after: landing->air (rise)");
        dead(air, lip, "after: air->lip (step on)");
        int r = path(lip, goal, false); printf("  from the lip on: %s\n", r == 0 ? "complete path" : r > 0 ? "PARTIAL" : "no poly");
    }
    else if (cmd == "offmesh") offmesh(v(3), v(4), v(5), v(6));
    else if (cmd == "reach") {
        // reach sx sy sz x y z [x y z ...]: which targets the start's polygon reaches over mesh links (directed).
        float a[3] = {v(3), v(4), v(5)}; dtPolyRef sr = pick(a); auto set = flood(sr);
        printf("reach from %.0f,%.0f,%.0f (poly %llu): %zu polygons\n", a[0], a[1], a[2], (unsigned long long)sr, set.size());
        for (int i = 6; i + 2 < argc; i += 3) { float b[3] = {v(i), v(i + 1), v(i + 2)}; dtPolyRef r = pick(b);
            printf("  %.0f,%.0f,%.0f poly %llu: %s\n", b[0], b[1], b[2], (unsigned long long)r, !r ? "NO POLY" : set.count(r) ? "reached" : "NOT reached"); }
    }
    else if (cmd == "corridor") { float a[3] = {v(3), v(4), v(5)}, b[3] = {v(6), v(7), v(8)}; int r = corridor(a, b, true);
        printf("corridor %s\n", r == 0 ? "complete" : r > 0 ? "INCOMPLETE" : "no poly"); }
    else if (cmd == "batch") {
        // batch: one query per stdin line, for scripts (seam_route_sim.py), one answer line each:
        //   from x y z        -> "from <poly> <polygons reached>"  (flood from the point's polygon; later lines test it)
        //   at x y z          -> "at <poly> <1 reached | 0 not>"
        //   floors x y zlo zhi -> "floors <z> <reached> ..." every floor under (x, y)
        char line[512]; std::unordered_set<dtPolyRef> set;
        while (fgets(line, sizeof line, stdin)) {
            char c[16]; float x, y, z, w; int n = sscanf(line, "%15s %f %f %f %f", c, &x, &y, &z, &w);
            if (n >= 4 && !strcmp(c, "from")) { float g[3] = {x, y, z}; dtPolyRef r = pick(g); set = flood(r); printf("from %llu %zu\n", (unsigned long long)r, set.size()); }
            else if (n >= 4 && !strcmp(c, "at")) { float g[3] = {x, y, z}; dtPolyRef r = pick(g); printf("at %llu %d\n", (unsigned long long)r, r && set.count(r) ? 1 : 0); }
            else if (n >= 5 && !strcmp(c, "floors")) { auto fl = floors(x, y, z, w); printf("floors"); for (auto& p : fl) printf(" %.1f %d", p.second, set.count(p.first) ? 1 : 0); printf("\n"); }
            else printf("?\n");
            fflush(stdout);
        }
    }
    else if (cmd == "border") {
        // border sx sy sz axis(x|y) border lo hi step zlo zhi: along a zone border, every floor 64 inside (the side
        // the start is on) and whether the start reaches it over the mesh links.
        float a[3] = {v(3), v(4), v(5)}; bool vx = argv[6][0] == 'x'; float bd = v(7), lo = v(8), hi = v(9), st = v(10), zlo = v(11), zhi = v(12);
        float side = (vx ? a[0] : a[1]) < bd ? -64.0f : 64.0f; auto set = flood(pick(a));
        printf("border %s=%.0f from %.0f,%.0f,%.0f (%zu polygons reached), samples at %s=%.0f\n", vx ? "x" : "y", bd, a[0], a[1], a[2], set.size(), vx ? "x" : "y", bd + side);
        for (float t = lo; t <= hi; t += st) { float x = vx ? bd + side : t, y = vx ? t : bd + side; auto fl = floors(x, y, zlo, zhi);
            printf("  %7.0f:", t); for (auto& p : fl) printf(" %.0f%s", p.second, set.count(p.first) ? "*" : ""); printf("\n"); }
    }
    return 0;
}
