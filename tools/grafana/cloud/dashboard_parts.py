#!/usr/bin/env python3
"""Panel builders and shared queries for the Grafana Cloud kit's dashboards (issue #652); dashboards.py uses them.

No datasource UIDs are hard-coded: every panel uses the ${ds_prom} / ${ds_loki} template variables,
so the JSON imports into any Grafana stack. Metric names match tools/grafana/cloud/alloy:
  loki_process_custom_*            log-derived metrics (labels: event, level, stage for SERVER_WORK)
  namedprocess_namegroup_*         process exporter, groupname="coreserver"
  node_*                           host metrics from a node_exporter already on the PC (job integrations/node_exporter; #657)
"""
P = "loki_process_custom_"
SEL = 'job="$job", instance=~"$instance"'
PROC = SEL + ', groupname="coreserver"'
NODE = 'job="integrations/node_exporter", instance=~"$node_instance"'

# The game server only runs while someone plays: missing game data is normal, not an error.
STOPPED = "Server stopped"
COLLECTOR_OFF = "No data: collector off"

# Hide Grafana Cloud's internal usage/insights/billing datasources so the stack's own Prometheus and Loki
# are picked by default.
DS_REGEX = "/^(?!.*(usage|insights|alert-state|billing|cardinality)).*$/"

PROM = {"type": "prometheus", "uid": "${ds_prom}"}
LOKI = {"type": "loki", "uid": "${ds_loki}"}

KEPT_BOT_EVENTS = [
    ("AUTONOMOUS_BOT_DEATH", "Bot deaths"),
    ("AUTONOMOUS_DEATH_ROUTE_REPLAN", "Death route replans"),
    ("BOT_SOLO_BAIL", "Solo bails"),
    ("BOT_RETREAT_ENDED", "Retreats ended"),
    ("BOT_SKILL_MISTAKE", "Skill mistakes"),
    ("AUTONOMOUS_XP_PROGRESS", "XP progress"),
    ("AUTONOMOUS_EQUIPMENT_UPGRADE", "Equipment upgrades"),
    ("AUTONOMOUS_EXCHANGE_SOLD", "Exchange sales"),
    ("RVR_BOT_KILLED", "RvR bot kills"),
    ("RVR_SIEGE", "RvR sieges"),
    ("RVR_FIGHT_BOARD", "RvR fight boards"),
    ("BG_WAVE_STATUS", "Battleground waves"),
    ("CRAFT_RECIPE_BROKEN", "Broken craft recipes"),
]


class Board:
    def __init__(self, uid, title, tags, extra_vars=()):
        self.uid, self.title, self.tags = uid, title, tags
        self.panels, self.y, self.x, self.row_h = [], 0, 0, 0
        self.next_id = 1
        self.extra_vars = list(extra_vars)

    def row(self, title):
        self._newline()
        self.panels.append({"type": "row", "title": title, "id": self._id(), "collapsed": False,
                            "gridPos": {"x": 0, "y": self.y, "w": 24, "h": 1}, "panels": []})
        self.y += 1

    def add(self, panel, w, h):
        if self.x + w > 24:
            self._newline()
        panel["id"] = self._id()
        panel["gridPos"] = {"x": self.x, "y": self.y, "w": w, "h": h}
        self.panels.append(panel)
        self.x += w
        self.row_h = max(self.row_h, h)

    def _newline(self):
        if self.x:
            self.y += self.row_h
        self.x, self.row_h = 0, 0

    def _id(self):
        self.next_id += 1
        return self.next_id - 1

    def json(self):
        tvars = [
            {"name": "ds_prom", "label": "Prometheus", "type": "datasource", "query": "prometheus",
             "current": {}, "hide": 0, "refresh": 1, "regex": DS_REGEX},
            {"name": "ds_loki", "label": "Loki", "type": "datasource", "query": "loki",
             "current": {}, "hide": 0, "refresh": 1, "regex": DS_REGEX},
            {"name": "job", "label": "Job", "type": "custom", "query": "livingserver,livingserver-test",
             "current": {"text": "livingserver", "value": "livingserver"},
             "options": [], "hide": 0},
            {"name": "instance", "label": "Server", "type": "query", "datasource": PROM,
             "query": {"query": 'label_values(%slines_total{job="$job"}, instance)' % P, "refId": "v"},
             "definition": 'label_values(%slines_total{job="$job"}, instance)' % P,
             "refresh": 2, "includeAll": True, "multi": False, "allValue": ".*",
             "current": {"text": "All", "value": "$__all"}, "hide": 0, "sort": 1},
        ] + self.extra_vars
        return {
            "uid": self.uid, "title": self.title, "tags": tags_all(self.tags), "editable": True,
            "schemaVersion": 39, "version": 1, "timezone": "browser", "graphTooltip": 1,
            "time": {"from": "now-6h", "to": "now"}, "refresh": "1m",
            "templating": {"list": tvars}, "annotations": {"list": []},
            "links": [{"type": "dashboards", "tags": ["living-server"], "asDropdown": True,
                       "title": "Living Server", "includeVars": True, "keepTime": True}],
            "panels": self.panels,
        }


def tags_all(tags):
    return ["living-server"] + tags


def tgt(expr, legend="", ref="A", instant=False):
    t = {"datasource": PROM, "expr": expr, "legendFormat": legend, "refId": ref}
    if instant:
        t.update({"instant": True, "range": False})
    return t


def ts(title, targets, unit="short", desc="", thresholds=None, stack=False, minv=None, bars=False,
       no_value=None):
    custom = {"drawStyle": "bars" if bars else "line", "lineWidth": 1, "fillOpacity": 60 if bars else 10,
              "showPoints": "never", "spanNulls": False,
              "stacking": {"mode": "normal" if stack else "none", "group": "A"}}
    steps = [{"color": "green", "value": None}]
    if thresholds:
        custom["thresholdsStyle"] = {"mode": "line+area" if len(thresholds) > 1 else "line"}
        steps += [{"color": c, "value": v} for v, c in thresholds]
    else:
        custom["thresholdsStyle"] = {"mode": "off"}
    defaults = {"unit": unit, "custom": custom, "color": {"mode": "palette-classic"},
                "thresholds": {"mode": "absolute", "steps": steps}}
    if minv is not None:
        defaults["min"] = minv
    defaults["noValue"] = no_value or STOPPED
    return {"type": "timeseries", "title": title, "description": desc, "datasource": PROM,
            "targets": targets, "fieldConfig": {"defaults": defaults, "overrides": []},
            "options": {"legend": {"displayMode": "list", "placement": "bottom", "showLegend": True},
                        "tooltip": {"mode": "multi", "sort": "desc"}}}


def stat(title, expr, unit="short", desc="", thresholds=None, mappings=None, color_mode="value", reduce="lastNotNull",
         no_value=None, base_color="text"):
    # Neutral base colour by default: "no data" (server stopped) must never look like an alert.
    steps = [{"color": base_color, "value": None}] + [{"color": c, "value": v} for v, c in (thresholds or [])]
    return {"type": "stat", "title": title, "description": desc, "datasource": PROM,
            "targets": [tgt(expr, instant=(reduce == "lastNotNull"))],
            "fieldConfig": {"defaults": {"unit": unit, "mappings": mappings or [], "noValue": no_value or STOPPED,
                                         "thresholds": {"mode": "absolute", "steps": steps},
                                         "color": {"mode": "thresholds"}}, "overrides": []},
            "options": {"reduceOptions": {"calcs": [reduce], "fields": "", "values": False},
                        "colorMode": color_mode, "graphMode": "area", "justifyMode": "auto",
                        "textMode": "auto", "orientation": "auto"}}


def logs(title, expr, desc=""):
    return {"type": "logs", "title": title, "description": desc, "datasource": LOKI,
            "targets": [{"datasource": LOKI, "expr": expr, "refId": "A", "queryType": "range"}],
            "options": {"showTime": True, "wrapLogMessage": True, "sortOrder": "Descending",
                        "enableLogDetails": True, "dedupStrategy": "none", "prettifyLogMessage": False}}


def text(content, title=""):
    return {"type": "text", "title": title, "options": {"mode": "markdown", "content": content}}


NODE_VAR = {"name": "node_instance", "label": "Host", "type": "query", "datasource": PROM,
            "query": {"query": 'label_values(node_uname_info{job="integrations/node_exporter"}, instance)',
                      "refId": "n"},
            "definition": 'label_values(node_uname_info{job="integrations/node_exporter"}, instance)',
            "refresh": 2, "includeAll": False, "multi": False, "current": {}, "hide": 0, "sort": 1}
