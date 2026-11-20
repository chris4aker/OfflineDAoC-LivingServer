"""Shared rules for the lore files: what counts as a developer note and a duplicated line."""
import json, re, glob, os

LORE_DIR = os.path.join(os.path.dirname(__file__), "..", "..", "source", "server", "GameServer", "bots", "autonomous", "data", "lore")

# Sentences that talk about our testing or our code, not about the world. Players never see these.
DEV_NOTE = re.compile(r"\b[Uu]ntested\b|\btests? only\b|\bin game with bots\b|\braid AI\b|\bno raid tactics\b|\bcontroller\b|\bour raid AI\b")

def sentences(text):
    return [s for s in re.split(r"(?<=[.!?])\s+", text.strip()) if s]

def lore_files(root=LORE_DIR):
    return sorted(glob.glob(os.path.join(root, "*", "*.json")))

def pool_entries(value):
    """Yell and bot-line pools: a list of strings or of {text: ...} objects."""
    return value if isinstance(value, list) else []

def entry_text(entry):
    return entry if isinstance(entry, str) else (entry.get("text") if isinstance(entry, dict) else None)

# A fact may be labelled "mythic" only when its source is Mythic's own words: a Camelot Herald page,
# darkageofcamelot.com, or a Mythic patch note. Allakhazam, Prima, GameBanshee and player posts are "legend".
MYTHIC_SOURCE = re.compile(r"camelotherald|darkageofcamelot|\bherald\b|\bmythic\b", re.I)

def all_facts(d):
    """Every fact dict ({text, provenance, source}) anywhere in a lore file."""
    out = []
    def walk(v):
        if isinstance(v, dict):
            if "provenance" in v: out.append(v)
            for x in v.values(): walk(x)
        elif isinstance(v, list):
            for x in v: walk(x)
    walk(d)
    return out

def write_json(path, d, raw):
    indent = 4 if raw.startswith('{\n    "') else 2
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(json.dumps(d, indent=indent, ensure_ascii=False) + ("\n" if raw.endswith("\n") else ""))
