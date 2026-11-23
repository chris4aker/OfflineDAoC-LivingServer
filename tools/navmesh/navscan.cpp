// Navmesh drop scan (1.2.2, the Darkness Falls stair stall of #438 generalized): for one server navmesh set (RecastDemo
// 'MSET', loaded exactly as the server's Detour.dll does: DetourInterface.cpp LoadNavMesh, 16384 nodes, DT_POLYREF64),
// print one JSON line per off-mesh connection and per sheer poly-to-poly step, measured with the server's query defaults
// (game (x, y, z) -> Detour (x/32, z/32, y/32); pick box game (32, 32, 64) = Detour (1, 2, 1); include All ^ Disabled).
//
// For every off-mesh connection, in each direction it can be taken (one-way: start -> end; bidirectional: both):
//   dead      samples (of 51) on the straight start -> end segment where findNearestPoly finds no polygon: the stuck
//             condition of #438 (a bot replotting from there gets NoPathFound and, as a companion, stands there for good)
//   deadFall  the same for the walk-off-the-lip + vertical descend shape the DF certificate uses (drops only)
//   connected both ends linked to the mesh (the link can be planned through)
//   used      a straight path from the start to the end crosses this very connection
//   detour    path length without Jump links divided by the length with them (0: no way round without the link)
// For every link between two ground polygons whose detail heights at the shared edge differ by more than 64 units
// ("step"): a sheer step a path can cross without an off-mesh link, with its dead samples (of 51) on the vertical
// between the two floors (only a step over twice the pick box's 64-unit half height can have any).
#include "DetourInterface.hpp"
#include "DetourNavMesh.h"
#include "DetourNavMeshQuery.h"
#include "DetourCommon.h"
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <cmath>
#include <string>

static const float F = 1.0f / 32.0f;
static void toDt(const float g[3], float d[3]) { d[0] = g[0] * F; d[1] = g[2] * F; d[2] = g[1] * F; }
static void toGame(const float d[3], float g[3]) { g[0] = d[0] / F; g[1] = d[2] / F; g[2] = d[1] / F; }
static float ext[3] = {1.0f, 2.0f, 1.0f};
static dtNavMesh* mesh; static dtNavMeshQuery* q;

static dtQueryFilter filter(bool jump) {
    dtQueryFilter f; f.setIncludeFlags((dtPolyFlags)(jump ? (ALL ^ DISABLED) : (ALL ^ DISABLED ^ JUMP))); f.setExcludeFlags(0); return f;
}

static bool onPoly(const float g[3]) {
    float d[3]; toDt(g, d); dtPolyRef r = 0; dtQueryFilter f = filter(true); q->findNearestPoly(d, ext, &f, &r, nullptr); return r != 0;
}

static int deadSamples(const float a[3], const float b[3], int* first, int* last) {
    int n = 0; *first = *last = -1;
    for (int i = 0; i <= 50; i++) {
        float g[3]; for (int k = 0; k < 3; k++) g[k] = a[k] + (b[k] - a[k]) * i / 50.0f;
        if (!onPoly(g)) { n++; if (*first < 0) *first = i; *last = i; }
    }
    return n;
}

// Length (game units) of the straight path a -> b, and whether it is complete and crosses the off-mesh connection `con`.
static float pathLength(const float a[3], const float b[3], bool jump, dtPolyRef con, bool* complete, bool* crosses) {
    *complete = false; if (crosses) *crosses = false;
    float s[3], e[3]; toDt(a, s); toDt(b, e); dtQueryFilter f = filter(jump);
    dtPolyRef sr = 0, er = 0; q->findNearestPoly(s, ext, &f, &sr, nullptr); q->findNearestPoly(e, ext, &f, &er, nullptr);
    if (!sr || !er) return 0;
    static dtPolyRef polys[2048]; int np = 0; dtStatus ps = q->findPath(sr, er, s, e, &f, polys, &np, 2048);
    if (np <= 0) return 0;
    *complete = !(ps & DT_PARTIAL_RESULT) && polys[np - 1] == er;
    if (crosses) for (int i = 0; i < np; i++) if (polys[i] == con) *crosses = true;
    static float pts[2048 * 3]; int n = 0; q->findStraightPath(s, e, polys, np, pts, nullptr, nullptr, &n, 2048, 0);
    float len = 0; for (int i = 1; i < n; i++) len += dtVdist(&pts[(i - 1) * 3], &pts[i * 3]) / F;
    return len;
}

static void direction(int zone, dtPolyRef ref, const dtOffMeshConnection& c, unsigned short flags, bool connected,
    const float a[3], const float b[3], bool bidir, const char* dir) {
    int f0, f1, ff0, ff1, fa0, fa1;
    int dead = deadSamples(a, b, &f0, &f1);
    float drop = a[2] - b[2], xy = std::hypot(b[0] - a[0], b[1] - a[1]);
    int deadFall = -1;
    if (drop > 0) {
        float air[3] = {b[0], b[1], a[2]};
        deadFall = deadSamples(a, air, &ff0, &ff1) + deadSamples(air, b, &fa0, &fa1);
    }
    bool complete = false, crosses = false, completeNoJump = false;
    float with = pathLength(a, b, true, ref, &complete, &crosses);
    float without = pathLength(a, b, false, 0, &completeNoJump, nullptr);
    printf("{\"zone\":%d,\"kind\":\"offmesh\",\"dir\":\"%s\",\"bidir\":%s,\"from\":[%.1f,%.1f,%.1f],\"to\":[%.1f,%.1f,%.1f],"
           "\"drop\":%.1f,\"xy\":%.1f,\"rad\":%.1f,\"flags\":%u,\"connected\":%s,\"dead\":%d,\"deadT\":[%.2f,%.2f],\"deadFall\":%d,"
           "\"used\":%s,\"pathLen\":%.0f,\"noJumpLen\":%.0f,\"noJumpComplete\":%s}\n",
        zone, dir, bidir ? "true" : "false", a[0], a[1], a[2], b[0], b[1], b[2], drop, xy, c.rad / F, flags,
        connected ? "true" : "false", dead, f0 / 50.0f, f1 / 50.0f, deadFall, (complete && crosses) ? "true" : "false",
        with, without, completeNoJump ? "true" : "false");
}

int main(int argc, char** argv) {
    if (argc < 3) { fprintf(stderr, "usage: navscan <zone.nav> <zone id> [stepThreshold=64]\n"); return 2; }
    if (!LoadNavMesh(argv[1], &mesh) || !CreateNavMeshQuery(mesh, &q)) { fprintf(stderr, "load failed %s\n", argv[1]); return 1; }
    int zone = atoi(argv[2]); float stepLimit = argc > 3 ? (float)atof(argv[3]) : 64.0f;
    const dtNavMesh* cm = mesh; int tiles = 0, polys = 0, cons = 0, steps = 0;
    for (int t = 0; t < cm->getMaxTiles(); t++) {
        const dtMeshTile* tile = cm->getTile(t);
        if (!tile || !tile->header) continue;
        tiles++; polys += tile->header->polyCount;
        dtPolyRef base = cm->getPolyRefBase(tile);
        for (int i = 0; i < tile->header->offMeshConCount; i++) {
            const dtOffMeshConnection& c = tile->offMeshCons[i]; cons++;
            const dtPoly& poly = tile->polys[c.poly]; dtPolyRef ref = base | (dtPolyRef)c.poly;
            int links = 0; for (unsigned int l = poly.firstLink; l != DT_NULL_LINK; l = tile->links[l].next) links++;
            float a[3], b[3]; toGame(&c.pos[0], a); toGame(&c.pos[3], b);
            bool bidir = (c.flags & DT_OFFMESH_CON_BIDIR) != 0;
            direction(zone, ref, c, poly.flags, links >= 2, a, b, bidir, "forward");
            if (bidir) direction(zone, ref, c, poly.flags, links >= 2, b, a, bidir, "reverse");
        }
        // Sheer steps between two ground polygons that share an edge (inside a tile or across a tile border).
        for (int i = 0; i < tile->header->polyCount; i++) {
            const dtPoly& p = tile->polys[i];
            if (p.getType() != DT_POLYTYPE_GROUND || (p.flags & DISABLED)) continue;
            dtPolyRef ref = base | (dtPolyRef)i;
            for (unsigned int l = p.firstLink; l != DT_NULL_LINK; l = tile->links[l].next) {
                const dtLink& link = tile->links[l];
                if (link.edge >= p.vertCount || link.ref <= ref) continue; // each pair once
                const dtMeshTile* nt = nullptr; const dtPoly* np = nullptr;
                if (dtStatusFailed(cm->getTileAndPolyByRef(link.ref, &nt, &np)) || np->getType() != DT_POLYTYPE_GROUND ||
                    (np->flags & DISABLED)) continue;
                const float* v0 = &tile->verts[p.verts[link.edge] * 3];
                const float* v1 = &tile->verts[p.verts[(link.edge + 1) % p.vertCount] * 3];
                // The shared part of the edge: a tile-border link covers only its portal (bmin..bmax of 255 along the edge).
                float t = link.side == 0xff ? 0.5f : (link.bmin + link.bmax) * 0.5f / 255.0f;
                float mid[3]; dtVlerp(mid, v0, v1, t);
                float h0[3], h1[3]; bool over = false;
                if (dtStatusFailed(q->closestPointOnPoly(ref, mid, h0, &over)) ||
                    dtStatusFailed(q->closestPointOnPoly(link.ref, mid, h1, &over))) continue;
                float dz = std::fabs(h0[1] - h1[1]) / F;
                // Both floors measured at the same spot (within 4 units): otherwise it is a slope, not a step.
                if (dz <= stepLimit || std::hypot(h0[0] - h1[0], h0[2] - h1[2]) / F > 4) continue;
                float g0[3], g1[3]; toGame(h0, g0); toGame(h1, g1); steps++;
                int s0, s1; int dead = deadSamples(g0, g1, &s0, &s1);
                printf("{\"zone\":%d,\"kind\":\"step\",\"at\":[%.1f,%.1f,%.1f],\"other\":[%.1f,%.1f,%.1f],\"dz\":%.1f,"
                       "\"flags\":[%u,%u],\"crossTile\":%s,\"dead\":%d}\n", zone, g0[0], g0[1], g0[2], g1[0], g1[1], g1[2], dz, p.flags,
                    np->flags, nt != tile ? "true" : "false", dead);
            }
        }
    }
    printf("{\"zone\":%d,\"kind\":\"summary\",\"tiles\":%d,\"polys\":%d,\"offmesh\":%d,\"steps\":%d}\n", zone, tiles, polys, cons, steps);
    return 0;
}
