"""A tiny stand-in for web.archive.org, used only by run-tests.ps1 (end-to-end test).

CDX: the first query gets 429 + Retry-After: 2, then two pages joined by a resumeKey.
Captures (id_ URLs): one 503 before success, one 404, one always 500, one same-page redirect, one redirect to
another site.
Every request is logged as a JSON line: arrival time, path, query, User-Agent.
"""
import argparse
import json
import time
from http.server import BaseHTTPRequestHandler, HTTPServer
from urllib.parse import urlsplit

HEADER = ["urlkey", "timestamp", "original", "mimetype", "statuscode", "digest", "length"]
H = "http://www.camelotherald.com/guilds/"


def row(orig, ts):
    return ["com,camelotherald)/guilds/" + orig.rsplit("/", 1)[-1], ts, orig, "text/html", "200", "D", "100"]


PAGE1 = [HEADER, row(H + "a.php", "20030101000000"), row("http://www.example.com/guilds/x", "20030101000000"),
         row(H + "old.php", "19990101000000"), [], ["com,camelotherald)/guilds/b.php 20030202000000"]]
PAGE2 = [HEADER, row(H + "b.php", "20030202000000"), row(H + "gone.php", "20030505000000"),
         row(H + "moved.php", "20030404000000"), row(H + "jump.php", "20030606000000"), row(H + "broken.php", "20030707000000"),
         row(H + "c.php", "20030707000000")]

state = {"cdx": 0, "b": 0}


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def send(self, code, body=b"", headers=None):
        self.send_response(code)
        for k, v in (headers or {}).items():
            self.send_header(k, v)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        parts = urlsplit(self.path)
        with open(self.server.log_path, "a", encoding="utf-8") as f:
            f.write(json.dumps({"t": time.time(), "path": parts.path, "query": parts.query,
                                "ua": self.headers.get("User-Agent", "")}) + "\n")
        if parts.path == "/cdx/search/cdx":
            state["cdx"] += 1
            if state["cdx"] == 1:
                return self.send(429, b"slow down", {"Retry-After": "2"})
            page = PAGE2 if "resumeKey=" in parts.query else PAGE1
            return self.send(200, json.dumps(page).encode(), {"Content-Type": "application/json"})
        if parts.path.startswith("/web/"):
            target = self.path[len("/web/"):]
            ts, _, orig = target.partition("id_/")
            if orig.endswith("b.php"):
                state["b"] += 1
                if state["b"] == 1:
                    return self.send(503, b"busy")
            if orig.endswith("broken.php"):
                return self.send(500, b"always broken")
            if orig.endswith("gone.php"):
                return self.send(404, b"not archived")
            if orig.endswith("moved.php") and ts == "20030404000000":
                return self.send(302, b"", {"Location": "/web/20030405000000id_/" + orig})
            if orig.endswith("jump.php"):
                return self.send(302, b"", {"Location": "/web/20030606000000id_/http://www.example.org/"})
            body = ("<html><body>%s at %s</body></html>" % (orig, ts)).encode()
            return self.send(200, body, {"Content-Type": "text/html"})
        return self.send(404, b"no")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port-file", required=True)
    ap.add_argument("--log", required=True)
    a = ap.parse_args()
    srv = HTTPServer(("127.0.0.1", 0), Handler)
    srv.log_path = a.log
    with open(a.port_file + ".tmp", "w") as f:
        f.write(str(srv.server_address[1]))
    import os
    os.replace(a.port_file + ".tmp", a.port_file)
    srv.serve_forever()


if __name__ == "__main__":
    main()
