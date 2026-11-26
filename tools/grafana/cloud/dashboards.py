#!/usr/bin/env python3
"""Generates the Grafana Cloud kit's three ✦ dashboards (issue #652): Overview, Server Health, Bots & World.

Hero tiles, gauges, bar gauges, a realm donut, a state timeline and gradient fills; built-in Grafana panels only, no
plugins, no external images, no datasource UIDs. Writes, under tools/grafana/cloud/dashboards/:
  <uid>.json          the dashboard model (Grafana's Import dialog takes it as is)
  gcx/*.json          gcx resources: the "Living Server" folder and the three dashboards in it

    python3 tools/grafana/cloud/dashboards.py           # regenerate
    python3 tools/grafana/cloud/dashboards.py --check   # fail when the files are stale (tools/check.py, CI)
    gcx resources validate -p tools/grafana/cloud/dashboards/gcx && gcx resources push -p tools/grafana/cloud/dashboards/gcx
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import dashboard_parts as base  # noqa: E402

P, SEL, PROC, NODE = base.P, base.SEL, base.PROC, base.NODE
PROM, LOKI = base.PROM, base.LOKI
STOPPED, COLLECTOR_OFF = base.STOPPED, base.COLLECTOR_OFF
tgt = base.tgt
OUT = os.path.join(HERE, "dashboards")
FOLDER_UID, FOLDER_TITLE = "living-server", "Living Server"

ALB, MID, HIB, GOLD = "#E02F44", "#3274D9", "#56A64B", "#F2CC0C"
GREY, ORANGE = "#8e8e8e", "orange"
REALMS = [("Albion", ALB), ("Midgard", MID), ("Hibernia", HIB)]

UIDS = {"overview": "living-server-overview-showcase", "health": "living-server-health-showcase",
        "bots": "living-server-bots-world-showcase"}
TITLES = {"overview": "Living Server ✦ Overview", "health": "Living Server ✦ Server Health",
          "bots": "Living Server ✦ Bots & World"}

UP = 'sum(namedprocess_namegroup_num_procs{%s})' % PROC


def gated(expr):
    """Value while CoreServer runs, -1 while stopped, no data while the collector is off."""
    return '(%s and on() (%s > 0)) or on() ((%s == 0) - 1)' % (expr, UP, UP)


STOPPED_MAP = {"type": "value", "options": {"-1": {"text": "Server stopped", "color": GREY}}}


# ------------------------------------------------------------------ panel builders
def header(key, tagline, accent):
    links = " &nbsp;·&nbsp; ".join(
        '<a href="/d/%s?${__url_time_range}&var-job=${job}" style="color:%s">%s</a>'
        % (UIDS[k], accent if k == key else "#bbbbbb", TITLES[k].split("✦ ")[1]) for k in ("overview", "health", "bots"))
    html = ('<div style="padding:6px 14px;border-left:6px solid %s;'
            'background:linear-gradient(90deg, rgba(242,204,12,0.18), rgba(50,116,217,0.10) 60%%, transparent)">'
            '<div style="font-size:30px;font-weight:700;letter-spacing:1px">%s</div>'
            '<div style="font-size:15px;opacity:0.85;margin-top:2px">%s</div>'
            '<div style="font-size:14px;margin-top:6px">%s</div></div>') % (accent, TITLES[key], tagline, links)
    return {"type": "text", "title": "", "transparent": True,
            "options": {"mode": "html", "content": html}}


def hero(title, expr, unit="short", color=GOLD, mappings=None, no_value=COLLECTOR_OFF, desc="",
         thresholds=None, graph=True, decimals=None, text_size=None):
    """Big background-gradient stat with sparkline (range query so the sparkline has data)."""
    steps = [{"color": color, "value": None}] + [{"color": c, "value": v} for v, c in (thresholds or [])]
    d = {"unit": unit, "noValue": no_value, "mappings": mappings or [],
         "thresholds": {"mode": "absolute", "steps": steps}, "color": {"mode": "thresholds"}}
    if decimals is not None:
        d["decimals"] = decimals
    opts = {"reduceOptions": {"calcs": ["lastNotNull"], "fields": "", "values": False},
            "colorMode": "background", "graphMode": "area" if graph else "none", "justifyMode": "center",
            "textMode": "value", "orientation": "auto", "wideLayout": True, "showPercentChange": False}
    if text_size:
        opts["text"] = {"valueSize": text_size}
    return {"type": "stat", "title": title, "description": desc, "datasource": PROM,
            "targets": [tgt(expr)], "fieldConfig": {"defaults": d, "overrides": []}, "options": opts}


def gauge(title, expr, unit, minv, maxv, steps, no_value=COLLECTOR_OFF, desc="", mappings=None):
    return {"type": "gauge", "title": title, "description": desc, "datasource": PROM,
            "targets": [tgt(expr)],
            "fieldConfig": {"defaults": {"unit": unit, "min": minv, "max": maxv, "noValue": no_value,
                                         "mappings": mappings or [],
                                         "thresholds": {"mode": "absolute",
                                                        "steps": [{"color": c, "value": v} for v, c in steps]},
                                         "color": {"mode": "thresholds"}}, "overrides": []},
            "options": {"reduceOptions": {"calcs": ["lastNotNull"], "fields": "", "values": False},
                        "showThresholdMarkers": True, "showThresholdLabels": False, "minVizHeight": 75}}


def bargauge(title, targets, unit="short", mode="lcd", no_value=STOPPED, steps=None, desc="", color_mode=None,
             overrides=None):
    steps = steps or [(None, "green")]
    color = {"mode": color_mode} if color_mode else {"mode": "thresholds"}
    return {"type": "bargauge", "title": title, "description": desc, "datasource": PROM, "targets": targets,
            "fieldConfig": {"defaults": {"unit": unit, "noValue": no_value, "min": 0, "color": color,
                                         "thresholds": {"mode": "absolute",
                                                        "steps": [{"color": c, "value": v} for v, c in steps]}},
                            "overrides": overrides or []},
            "options": {"orientation": "horizontal", "displayMode": mode, "showUnfilled": True,
                        "valueMode": "color", "namePlacement": "left", "sizing": "auto",
                        "reduceOptions": {"calcs": ["lastNotNull"], "fields": "", "values": False}}}


def color_override(name, color):
    return {"matcher": {"id": "byName", "options": name},
            "properties": [{"id": "color", "value": {"mode": "fixed", "fixedColor": color}}]}


def realm_overrides(total=True):
    o = [color_override(r, c) for r, c in REALMS]
    if total:
        o.append(color_override("total", GOLD))
    return o


def fancy_ts(title, targets, unit="short", desc="", stack=False, no_value=STOPPED, overrides=None,
             thresholds=None, bars=False, fill=30, palette="palette-classic"):
    custom = {"drawStyle": "bars" if bars else "line", "lineInterpolation": "smooth", "lineWidth": 2,
              "fillOpacity": fill, "gradientMode": "opacity", "showPoints": "never", "spanNulls": False,
              "stacking": {"mode": "normal" if stack else "none", "group": "A"},
              "thresholdsStyle": {"mode": "line" if thresholds else "off"}}
    steps = [{"color": "green", "value": None}] + [{"color": c, "value": v} for v, c in (thresholds or [])]
    return {"type": "timeseries", "title": title, "description": desc, "datasource": PROM, "targets": targets,
            "fieldConfig": {"defaults": {"unit": unit, "min": 0, "noValue": no_value, "custom": custom,
                                         "color": {"mode": palette},
                                         "thresholds": {"mode": "absolute", "steps": steps}},
                            "overrides": overrides or []},
            "options": {"legend": {"displayMode": "list", "placement": "bottom", "showLegend": True},
                        "tooltip": {"mode": "multi", "sort": "desc"}}}


def state_timeline():
    return {"type": "state-timeline", "title": "🟢 Server state", "datasource": PROM,
            "description": "Running / Stopped while the collector runs. Blank = collector off.",
            "targets": [tgt(UP, "CoreServer")],
            "fieldConfig": {"defaults": {"noValue": COLLECTOR_OFF, "color": {"mode": "thresholds"},
                                         "custom": {"fillOpacity": 80, "lineWidth": 0, "spanNulls": False},
                                         "thresholds": {"mode": "absolute", "steps": [{"color": GREY, "value": None}]},
                                         "mappings": [{"type": "value", "options": {"0": {"text": "Stopped", "color": GREY}}},
                                                      {"type": "range", "options": {"from": 1, "to": 1e9,
                                                                                    "result": {"text": "Running", "color": "green"}}}]},
                            "overrides": []},
            "options": {"showValue": "never", "mergeValues": True, "alignValue": "center", "rowHeight": 0.8,
                        "legend": {"showLegend": False}, "tooltip": {"mode": "single"}}}


def realm_donut():
    return {"type": "piechart", "title": "🏰 Bots by realm", "datasource": PROM,
            "targets": [tgt('max(%sbots_online_%s{%s}) and on() (%s > 0)' % (P, r.lower(), SEL, UP), r, chr(65 + i))
                        for i, (r, _) in enumerate(REALMS)],
            "fieldConfig": {"defaults": {"unit": "short", "noValue": COLLECTOR_OFF, "color": {"mode": "palette-classic"}},
                            "overrides": realm_overrides(False)},
            "options": {"pieType": "donut", "displayLabels": ["value"],
                        "legend": {"displayMode": "list", "placement": "right", "showLegend": True, "values": ["percent"]},
                        "reduceOptions": {"calcs": ["lastNotNull"], "fields": "", "values": False},
                        "tooltip": {"mode": "single"}}}


def logs(title, expr, desc=""):
    p = base.logs(title, expr, desc)
    p["options"]["showTime"] = True
    return p


SERVER_MAPS = [{"type": "value", "options": {"0": {"text": "Stopped", "color": GREY}}},
               {"type": "range", "options": {"from": 1, "to": 1e9, "result": {"text": "Running", "color": "green"}}}]

BOTS_TOTAL = gated('max(%sbots_online{%s})' % (P, SEL))
REALM_SERIES = " or ".join('label_replace(max(%sbots_online_%s{%s}), "realm", "%s", "", "")'
                           % (P, r.lower(), SEL, r) for r, _ in REALMS)
TICK_P99 = 'max(%sai_tick_p99_ms{%s})' % (P, SEL)
TICK_STEPS = [(None, "green"), (30, "yellow"), (100, "red")]


def bots_over_time(h_title="📊 Bots online over time"):
    return fancy_ts(h_title, [tgt('(%s) and on() (%s > 0)' % (REALM_SERIES, UP), "{{realm}}", "A"),
                              tgt('max(%sbots_online{%s}) and on() (%s > 0)' % (P, SEL, UP), "total", "B")],
                    overrides=realm_overrides(), no_value=COLLECTOR_OFF,
                    desc="Per realm and total. Gaps = server stopped or collector off.")


def finish(b, key):
    d = b.json()
    d["tags"] = ["living-server-showcase"] + b.tags
    d["links"] = [{"type": "dashboards", "tags": ["living-server-showcase"], "asDropdown": True,
                   "title": "Living Server", "includeVars": True, "keepTime": True}]
    return d


# ------------------------------------------------------------------ Overview
def overview():
    b = base.Board(UIDS["overview"], TITLES["overview"], ["overview"], [base.NODE_VAR])
    b.add(header("overview", "Is the world alive? Server, bots, tick and host at a glance.", GOLD), 24, 4)
    b.add(hero("Server", UP, mappings=SERVER_MAPS, color=ORANGE, graph=False,
               desc="Running / Stopped (collector on) / No data: collector off."), 4, 6)
    b.add(hero("⚔️ Bots online", BOTS_TOTAL, color=GOLD, mappings=[STOPPED_MAP], text_size=64,
               desc="Autonomous bots online (BOT_SKILL_BUDGET, every 5 min)."), 6, 6)
    b.add(hero("⏳ Uptime", '(time() - max(namedprocess_namegroup_oldest_start_time_seconds{%s})'
               ' and on() (%s > 0)) or on() ((%s == 0) - 1)' % (PROC, UP, UP),
               unit="s", color="#8F3BB8", mappings=[{"type": "value", "options": {"-1": {"text": "Stopped", "color": GREY}}}],
               graph=False, decimals=0), 4, 6)
    b.add(gauge("🧠 AI tick p99", TICK_P99, "ms", 0, 150, TICK_STEPS, no_value=STOPPED,
                desc="Green < 30 ms, yellow < 100 ms, red ≥ 100 ms."), 5, 6)
    b.add(hero("🚨 WRN+ERR / min", 'sum(rate(%slines_total{%s, level=~"WRN|ERR"}[5m])) * 60' % (P, SEL),
               color="#1F60C4", thresholds=[(20, "#FF9830"), (100, "#E02F44")], no_value=STOPPED, decimals=1), 5, 6)
    b.add(state_timeline(), 24, 3)

    b.row("🎮 Game")
    b.add(fancy_ts("⏱️ AI tick latency", [tgt('max(%sai_tick_p50_ms{%s})' % (P, SEL), "p50", "A"),
                                         tgt('max(%sai_tick_p95_ms{%s})' % (P, SEL), "p95", "B"),
                                         tgt(TICK_P99, "p99", "C")],
                   unit="ms", thresholds=[(100, "red")],
                   overrides=[color_override("p50", HIB), color_override("p95", GOLD), color_override("p99", ALB)]), 12, 8)
    b.add(bots_over_time(), 12, 8)
    b.add(fancy_ts("📜 Log lines per minute", [tgt('sum by (level) (rate(%slines_total{%s}[5m])) * 60' % (P, SEL),
                                                  "{{level}}")], stack=True,
                   overrides=[color_override("INF", MID), color_override("WRN", "#FF9830"),
                              color_override("ERR", ALB)]), 12, 8)
    b.add(fancy_ts("🔥 Game loop over budget", [tgt('max(%sserver_work_over_budget{%s, stage="GameLoopService"})'
                                                    % (P, SEL), "overBudget")], bars=True, fill=70,
                   overrides=[color_override("overBudget", "#FF9830")]), 12, 8)

    b.row("🖥️ Host & process")
    nv = "No host data"
    b.add(gauge("Host CPU", '1 - avg(rate(node_cpu_seconds_total{%s, mode="idle"}[5m]))' % NODE, "percentunit",
                0, 1, [(None, "green"), (0.7, "yellow"), (0.9, "red")], no_value=nv), 6, 6)
    b.add(gauge("Host memory", '1 - node_memory_MemAvailable_bytes{%s} / node_memory_MemTotal_bytes{%s}'
                % (NODE, NODE), "percentunit", 0, 1, [(None, "green"), (0.75, "yellow"), (0.9, "red")], no_value=nv), 6, 6)
    b.add(gauge("Disk /", '1 - node_filesystem_avail_bytes{%s, mountpoint="/"} / node_filesystem_size_bytes{%s, mountpoint="/"}'
                % (NODE, NODE), "percentunit", 0, 1, [(None, "green"), (0.8, "yellow"), (0.9, "red")], no_value=nv), 6, 6)
    b.add(hero("CoreServer memory", 'sum(namedprocess_namegroup_memory_bytes{%s, memtype="resident"})' % PROC,
               unit="bytes", color="#5794F2", no_value=STOPPED), 6, 6)
    b.add(fancy_ts("CoreServer CPU (cores)", [tgt('sum by (mode) (rate(namedprocess_namegroup_cpu_seconds_total{%s}[5m]))'
                                                  % PROC, "{{mode}}")], stack=True), 24, 7)
    return finish(b, "overview")


# ------------------------------------------------------------------ Health
def health():
    b = base.Board(UIDS["health"], TITLES["health"], ["health"])
    b.add(header("health", "Under the hood: tick budget, game services, memory and the error log.", ALB), 24, 4)
    b.add(gauge("🧠 AI tick p99", TICK_P99, "ms", 0, 150, TICK_STEPS, no_value=STOPPED), 5, 6)
    b.add(gauge("💾 Heap (live / total)", 'max(%sserver_memory_live_mb{%s}) / max(%sserver_memory_heap_mb{%s})'
                % (P, SEL, P, SEL), "percentunit", 0, 1, [(None, "green"), (0.8, "yellow"), (0.95, "red")],
                no_value=STOPPED, desc="Live managed memory as a share of the heap."), 5, 6)
    b.add(hero("Heap", 'max(%sserver_memory_heap_mb{%s})' % (P, SEL), unit="decmbytes", color="#8F3BB8",
               no_value=STOPPED), 5, 6)
    b.add(hero("Handles", 'max(%sserver_handles{%s})' % (P, SEL), color="#1F60C4", no_value=STOPPED), 3, 6)
    b.add(hero("Threads", 'max(%sserver_threads{%s})' % (P, SEL), color="#1F60C4", no_value=STOPPED), 3, 6)
    b.add(hero("🗑️ GC pauses / h", 'sum(increase(%slines_total{%s, event="SERVER_GC_PAUSE"}[1h]))' % (P, SEL),
               color=HIB, thresholds=[(10, "#FF9830"), (30, ALB)], no_value=STOPPED, decimals=0), 3, 6)

    b.row("⏱️ AI tick")
    b.add(fancy_ts("Tick latency", [tgt('max(%sai_tick_p50_ms{%s})' % (P, SEL), "p50", "A"),
                                    tgt('max(%sai_tick_p95_ms{%s})' % (P, SEL), "p95", "B"),
                                    tgt(TICK_P99, "p99", "C"),
                                    tgt('max(%sai_tick_max_ms{%s})' % (P, SEL), "max", "D")],
                   unit="ms", thresholds=[(100, "red")],
                   overrides=[color_override("p50", HIB), color_override("p95", GOLD),
                              color_override("p99", "#FF9830"), color_override("max", ALB)]), 16, 8)
    b.add(fancy_ts("Serial vs parallel", [tgt('max(%sai_serial_ms{%s})' % (P, SEL), "serial", "A"),
                                          tgt('max(%sai_parallel_ms{%s})' % (P, SEL), "parallel", "B")],
                   unit="ms", stack=True, overrides=[color_override("serial", "#8F3BB8"),
                                                     color_override("parallel", "#5794F2")]), 8, 8)

    b.row("⚙️ Game services (SERVER_WORK)")
    b.add(bargauge("Max ms per service", [tgt('sort_desc(max by (stage) (%sserver_work_max_ms{%s}))' % (P, SEL),
                                              "{{stage}}", instant=True)], unit="ms", mode="lcd",
                   steps=[(None, "green"), (20, "yellow"), (50, "red")]), 12, 11)
    b.add(bargauge("Average ms per service", [tgt('sort_desc(max by (stage) (%sserver_work_avg_ms{%s}))' % (P, SEL),
                                                  "{{stage}}", instant=True)], unit="ms", mode="gradient",
                   color_mode="continuous-BlYlRd"), 12, 11)
    b.add(fancy_ts("Over budget per service", [tgt('max by (stage) (%sserver_work_over_budget{%s}) > 0' % (P, SEL),
                                                   "{{stage}}")], bars=True, stack=True, fill=70,
                   desc="Only services that went over budget."), 24, 7)

    b.row("💾 Memory & runtime")
    b.add(fancy_ts("Managed heap", [tgt('max(%sserver_memory_heap_mb{%s})' % (P, SEL), "heap", "A"),
                                    tgt('max(%sserver_memory_live_mb{%s})' % (P, SEL), "live", "B")],
                   unit="decmbytes", overrides=[color_override("heap", "#8F3BB8"), color_override("live", "#5794F2")]), 12, 7)
    b.add(fancy_ts("GC pauses & stalls per hour",
                   [tgt('sum(increase(%slines_total{%s, event="SERVER_GC_PAUSE"}[1h]))' % (P, SEL), "GC pauses", "A"),
                    tgt('sum(increase(%slines_total{%s, event="SERVER_STALL"}[1h]))' % (P, SEL), "stalls", "B")],
                   overrides=[color_override("GC pauses", HIB), color_override("stalls", ALB)]), 12, 7)

    b.row("🚨 Warnings & errors")
    b.add(logs("WRN / ERR (names and free text blanked as ~)",
               '{job="$job", instance=~"$instance", level=~"WRN|ERR"}'), 24, 12)
    return finish(b, "health")


# ------------------------------------------------------------------ Bots & World
def bots_world():
    ev_var = {"name": "event", "label": "Event", "type": "custom",
              "query": ",".join(e for e, _ in base.KEPT_BOT_EVENTS) + ",other",
              "includeAll": True, "multi": True, "allValue": ".+",
              "current": {"text": "All", "value": ["$__all"]}, "options": [], "hide": 0}
    b = base.Board(UIDS["bots"], TITLES["bots"], ["bots", "world"], [ev_var])
    b.add(header("bots", "Three realms, one war. Who's online, who's fighting, who just died.", HIB), 24, 4)
    b.add(hero("⚔️ Bots online", BOTS_TOTAL, color=GOLD, mappings=[STOPPED_MAP], text_size=72), 6, 7)
    b.add(realm_donut(), 6, 7)
    for r, c in REALMS:
        b.add(hero(r, gated('max(%sbots_online_%s{%s})' % (P, r.lower(), SEL)), color=c,
                   mappings=[STOPPED_MAP], text_size=48), 4, 7)
    b.add(bots_over_time("📊 Bots online by realm"), 24, 8)

    b.row("🌍 World pulse")
    b.add(fancy_ts("Events per minute", [tgt('sum by (event) (rate(%slines_total{%s, event=~"$event", event!="other", level="INF"}[5m])) * 60'
                                             % (P, SEL), "{{event}}")], stack=True, fill=40), 16, 9)
    b.add(bargauge("🏆 Top events in range", [tgt('topk(10, sum by (event) (increase(%slines_total{%s, event=~"$event", event!="other"}[$__range])))'
                                                 % (P, SEL), "{{event}}", instant=True)], mode="gradient",
                   color_mode="continuous-GrYlRd"), 8, 9)

    def per_hour(ev, label, color):
        return fancy_ts(label, [tgt('sum(increase(%slines_total{%s, event="%s"}[1h]))' % (P, SEL, ev), label)],
                        bars=True, fill=80, overrides=[color_override(label, color)], desc="%s per hour" % ev)

    sections = [
        ("💀 Deaths & retreats", [("AUTONOMOUS_BOT_DEATH", "Bot deaths", ALB),
                                 ("AUTONOMOUS_DEATH_ROUTE_REPLAN", "Death route replans", "#FF9830"),
                                 ("BOT_RETREAT_ENDED", "Retreats ended", "#8F3BB8"),
                                 ("BOT_SOLO_BAIL", "Solo bails", "#5794F2")]),
        ("⚔️ RvR & battlegrounds", [("RVR_BOT_KILLED", "RvR kills", ALB), ("RVR_SIEGE", "Sieges", GOLD),
                                    ("RVR_FIGHT_BOARD", "Fight boards", MID), ("BG_WAVE_STATUS", "BG waves", HIB)]),
        ("📈 Progress & economy", [("AUTONOMOUS_XP_PROGRESS", "XP progress", HIB),
                                  ("AUTONOMOUS_EQUIPMENT_UPGRADE", "Gear upgrades", GOLD),
                                  ("AUTONOMOUS_EXCHANGE_SOLD", "Exchange sales", "#5794F2"),
                                  ("BOT_SKILL_MISTAKE", "Skill mistakes", "#FF9830"),
                                  ("CRAFT_RECIPE_BROKEN", "Broken craft recipes", ALB)]),
    ]
    for title, evs in sections:
        b.row(title)
        w = 24 // len(evs) if len(evs) <= 4 else 6  # 5 panels: 4 + 1 on the next line
        for ev, label, color in evs:
            b.add(per_hour(ev, label, color), w, 7)
    b.row("🗡️ Kill feed")
    b.add(logs("Kill feed: RvR kills and bot deaths (names blanked as ~)",
               '{job="$job", instance=~"$instance", event=~"RVR_BOT_KILLED|AUTONOMOUS_BOT_DEATH"}'), 24, 12)
    b.add(logs("Event lines (filter with the Event dropdown)",
               '{job="$job", instance=~"$instance", event=~"$event"}'), 24, 10)
    return finish(b, "bots")


def outputs():
    """Every generated file: relative path -> text."""
    files = {}
    folder = {"apiVersion": "folder.grafana.app/v1beta1", "kind": "Folder", "metadata": {"name": FOLDER_UID},
              "spec": {"title": FOLDER_TITLE, "description": "Offline DAoC Living Server monitoring (tools/grafana/cloud)"}}
    files["gcx/folder-living-server.json"] = folder
    for key, fn in [("overview", overview), ("health", health), ("bots", bots_world)]:
        d = fn()
        files[UIDS[key] + ".json"] = d
        files["gcx/dashboard-%s.json" % UIDS[key]] = {
            "apiVersion": "dashboard.grafana.app/v1beta1", "kind": "Dashboard",
            "metadata": {"name": UIDS[key], "annotations": {"grafana.app/folder": FOLDER_UID}}, "spec": d}
    return {k: json.dumps(v, indent=2, ensure_ascii=False) + "\n" for k, v in files.items()}


def main(argv):
    check = "--check" in argv
    want = outputs()
    stale = []
    for rel, text in want.items():
        path = os.path.join(OUT, rel)
        have = open(path, encoding="utf-8").read() if os.path.exists(path) else None
        if have == text:
            continue
        if check:
            stale.append(rel)
        else:
            os.makedirs(os.path.dirname(path), exist_ok=True)
            with open(path, "w", encoding="utf-8", newline="\n") as f:
                f.write(text)
            print("wrote", os.path.relpath(path))
    extra = []
    for root, _, names in os.walk(OUT):
        for n in names:
            rel = os.path.relpath(os.path.join(root, n), OUT).replace(os.sep, "/")
            if n.endswith(".json") and rel not in want:
                extra.append(rel)
    if check:
        if stale or extra:
            print("tools/grafana/cloud/dashboards is stale (run python3 tools/grafana/cloud/dashboards.py):",
                  ", ".join(stale + extra))
            return 1
        print("dashboards up to date: %d files" % len(want))
        return 0
    for rel in extra:
        os.remove(os.path.join(OUT, rel))
        print("removed", rel)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
