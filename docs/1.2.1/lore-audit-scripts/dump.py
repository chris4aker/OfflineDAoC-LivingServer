import json,sys,os
os.chdir(sys.argv[1])
full='--full' in sys.argv
for f in [a for a in sys.argv[2:] if not a.startswith('--')]:
    d=json.load(open(f))
    print('='*20,f,d.get('realm'),d.get('tier'))
    for x in d.get('facts',[]):
        if x.get('provenance')=='mythic' or full:
            print(' FACT[%s]: %s  <%s>'%(x['provenance'],x['text'],x.get('source','')[:90]))
    print('STORY:',d.get('story'))
    ch=d.get('chapters') or []
    print('#chapters',len(ch))
    for c in (ch if full else ch[:2]):
        print(' CH:',c.get('title'),'::',c.get('text'))
    print('LEGEND:',d.get('legend'))
    print('YELLS:',json.dumps(d.get('yells'))[:600])
    if 'summary' in d: print('SUMMARY:',d['summary'])
