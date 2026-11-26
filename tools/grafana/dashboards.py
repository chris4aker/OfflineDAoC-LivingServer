"""The dashboards, one per goal, as gcx resources and as Grafana file-provisioning JSON (issue #628).

Each panel names the rows and fields it reads, so kit.py --check can fail when a row does not exist (in code or in
pending-rows.json), when a field is not one of the row's, or when the privacy filter drops it. Every query is a LogQL
metric query over the shipped rows; `gcx logs metrics '<expr>'` runs the same text.
"""
from __future__ import annotations

import copy
import json
from dataclasses import dataclass, field

FOLDER_UID = "offlinedaoc-living-server"
FOLDER_TITLE = "Offline DAoC Living Server"
DATASOURCE_UID = "offlinedaoc-loki"
SEL = 'job="offlinedaoc"'


def sel(row: str) -> str:
    return "{" + SEL + ', row="' + row + '"}'


def sel_many(rows: list[str]) -> str:
    return "{" + SEL + ', row=~"' + "|".join(rows) + '"}'


@dataclass
class Panel:
    title: str
    expr: str
    rows: list[str]
    fields: list[str] = field(default_factory=list)
    legend: str = ""
    unit: str = "short"
    kind: str = "timeseries"
    description: str = ""


@dataclass
class Dashboard:
    slug: str
    title: str
    description: str
    panels: list[Panel]


def count_by(row: str, by: str, window: str = "$__auto", filters: str = "") -> str:
    return f"sum by ({by}) (count_over_time({sel(row)} | logfmt{filters} [{window}]))"


def quantile(q: float, row: str, value: str, by: str, filters: str = "", window: str = "$__auto") -> str:
    return (f"quantile_over_time({q}, {sel(row)} | logfmt{filters} | unwrap {value} | __error__=\"\" [{window}]) "
            f"by ({by})")


RECOVERY_ROWS = ["AUTONOMOUS_ROUTE_RECOVERY", "AUTONOMOUS_LOCAL_SURFACE_RECOVERY", "AUTONOMOUS_RAID_STUCK_RECOVERY",
                 "AUTONOMOUS_ROUTE_POCKET_ESCAPE", "AUTONOMOUS_ZONE_SEAM_CYCLE", "AUTONOMOUS_SERVICE_SURFACE_RECOVERY",
                 "AUTONOMOUS_CAPITAL_EGRESS_RECOVERY", "AUTONOMOUS_CROSSING_SOURCE_RECOVERY"]
CENSUS_ROWS = ["SERVER_NPC_CENSUS", "SERVER_HANDLE_CENSUS", "SERVER_STATIC_CENSUS", "SERVER_HOLDER_CENSUS",
               "SERVER_BOT_FIELD_CENSUS"]

DASHBOARDS: list[Dashboard] = [
    Dashboard("battlegrounds", "Battlegrounds", "BG_POPULATION, BG_TARGET_PICK, BG_RALLY_RESPONSE (#625, #610, #621).", [
        Panel("Bots per battleground (largest 10-min count, all realms)",
              f"sum by (bg) (max_over_time({sel('BG_POPULATION')} | logfmt | unwrap bots | __error__=\"\" [10m]) by (bg, realm))",
              ["BG_POPULATION"], ["bg", "bots"], "{{bg}}"),
        Panel("Expected headcount per battleground",
              f"sum by (bg) (max_over_time({sel('BG_POPULATION')} | logfmt | unwrap expected | __error__=\"\" [10m]) by (bg, realm))",
              ["BG_POPULATION"], ["bg", "expected"], "{{bg}}"),
        Panel("Target picks by kind (sampled)", count_by("BG_TARGET_PICK", "target"), ["BG_TARGET_PICK"], ["target"],
              "{{target}}"),
        Panel("Rally picks vs scans", count_by("BG_TARGET_PICK", "rally"), ["BG_TARGET_PICK"], ["rally"], "rally={{rally}}"),
        Panel("Rally response, median ms", quantile(0.5, "BG_RALLY_RESPONSE", "ms", "bg"), ["BG_RALLY_RESPONSE"],
              ["ms", "bg"], "{{bg}}", "ms"),
        Panel("Rally response, 90th percentile ms", quantile(0.9, "BG_RALLY_RESPONSE", "ms", "bg"), ["BG_RALLY_RESPONSE"],
              ["ms", "bg"], "{{bg}}", "ms"),
    ]),
    Dashboard("combat", "Combat", "CAST_INTERRUPT and CC_BREAK (#625, #596, #621).", [
        Panel("Interrupted casts: kept or lost, by caster kind", count_by("CAST_INTERRUPT", "caster, outcome"),
              ["CAST_INTERRUPT"], ["caster", "outcome"], "{{caster}} {{outcome}}"),
        Panel("Interrupt timer left at half-cast, median ms", quantile(0.5, "CAST_INTERRUPT", "leftMs", "caster"),
              ["CAST_INTERRUPT"], ["leftMs", "caster"], "{{caster}}", "ms"),
        Panel("CC breaks by kind and side", count_by("CC_BREAK", "cc, side"), ["CC_BREAK"], ["cc", "side"],
              "{{cc}} {{side}}"),
        Panel("CC held before the break, median ms", quantile(0.5, "CC_BREAK", "ms", "cc"), ["CC_BREAK"], ["ms", "cc"],
              "{{cc}}", "ms"),
    ]),
    Dashboard("chat", "Chat", "PLAYER_LINE_OUTCOME and CHAT_LINE (#625).", [
        Panel("Player lines answered within 30 s, by channel (share)",
              f"sum by (ch) (count_over_time({sel('PLAYER_LINE_OUTCOME')} | logfmt | answered=\"1\" [1h])) / "
              f"sum by (ch) (count_over_time({sel('PLAYER_LINE_OUTCOME')} | logfmt [1h]))",
              ["PLAYER_LINE_OUTCOME"], ["ch", "answered"], "{{ch}}", "percentunit"),
        Panel("Answer latency, median ms", quantile(0.5, "PLAYER_LINE_OUTCOME", "latencyMs", "ch", ' | answered="1"'),
              ["PLAYER_LINE_OUTCOME"], ["latencyMs", "ch", "answered"], "{{ch}}", "ms"),
        Panel("Reply kinds", count_by("PLAYER_LINE_OUTCOME", "kind"), ["PLAYER_LINE_OUTCOME"], ["kind"], "{{kind}}"),
        Panel("Chat lines by speaker kind and channel", count_by("CHAT_LINE", "who, ch"), ["CHAT_LINE"], ["who", "ch"],
              "{{who}} {{ch}}"),
    ]),
    Dashboard("levelling", "Levelling", "AUTONOMOUS_KILL_XP, AUTONOMOUS_GOAL_ATTEMPT, REALM_LEVEL_SPREAD (#544, #625).", [
        Panel("Kill XP per member, median, by realm", quantile(0.5, "AUTONOMOUS_KILL_XP", "xpPerMember", "realm"),
              ["AUTONOMOUS_KILL_XP"], ["xpPerMember"], "{{realm}}"),
        Panel("Sampled kills by con", count_by("AUTONOMOUS_KILL_XP", "con"), ["AUTONOMOUS_KILL_XP"], ["con"], "{{con}}"),
        Panel("Goal attempts finished, by outcome",
              f"sum by (outcome) (count_over_time({sel('AUTONOMOUS_GOAL_ATTEMPT')} | regexp `^\\S+ (?P<j>.*)$` "
              f"| line_format `{{{{.j}}}}` | json outcome=\"outcome\" [$__auto]))",
              ["AUTONOMOUS_GOAL_ATTEMPT"], [], "{{outcome}}",
              description="The row is JSON after its marker; the query strips the marker and reads it with | json."),
        Panel("REALM_LEVEL_SPREAD rows per realm", f"sum by (realm) (count_over_time({sel('REALM_LEVEL_SPREAD')} [1h]))",
              ["REALM_LEVEL_SPREAD"], [], "{{realm}}"),
    ]),
    Dashboard("groups", "Groups", "GROUP_ENDED (#625, #544).", [
        Panel("Gamebot groups ended per realm", f"sum by (realm) (count_over_time({sel('GROUP_ENDED')} [1h]))",
              ["GROUP_ENDED"], [], "{{realm}}"),
        Panel("Groups formed (AUTONOMOUS_GROUP_FORMED) per realm",
              f"sum by (realm) (count_over_time({sel('AUTONOMOUS_GROUP_FORMED')} [1h]))", ["AUTONOMOUS_GROUP_FORMED"], [],
              "{{realm}}"),
    ]),
    Dashboard("economy", "Economy", "ECONOMY_PULSE and AUTONOMOUS_EXCHANGE_SOLD (#625).", [
        Panel("Realm Exchange sales per realm", f"sum by (realm) (count_over_time({sel('AUTONOMOUS_EXCHANGE_SOLD')} [1h]))",
              ["AUTONOMOUS_EXCHANGE_SOLD"], [], "{{realm}}"),
        Panel("Realm Exchange sale price, median copper", quantile(0.5, "AUTONOMOUS_EXCHANGE_SOLD", "price", "realm",
                                                                    window="1h"),
              ["AUTONOMOUS_EXCHANGE_SOLD"], ["price"], "{{realm}}"),
        Panel("ECONOMY_PULSE rows per realm", f"sum by (realm) (count_over_time({sel('ECONOMY_PULSE')} [1h]))",
              ["ECONOMY_PULSE"], [], "{{realm}}"),
    ]),
    Dashboard("movement", "Movement", "STUCK_HOTSPOT (#625) and the recovery rows.", [
        Panel("Recoveries by kind", f"sum by (row) (count_over_time({sel_many(RECOVERY_ROWS)} [$__auto]))",
              RECOVERY_ROWS, [], "{{row}}"),
        Panel("Recoveries by region (route recovery)", count_by("AUTONOMOUS_ROUTE_RECOVERY", "region"),
              ["AUTONOMOUS_ROUTE_RECOVERY"], ["region"], "region {{region}}"),
        Panel("STUCK_HOTSPOT rows", f"sum(count_over_time({sel('STUCK_HOTSPOT')} [1h]))", ["STUCK_HOTSPOT"], [], "rows"),
    ]),
    Dashboard("rvr", "RvR", "RVR_DECIDE, RVR_SIEGE_OUTPUT, RVR_KEEP_TAKEN.", [
        Panel("RvR decisions by layer", count_by("RVR_DECIDE", "layer"), ["RVR_DECIDE"], ["layer"], "{{layer}}"),
        Panel("RvR decisions by realm and what", count_by("RVR_DECIDE", "realm, what"), ["RVR_DECIDE"], ["what"],
              "{{realm}} {{what}}"),
        Panel("Siege damage by engine kind", f"sum by (kind) (sum_over_time({sel('RVR_SIEGE_OUTPUT')} | logfmt | unwrap damage | __error__=\"\" [$__auto]))",
              ["RVR_SIEGE_OUTPUT"], ["kind", "damage"], "{{kind}}"),
        Panel("Keeps taken by realm", f"sum by (realm) (count_over_time({sel('RVR_KEEP_TAKEN')} [1h]))",
              ["RVR_KEEP_TAKEN"], [], "{{realm}}"),
    ]),
    Dashboard("server-health", "Server health", "SERVER_STALL, SERVER_HEAP_SNAPSHOT and the censuses.", [
        Panel("Worst tick per minute, ms", f"max(max_over_time({sel('SERVER_STALL')} | logfmt | unwrap worstTickMs | __error__=\"\" [$__auto]))",
              ["SERVER_STALL"], ["worstTickMs"], "worst tick", "ms"),
        Panel("GC pause per stall minute, ms", f"max(max_over_time({sel('SERVER_STALL')} | logfmt | unwrap gcPauseMs | __error__=\"\" [$__auto]))",
              ["SERVER_STALL"], ["gcPauseMs"], "GC pause", "ms"),
        Panel("Heap at stalls, MB", f"max(max_over_time({sel('SERVER_STALL')} | logfmt | unwrap heapMB | __error__=\"\" [$__auto]))",
              ["SERVER_STALL"], ["heapMB"], "heap", "decmbytes"),
        Panel("Heap snapshots, MB", f"max(max_over_time({sel('SERVER_HEAP_SNAPSHOT')} | logfmt | unwrap heapMB | __error__=\"\" [$__auto]))",
              ["SERVER_HEAP_SNAPSHOT"], ["heapMB"], "heap", "decmbytes"),
        Panel("Census rows", f"sum by (row) (count_over_time({sel_many(CENSUS_ROWS)} [$__auto]))", CENSUS_ROWS, [],
              "{{row}}"),
        Panel("Server starts (FORK_BUILD)", f"sum by (build) (count_over_time({sel('FORK_BUILD')} [$__auto]))",
              ["FORK_BUILD"], [], "{{build}}"),
    ]),
]


def dashboard_model(d: Dashboard) -> dict:
    """The classic dashboard JSON (what file provisioning reads, and a v1 Dashboard's spec)."""
    panels = []
    for i, p in enumerate(d.panels):
        panels.append({
            "id": i + 1,
            "type": p.kind,
            "title": p.title,
            "description": p.description or f"Rows: {', '.join(p.rows)}.",
            "datasource": {"type": "loki", "uid": "${datasource}"},
            "gridPos": {"h": 8, "w": 12, "x": (i % 2) * 12, "y": (i // 2) * 8},
            "fieldConfig": {"defaults": {"unit": p.unit}, "overrides": []},
            "options": {"legend": {"displayMode": "list", "placement": "bottom", "showLegend": True},
                        "tooltip": {"mode": "multi", "sort": "desc"}},
            "targets": [{"refId": "A", "datasource": {"type": "loki", "uid": "${datasource}"}, "expr": p.expr,
                         "legendFormat": p.legend, "queryType": "range", "editorMode": "code"}],
        })
    return {
        "uid": f"offlinedaoc-{d.slug}",
        "title": f"Living Server: {d.title}",
        "description": d.description + " Generated by tools/grafana/kit.py; edit dashboards.py, not this file.",
        "tags": ["offlinedaoc", "living-server"],
        "editable": True,
        "schemaVersion": 41,
        "time": {"from": "now-24h", "to": "now"},
        "refresh": "1m",
        "timezone": "browser",
        "templating": {"list": [{
            "name": "datasource", "label": "Loki", "type": "datasource", "query": "loki",
            "current": {"text": "Offline DAoC Loki", "value": DATASOURCE_UID}, "hide": 0, "refresh": 1,
        }]},
        "annotations": {"list": []},
        "panels": panels,
    }


def gcx_dashboard(d: Dashboard) -> dict:
    model = dashboard_model(d)
    spec = copy.deepcopy(model)
    spec.pop("uid")
    return {
        "apiVersion": "dashboard.grafana.app/v1beta1",
        "kind": "Dashboard",
        "metadata": {"name": model["uid"], "annotations": {"grafana.app/folder": FOLDER_UID}},
        "spec": spec,
    }


def gcx_folder() -> str:
    return ("apiVersion: folder.grafana.app/v1beta1\nkind: Folder\nmetadata:\n  name: " + FOLDER_UID +
            "\nspec:\n  title: " + FOLDER_TITLE + "\n")


def outputs() -> dict[str, str]:
    """Relative path (under tools/grafana) -> file text."""
    out = {"resources/folder.yaml": gcx_folder()}
    for d in DASHBOARDS:
        out[f"resources/dashboards/{d.slug}.json"] = json.dumps(gcx_dashboard(d), indent=2) + "\n"
        out[f"provisioning/dashboards/{d.slug}.json"] = json.dumps(dashboard_model(d), indent=2) + "\n"
    return out
