import json,glob,re,os,sys,collections
os.chdir(sys.argv[1])
B=sorted(glob.glob('bosses/*.json')); L=sorted(glob.glob('locations/*.json'))
def J(f): return json.load(open(f))
# 1 'the only'
allt=[]
for f in B+L:
    d=J(f); allt.append(d.get('story') or '')
    for c in d.get('chapters') or []: allt.append(c.get('text',''))
T='\n'.join(allt)
for p in [r'\bthe only\b',r'\bit is the only\b',r'\bfirst company\b',r'\bDame Ysolde\b',r'\bSir Emrys\b',r'\bCaer Gothwaite\b',r'\bthe Inconnu\b',r'\bthe Sylvan\b',r'\bthe druids of Lough Gur\b',r'\bthe readers of Caer Gothwaite\b',r'\bthe cast-out sculptors\b',r'\breturn(?:s|ed)? (?:his|her|the) salute\b']:
    print(p,len(re.findall(p,T)))
# 2 location section-header template
hdr=0;why=0
for f in L:
    s=J(f).get('story','')
    if re.search(r'Why (?:it|does it) matter',s): why+=1
    paras=[p for p in s.split('\n\n') if p.strip()]
    if sum(1 for p in paras if re.match(r"^[A-Z][^.?!]{2,40}[.?] ",p) and len(p.split('.')[0].split())<=6)>=2: hdr+=1
print('locations',len(L),'with Why-it-matters',why,'with short header-sentence paragraphs',hdr)
# 3 meta leakage in legend/botLines/bookPage/yells
meta=re.compile(r"Living Server|raid AI|[Uu]ntested|tests? only|in game with bots|our raid|\bbots?\b|gamebots|server",)
hits=collections.Counter(); ex=[]
for f in B+L:
    d=J(f)
    for k in ['legend','bookPage','story']:
        v=d.get(k)
        if isinstance(v,str) and meta.search(v):
            hits[k]+=1
            if len(ex)<14: m=meta.search(v); ex.append((f,k,v[max(0,m.start()-80):m.end()+60]))
print('META',hits)
for e in ex: print('  ',e)
# 4 duplicate sentences within a legend; duplicate yells
dup=[]
for f in B:
    d=J(f)
    lg=d.get('legend') or ''
    ss=[s.strip() for s in re.split(r'(?<=[.!?])\s+',lg) if len(s)>20]
    if len(ss)!=len(set(ss)): dup.append((f,'legend'))
    y=d.get('yells') or {}
    for k,v in y.items():
        if isinstance(v,list):
            vv=[x if isinstance(x,str) else x.get('text') for x in v]
            if len(vv)!=len(set(vv)): dup.append((f,'yells.'+k))
print('DUPES',len(dup),dup[:20])
# 5 provenance labelled mythic but sourced to non-Mythic sites
bad=collections.Counter(); exs=[]
tot=0
for f in B+L:
    for x in J(f).get('facts',[]):
        if x.get('provenance')=='mythic':
            tot+=1
            s=x.get('source','')
            if not re.search(r'camelotherald\.com|darkageofcamelot\.com|mythic',s,re.I):
                dom=re.findall(r'https?://([^/ ]+)',s); bad[dom[0] if dom else s[:30]]+=1
                if len(exs)<8: exs.append((f,x['text'][:140],s[:80]))
            elif 'fandom' in s:
                bad['camelotherald.fandom']+=1
print('mythic facts',tot,'non-Mythic-sourced',sum(bad.values()),bad.most_common(10))
for e in exs: print('  ',e)
# 6 how many bosses have zero mythic facts; stories length
nm=0;tier=collections.Counter();lens=[]
for f in B:
    d=J(f);
    if not any(x.get('provenance')=='mythic' for x in d.get('facts',[])): nm+=1
    lens.append(len((d.get('story') or '').split()))
print('bosses w/o any mythic fact',nm,'of',len(B))
import statistics; print('boss story words mean',statistics.mean(lens),'max',max(lens))
ch=[len(c.get('text','').split()) for f in B for c in (J(f).get('chapters') or [])]
print('chapters',len(ch),'words',sum(ch))
# 7 lair-immunity explained in lore among epic bosses
# 8 first-kill credit: named company leaders
lead=collections.Counter(re.findall(r"\b((?:Sir|Dame|Brother|Thane|Jarl|Lady|Marshal) [A-Z][a-z]+)",T))
print('top named NPC-heroes',lead.most_common(25))
