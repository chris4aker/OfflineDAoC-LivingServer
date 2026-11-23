import json,glob,random,os,re,sys
os.chdir(sys.argv[1])
random.seed(1119)
B=sorted(glob.glob('bosses/*.json')); Lc=sorted(glob.glob('locations/*.json')); H=sorted(glob.glob('history/*.json'))
forced=['bosses/golestandt.json','bosses/gjalpinulva.json','bosses/cuuldurach-the-glimmer-king.json','bosses/legion.json']
realm={f:json.load(open(f)).get('realm') for f in B}
pool=[f for f in B if f not in forced]
pick=[]
for r in ['Albion','Midgard','Hibernia']:
    pick+=random.sample([f for f in pool if realm[f]==r],3)
rest=[f for f in pool if f not in pick]
pick+=random.sample(rest,5)
bosses=forced+pick
locs=random.sample(Lc,8); hist=random.sample(H,2)
tl=[]
for fn in ['../chat/tales.lines','../chat/tales_more.lines']:
    for line in open(fn):
        m=re.match(r'@scene (\S+)',line)
        if m: tl.append((fn,m.group(1)))
tales=random.sample(tl,6)
for f in bosses: print(f,realm[f])
print(locs);print(hist);print(tales);print(len(tl))
