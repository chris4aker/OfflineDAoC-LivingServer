"""Check the names and lines the server puts into bot chat at run time (not in the .lines files) against the corpus rules.

  python tools/bot-voices/check_runtime_names.py              (exit 1 on any problem; run by CI)
  python tools/bot-voices/check_runtime_names.py --warnings   (also list every warning, by owner)

1.2 chat audit (TEXT-028, DESIGN-045 g): the chat string literals in code (field talk, market, exchange, replies, guild,
group, combat calls, crafter talk, companions...) go through the same banned list (banned.txt). 68% of the lines in the
owner's logs came from code and none of it was checked. A "ban" hit fails; an "audit" hit fails in a file whose owner is
in chat_rules.STRICT_OWNERS and warns elsewhere (in code a "ban" entry counts as "audit": the word lists that read player
text look like said lines); an "unsure" hit warns. Log messages, literals that start with "^", "(" or "\b" (regexes) and
lines marked "// chat-check: input" (words read in player text, never said) are skipped.

The corpus builder checks every written line, but slot values chosen in code skip it: the dungeons
AutonomousBotVoices.DungeonFor names, and the words bot guild names are built from
(AutonomousBotGuildPlanner). Both are checked here with the same out-of-era and banned-word rules
(audit 2026-10-16: "the Catacombs of Cardova" and "Azure Tower" had slipped through).
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent))
from build_corpus import BANNED_RE, BANNED_ENTRIES  # noqa: E402  (same rules as the corpus)
import chat_rules  # noqa: E402

AUTONOMOUS = ROOT / 'source/server/GameServer/bots/autonomous'


def dungeon_names():
    text = (AUTONOMOUS / 'AutonomousBotVoices.cs').read_text(encoding='utf-8')
    body = text[text.index('public static string DungeonFor'):]
    body = body[:body.index('#endregion') if '#endregion' in body else len(body)]
    return re.findall(r'\("([^"]+)",\s*\d+,\s*\d+\)', body)


def guild_words():
    text = (AUTONOMOUS / 'AutonomousBotGuildPlanner.cs').read_text(encoding='utf-8')
    start = text.index('private static readonly RealmWords Albion')
    end = text.index('CasualNames', start)
    words = re.findall(r'"([^"{}]+)"', text[start:end])
    casual = text[end:text.index('];', end)]
    return words + re.findall(r'"([^"{}]+)"', casual)


GAMESERVER = ROOT / 'source/server/GameServer'
# The files whose string literals are bot chat, and who edits them (scratchpad CHAT-FIX-BRIEF.md, OWNERSHIP).
CODE_OWNERS = [
    ('E3', r'AutonomousBotVoiceCorpus|AutonomousBotVoices|AutonomousChatIdiolect'),
    ('E1', r'AutonomousChat(IntentModel|ShortTalk|Questions|Threads|ReplyFit|ReplyBudget|Context|Why)|AutonomousBotChatCoordinator'),
    ('E2', r'AutonomousChat(FieldTalk|Market|Exchange|Engagement|Knowledge|Filler|DebateSpeech|LiveAnswers)|RealmEvent(Spoken|Notices)'),
    ('E4', r'AutonomousBotStorylines|AutonomousPlayerDeeds|AutonomousChatNews|AutonomousRumours|AutonomousHolidays|AutonomousGoldSpammers|'
           r'AutonomousRoleplayers|AutonomousBeggars|AutonomousBotBonds|BotMilestones.*|AutonomousLivingServer\.Memory'),
    ('E5', r'AutonomousBotCombatCalls|AutonomousBotGroupChatter|AutonomousBotGuildSocial|AutonomousBotGuildChatLines|AutonomousBotGuilds|'
           r'AutonomousLivingServer\.Tells|CraftingDrama|CrafterTalk'),
    ('E6', r'AutonomousTales.*|LoreChat|LoreBible|LoreCodex'),
    ('E7', r'.*165.*|NoLiferBuffBots|BotGroupInvite|AutonomousLivingServer(\.Events|\.Engage)?|BotPeopleCommands|BotLedGroups|'
           r'RealmRaidPlayerInvite|AutonomousChatRegulars'),
]
LITERAL_RE = re.compile(r'\$?@?\$?"((?:[^"\\]|\\.)*)"')


def code_owner(path):
    stem = path.stem
    for owner, pattern in CODE_OWNERS:
        if re.fullmatch(pattern, stem):
            return owner
    return None


def chat_literals():
    """(where, owner, text) for each natural-language string literal in the chat files."""
    for path in sorted(GAMESERVER.rglob('*.cs')):
        owner = code_owner(path)
        if owner is None:
            continue
        for number, line in enumerate(path.read_text(encoding='utf-8-sig', errors='replace').split('\n'), 1):
            stripped = line.strip()
            # "// chat-check: input" marks a line of words the code reads in player text, not words a bot says.
            if stripped.startswith(('//', '*')) or 'Log.' in line or 'Exception(' in line or 'LOG' in line or 'chat-check: input' in line:
                continue
            for m in LITERAL_RE.finditer(line):
                text = m.group(1)
                if len(text) < 2 or text.startswith(('^', '(', '\\b', '(?')) or re.match(r'^[A-Z0-9_]{4,}\b', text):
                    continue
                if not re.search(r'[a-z]', text) or ('/' in text and '.' in text and ' ' not in text):
                    continue
                yield f'{path.relative_to(ROOT)}:{number}', owner, text


def check_code_lines(problems, show):
    warnings = {}
    count = 0
    for where, owner, text in chat_literals():
        count += 1
        said = re.sub(r'\{[^{}]*\}', ' ', text)
        for entry in BANNED_ENTRIES:
            if entry['id'] == 'url' and 'GoldSpammers' in where:
                continue
            hit = entry['regex'].search(said)
            if not hit:
                continue
            message = f'{where}: "{hit.group(0)}" ({entry["id"]}: {entry["why"]}): "{text}"'
            # In code every entry is an audit one: word lists that read player text ("mythic", "tower") look like said lines.
            if entry['level'] != 'unsure' and owner in chat_rules.STRICT_OWNERS:
                problems.append(message)
            else:
                warnings.setdefault(owner, []).append(message + (' (unsure)' if entry['level'] == 'unsure' else ''))
            break
    total = sum(len(v) for v in warnings.values())
    if total:
        print(f'{total} warning(s) in chat code literals (not failures for these owners yet): ' +
              ', '.join(f'{o} {len(v)}' for o, v in sorted(warnings.items())))
        if show:
            for owner, items in sorted(warnings.items()):
                print(f'  -- {owner} ({len(items)})')
                for item in items:
                    print('    ' + item)
    return count


def main():
    problems = []
    literals = check_code_lines(problems, '--warnings' in sys.argv)
    dungeons = dungeon_names()
    if len(dungeons) < 12:
        problems.append(f'found only {len(dungeons)} dungeon names in DungeonFor; the checker needs updating')
    for kind, names in (('dungeon', dungeons), ('guild word', guild_words())):
        for name in names:
            hit = BANNED_RE.search(name)
            if hit:
                problems.append(f'{kind} "{name}": out-of-era or banned word "{hit.group(0)}"')
    if problems:
        print(f'{len(problems)} problem(s):')
        for problem in problems:
            print('  ' + problem)
        raise SystemExit(1)
    print(f'runtime names ok: {len(dungeons)} dungeons, {len(guild_words())} guild words, {literals} chat literals in code')


if __name__ == '__main__':
    main()
