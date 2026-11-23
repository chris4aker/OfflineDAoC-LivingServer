import json,glob,re,os,sys,collections
os.chdir(sys.argv[1])
docs=[]  # (file, field, text)
for f in sorted(glob.glob('bosses/*.json'))+sorted(glob.glob('locations/*.json')):
    d=json.load(open(f))
    if d.get('story'): docs.append((f,'story',d['story']))
    for c in d.get('chapters') or []:
        docs.append((f,'chapter:'+c.get('title',''),c.get('text','')))
allt='\n'.join(t for _,_,t in docs)
words=len(re.findall(r"[A-Za-z']+",allt))
nstory=sum(1 for _,k,_ in docs if k=='story'); nch=len(docs)-nstory
print('docs',len(docs),'stories',nstory,'chapters',nch,'words',words)
sents=re.split(r'(?<=[.!?])\s+',allt)
print('sentences',len(sents))
pats={
 'not X, but Y': r"\bnot [^.;:]{1,60}?, but\b",
 ', not X (contrast tail)': r", not (?:a |an |the )?[a-z]+[^,.;]{0,30}[.;]",
 'X is not Y. It is Z / is not ... It': r"\b(?:is|was) not [^.]{1,60}\. (?:It|He|She|They) (?:is|was)\b",
 'the drovers say': r"\bthe drovers say\b",
 '<group> say (the X say)': r"\bthe [A-Z]?[a-z]+(?: [a-z]+)? say\b",
 'the Inconnu say/said': r"\bInconnu,? [^.]{0,40}?\bsa(?:y|id)\b",
 'they say': r"\bthey say\b",
 'some say': r"\bsome (?:of them )?say\b",
 'nobody knows / no one knows': r"\b(?:no ?one|nobody) (?:now living )?knows\b",
 'nobody (any)': r"\bnobody\b",
 'nobody agrees / cannot agree': r"\b(?:nobody|no one) agrees|cannot agree|never settled|has never settled|argued to this day|accounts (?:differ|divide)",
 'was not heard of again / did not come back': r"not heard of again|did not come back|never came back|was not seen again|never seen again|did not come home",
 'older than': r"\bolder than\b",
 'for a breath / long breath': r"\b(?:for a|a long|the time it takes to draw a) breath\b",
 'to this day / still': r"\bto this day\b",
 'still (any)': r"\bstill\b",
 'ever since / since': r"\bever since\b",
 'whether ... or': r"\bwhether\b",
 'em dash': r"—",
 'en dash': r"–",
 'colon': r":",
 'semicolon': r";",
 'the way a X ...s (simile)': r"\bthe way (?:a|an|the) [a-z]+",
 'as a X does / as a X (simile)': r"\bas a [a-z]+ (?:does|do|keeps|calls|spreads|runs|takes|would)\b",
 'The X say A; the Y, B (dual attribution with semicolon)': r"say[s]? [^.;]{3,80}; the [A-Z][a-z]+",
 'X matters because / Why it matters': r"matters because|Why it matters|Why does it matter",
 'and the X has never': r"\bhas never\b",
 'never once': r"\bnever once\b",
 'in his/her day / in the year': r"\bin the year\b",
 'first company / first to kill': r"\bfirst (?:company|warband|hosting|band) (?:to|against)|\bthe first to (?:break|bring|kill)",
 'returns / comes back (respawn)': r"\b(?:returns|comes back|rises again|always come back|has always come back|up again)\b",
 'cannot be hurt outside (lair immunity lore)': r"no blade|outside (?:her|his|its) (?:lair|court|bed|grove)|only there can|cannot be (?:harmed|hurt|wounded)|no blow lands|untouchable|on ground not his",
 'Living Server meta words': r"\b(?:Living Server|server|raid AI|bots?|level \d+|health)\b",
}
res={}
for k,p in pats.items():
    ms=list(re.finditer(p,allt,flags=0 if k.startswith('<') or 'Inconnu' in k else re.I))
    files=set()
    for f,_,t in docs:
        if re.search(p,t,flags=0 if k.startswith('<') or 'Inconnu' in k else re.I): files.add(f)
    ex=[allt[max(0,m.start()-60):m.end()+40].replace('\n',' ') for m in ms[:3]]
    res[k]=(len(ms),len(files),ex)
    print(f'{k}: {len(ms)} hits in {len(files)} files ({len(ms)/words*10000:.1f}/10k words)')
    for e in ex: print('    ..',e)
# groups that "say"
g=collections.Counter(m.group(0) for m in re.finditer(r"\bthe [A-Z][A-Za-z']+(?: [a-z]+)? (?:say|said|says)\b|\bthe [a-z]+ (?:of [A-Z][a-z]+ )?(?:say|said)\b",allt))
print('TOP SAYERS',g.most_common(40))
# triplets: "A, B and C" / "A, B, and C"
trip=re.findall(r"\b\w+, \w+(?: \w+)? and \w+\b",allt)
print('triplet-ish X, Y and Z:',len(trip))
# openings / closings
op=collections.Counter(); cl=collections.Counter()
for f,k,t in docs:
    s=re.split(r'(?<=[.!?])\s+',t.strip())
    op[' '.join(s[0].split()[:3])]+=1
    cl[' '.join(s[-1].split()[:3])]+=1
print('OPENINGS',op.most_common(25))
print('CLOSINGS',cl.most_common(25))
# closing sentence types
endq=collections.Counter()
for f,k,t in docs:
    last=re.split(r'(?<=[.!?])\s+',t.strip())[-1]
    for lab,p in [('still',r'\bstill\b'),('never',r'\bnever\b'),('nobody',r'\bnobody|no one\b'),('since',r'\bsince\b'),('again',r'\bagain\b'),('waits',r'\bwait')]:
        if re.search(p,last,re.I): endq[lab]+=1
print('LAST SENTENCE words (of %d texts)'%len(docs),endq)
# ngrams
toks=re.findall(r"[a-z']+",allt.lower())
for n in (4,5,6):
    c=collections.Counter(tuple(toks[i:i+n]) for i in range(len(toks)-n))
    print(f'TOP {n}-grams',[(' '.join(k),v) for k,v in c.most_common(30)])
# sentence length
L=[len(s.split()) for s in sents if s.strip()]
import statistics
print('sentence len mean',statistics.mean(L),'median',statistics.median(L),'>40 words',sum(1 for x in L if x>40))
json.dump({k:[v[0],v[1]] for k,v in res.items()},open(sys.argv[2],'w'),indent=1)
