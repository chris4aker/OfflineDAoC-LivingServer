import json,collections,sys
rows=json.load(open('scored.json'))
OV={}
def s(cat,ids):
    for i in ids.split(): OV['f'+i]=cat
s('GOOD','002 043 044 049 174 177 179 242 244 222 385 394 397 402 403 408 423 503 507 528 325')
s('NON','046 073 171 346 347 371 419 458 460 464 465 467 469 582 594 135 136 137 425 515 618 154 188 449 453')
s('WRONG','051 052 104 578 109 579 125 128 295 162 035 355 615')
s('OFF','080 115 118 126 334 429 438 452 473 621 181 105 190 031 566 025 067 571 083 088 094 576 096 142 172 198 204 211 262 219 264 274 332 291 316 328 366 410 455 456 472 481 484 496 516 524 525 574 147 585')
s('STUB','110 111 191 192 286 287 288 318 319 607 612 113 411 415')
s('NON','462')
MAP={'ANSWERED':'GOOD','WRONG':'WRONG','NON_ANSWER':'NON','OFF_TOPIC':'OFF','FLUFF':'NON','STUB':'STUB'}
for r in rows:
    if r['id'] in OV: j=OV[r['id']]
    elif r['cat']=='SILENT': j='GOOD' if r['sil'] else 'SILENT'
    else: j=MAP[r['cat']]
    r['j']=j
json.dump(rows,open('judged.json','w'),indent=1,ensure_ascii=False)
C=['GOOD','WRONG','NON','SILENT','OFF','STUB']
def table(key,title,f=None,order=None):
    g=collections.defaultdict(list)
    for r in rows:
        if f and not f(r): continue
        g[key(r)].append(r)
    print(f'\n### {title}\n\n| group | n | good | good % | wrong | non-answer | silent | off-topic | stub |\n|---|---:|---:|---:|---:|---:|---:|---:|---:|')
    keys=order or sorted(g,key=lambda k:-len(g[k]))
    for k in keys:
        v=g[k]; c=collections.Counter(x['j'] for x in v)
        print(f"| {k} | {len(v)} | {c['GOOD']} | {100*c['GOOD']/len(v):.0f}% | {c['WRONG']} | {c['NON']} | {c['SILENT']} | {c['OFF']} | {c['STUB']} |")
table(lambda r:'all','Total')
table(lambda r:r['ch'],'By channel')
table(lambda r:r['kind'],'By kind')
table(lambda r:r['bracket'],'By level bracket',order=['1','5','10','20','35','50'])
table(lambda r:r['realm'],'By realm')
table(lambda r:r['topic'],'By topic')
table(lambda r:'gap' if r['gap'] else 'data has it','Data gap or not (questions only)',f=lambda r:r['kind']=='question')
# variants
qs={q['id']:q for q in json.load(open('/home/claude/a2-questions/tools/bot-voices/eval/audit_fresh_questions.json'))['questions']}
by={ (r['text'],r['realm']):r for r in rows if r['ch']=='faction'}
print('\n### Same question, another channel\n')
tr=collections.Counter()
for r in rows:
    v=qs[r['id']].get('variant_of')
    if v:
        b=by[(v,r['realm'])]; tr[(b['j'],r['ch'],r['j'])]+=1
for k,n in sorted(tr.items()): print(k,n)
print(collections.Counter(r['route'].split('(')[0].strip() if r['j'] in('SILENT',) else None for r in rows).most_common(8))
print(collections.Counter(r['route'] for r in rows if r['j']=='SILENT').most_common(12))
