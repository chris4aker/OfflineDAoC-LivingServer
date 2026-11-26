#!/usr/bin/env python3
"""Lists, archives and deletes the repository's old branches (issue #645; replaces the 2026-10-18 one-off).

The owner, 2026-11-25: "clean up as much as you can on the GitHub repos". The dry run is the default: it changes
nothing and writes the list for the owner to approve. Only --mode APPLY (exactly) tags and deletes.

    python3 tools/release/branch_cleanup.py                      # dry run (the workflow's default, mode "show")
    python3 tools/release/branch_cleanup.py --mode APPLY         # tag, then delete (the owner approves this run)
    python3 tools/release/branch_cleanup.py --summary s.md --out branch-cleanup.json

Every branch gets one class (the first rule that fits, in this order):

| Class | Rule | APPLY does |
|---|---|---|
| protected | main, integration/*, the legacy release branches release/0.* (the 0.3x lines, "Keep the old releases"), the release lines release/<version>, a branch GitHub protects, a branch whose tip is a release tag (v*) | nothing |
| open-pr | an open pull request comes from it | nothing |
| recent | its newest commit is less than 14 days old | nothing |
| merged | its tip is in main | deletes it |
| replaced | not in main, and its pull requests are all closed (merged by squash or rebase, or closed and replaced) | tags archive/<branch>, then deletes it |
| never-merged | not in main and never had a pull request | tags archive/<branch>, then deletes it |

- A branch that is not in main is tagged `archive/<branch>` at its tip first, so no commit is lost. The tag is read back
  from GitHub, and only when it names the tip is the branch deleted. An existing archive tag on another commit stops that
  branch (it is reported, not deleted).
- A deletion is pushed with a lease on the tip it classified, so a branch pushed to since is left alone.
- Tags are never deleted or moved. The legacy releases' tags (v0.3-v0.37c, v0.36-se.*) stay.
- "Recent" uses the tip commit's committer date: GitHub keeps no push date per branch.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import urllib.request
from dataclasses import asdict, dataclass, field
from datetime import datetime, timedelta, timezone

RECENT_DAYS = 14
CLASSES = ["protected", "open-pr", "recent", "merged", "replaced", "never-merged"]
DELETES = {"merged", "replaced", "never-merged"}
ARCHIVES = {"replaced", "never-merged"}
RELEASE_LINE = re.compile(r"^release/\d+(?:\.\d+){1,2}$")


@dataclass
class Branch:
    name: str
    sha: str
    committed: datetime
    in_main: bool
    protected: bool = False


@dataclass
class PullRequest:
    number: int
    head: str
    state: str  # "open" or "closed"
    merged: bool = False


@dataclass
class Verdict:
    branch: str
    sha: str
    cls: str
    why: str
    prs: list[int] = field(default_factory=list)
    committed: str = ""
    action: str = ""
    result: str = ""


def protected_reason(b: Branch, release_tag_shas: set[str]) -> str:
    if b.name == "main":
        return "main"
    if b.name.startswith("integration/"):
        return "integration branch"
    if b.name.startswith("release/0."):
        return "legacy release branch (0.3x line, \"Keep the old releases\")"
    if RELEASE_LINE.match(b.name):
        return "release line"
    if b.protected:
        return "protected on GitHub"
    if b.sha in release_tag_shas:
        return "tip is a release tag"
    return ""


def classify(branches: list[Branch], prs: list[PullRequest], now: datetime,
             release_tag_shas: set[str] = frozenset()) -> list[Verdict]:
    by_head: dict[str, list[PullRequest]] = {}
    for pr in prs:
        by_head.setdefault(pr.head, []).append(pr)
    out = []
    for b in sorted(branches, key=lambda x: x.name):
        mine = sorted(by_head.get(b.name, []), key=lambda p: p.number)
        numbers = [p.number for p in mine]
        stamp = b.committed.astimezone(timezone.utc).strftime("%Y-%m-%d")
        reason = protected_reason(b, release_tag_shas)
        if reason:
            cls, why = "protected", reason
        elif any(p.state == "open" for p in mine):
            cls, why = "open-pr", "open pull request " + ", ".join(f"#{p.number}" for p in mine if p.state == "open")
        elif now - b.committed < timedelta(days=RECENT_DAYS):
            cls, why = "recent", f"newest commit {stamp}, less than {RECENT_DAYS} days ago"
        elif b.in_main:
            cls, why = "merged", "tip is in main"
        elif mine:
            merged = [p.number for p in mine if p.merged]
            why = (f"pull request {', '.join(f'#{n}' for n in merged)} merged by squash or rebase" if merged
                   else f"pull request {', '.join(f'#{n}' for n in numbers)} closed without merging")
            cls = "replaced"
        else:
            cls, why = "never-merged", "not in main, no pull request"
        v = Verdict(b.name, b.sha, cls, why, numbers, stamp)
        if cls in ARCHIVES:
            v.action = f"tag archive/{b.name}, then delete"
        elif cls in DELETES:
            v.action = "delete"
        else:
            v.action = "keep"
        out.append(v)
    return out


# ---- collecting (git and the GitHub REST API) -------------------------------------------------------------------

def git(*args: str) -> str:
    return subprocess.run(["git", *args], check=True, capture_output=True, text=True).stdout


def api_pages(path: str, token: str):
    url = f"https://api.github.com/{path}"
    while url:
        req = urllib.request.Request(url, headers={"Accept": "application/vnd.github+json",
                                                   "Authorization": f"Bearer {token}",
                                                   "X-GitHub-Api-Version": "2022-11-28"})
        with urllib.request.urlopen(req, timeout=60) as r:
            yield from json.loads(r.read().decode("utf-8"))
            m = re.search(r'<([^>]+)>;\s*rel="next"', r.headers.get("Link", ""))
            url = m.group(1) if m else ""


def collect(remote: str, repo: str, token: str, main: str = "main"):
    git("fetch", "--quiet", "--prune", remote, f"+refs/heads/*:refs/remotes/{remote}/*", "+refs/tags/*:refs/tags/*")
    protected = {b["name"] for b in api_pages(f"repos/{repo}/branches?per_page=100&protected=true", token)}
    prs = [PullRequest(p["number"], p["head"]["ref"], p["state"], bool(p.get("merged_at")))
           for p in api_pages(f"repos/{repo}/pulls?state=all&per_page=100", token)
           if (p.get("head") or {}).get("repo") and p["head"]["repo"]["full_name"].lower() == repo.lower()]
    tags = {}
    for line in git("for-each-ref", "--format=%(refname:strip=2)\t%(objectname)\t%(*objectname)", "refs/tags").splitlines():
        name, obj, peeled = line.split("\t")
        tags[name] = peeled or obj  # an annotated tag names its commit through the peeled id
    release_tag_shas = {sha for name, sha in tags.items() if re.match(r"^v\d", name)}
    main_ref = f"refs/remotes/{remote}/{main}"
    branches = []
    fmt = "%(refname:strip=3)\t%(objectname)\t%(committerdate:iso-strict)"
    for line in git("for-each-ref", f"--format={fmt}", f"refs/remotes/{remote}").splitlines():
        name, sha, date = line.split("\t")
        if name == "HEAD":
            continue
        in_main = subprocess.run(["git", "merge-base", "--is-ancestor", sha, main_ref]).returncode == 0
        branches.append(Branch(name, sha, datetime.fromisoformat(date), in_main, name in protected))
    return branches, prs, release_tag_shas, tags


# ---- applying ----------------------------------------------------------------------------------------------------

def apply(verdicts: list[Verdict], remote: str, tags: dict[str, str], run=subprocess.run) -> int:
    failures = 0
    for v in verdicts:
        if v.cls not in DELETES:
            continue
        if v.cls in ARCHIVES:
            tag = f"archive/{v.branch}"
            have = tags.get(tag)
            if have and have != v.sha:
                v.result = f"kept: {tag} already names {have[:12]}, not the tip {v.sha[:12]}"
                failures += 1
                continue
            if not have and run(["git", "push", "--quiet", remote, f"{v.sha}:refs/tags/{tag}"]).returncode != 0:
                v.result = f"kept: could not push {tag}"
                failures += 1
                continue
            seen = run(["git", "ls-remote", "--tags", remote, f"refs/tags/{tag}"], capture_output=True, text=True)
            if seen.returncode != 0 or v.sha not in (seen.stdout or ""):
                v.result = f"kept: {tag} could not be read back at {v.sha[:12]}"
                failures += 1
                continue
        lease = f"--force-with-lease=refs/heads/{v.branch}:{v.sha}"
        if run(["git", "push", "--quiet", lease, remote, f":refs/heads/{v.branch}"]).returncode != 0:
            v.result = "kept: delete refused (pushed to since, or no rights)"
            failures += 1
            continue
        v.result = "archived and deleted" if v.cls in ARCHIVES else "deleted"
    return failures


# ---- reporting ---------------------------------------------------------------------------------------------------

def summary(verdicts: list[Verdict], mode: str) -> str:
    applying = mode == "APPLY"
    counts = {c: sum(1 for v in verdicts if v.cls == c) for c in CLASSES}
    lines = [f"## Branch cleanup ({'APPLY' if applying else 'dry run: nothing was changed'})", "",
             f"{len(verdicts)} branches.", "", "| Class | Branches | APPLY does |", "|---|---|---|"]
    does = {"protected": "keeps", "open-pr": "keeps", "recent": "keeps", "merged": "deletes",
            "replaced": "tags `archive/<branch>`, then deletes", "never-merged": "tags `archive/<branch>`, then deletes"}
    for c in CLASSES:
        lines.append(f"| {c} | {counts[c]} | {does[c]} |")
    for c in CLASSES:
        rows = [v for v in verdicts if v.cls == c]
        if not rows:
            continue
        lines += ["", f"<details><summary>{c} ({len(rows)})</summary>", "", "| Branch | Tip | Newest commit | Why |"
                  + (" Result |" if applying else ""), "|---|---|---|---|" + ("---|" if applying else "")]
        for v in rows:
            lines.append(f"| `{v.branch}` | `{v.sha[:12]}` | {v.committed} | {v.why} |"
                         + (f" {v.result or 'kept'} |" if applying else ""))
        lines += ["", "</details>"]
    if not applying:
        lines += ["", "Run it again with mode `APPLY` to do it. Tags are never deleted."]
    return "\n".join(lines) + "\n"


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--mode", default="show", help="APPLY tags and deletes; anything else is a dry run")
    ap.add_argument("--remote", default="origin")
    ap.add_argument("--repo", default=os.environ.get("GITHUB_REPOSITORY", ""), help="owner/name")
    ap.add_argument("--summary", help="also write the Markdown summary here (the workflow's step summary)")
    ap.add_argument("--out", help="write every verdict here as JSON")
    a = ap.parse_args(argv)
    token = os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN", "")
    if not a.repo or not token:
        print("needs --repo (or GITHUB_REPOSITORY) and GITHUB_TOKEN", file=sys.stderr)
        return 2
    branches, prs, release_tag_shas, tags = collect(a.remote, a.repo, token)
    verdicts = classify(branches, prs, datetime.now(timezone.utc), release_tag_shas)
    failures = apply(verdicts, a.remote, tags) if a.mode == "APPLY" else 0
    text = summary(verdicts, a.mode)
    print(text)
    if a.summary:
        with open(a.summary, "a", encoding="utf-8") as f:
            f.write(text)
    if a.out:
        with open(a.out, "w", encoding="utf-8") as f:
            json.dump([asdict(v) for v in verdicts], f, indent=1)
    if failures:
        print(f"{failures} branch(es) could not be archived or deleted; see the Result column.", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
