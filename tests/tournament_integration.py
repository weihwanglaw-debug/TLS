import test_inputs
"""Local tournament generation, scheduling and advancement in a disposable SQL database."""
import copy,json,subprocess,tempfile,time,socket,urllib.request,urllib.error
from pathlib import Path
from datetime import date,timedelta
from sql_test_database import TestDatabase,root,project,dll
base='http://localhost:5197/api/';checks=0;process=None
def call(path,body=None,status=200,method=None):
    global checks
    req=urllib.request.Request(base+path,data=json.dumps(body).encode() if body is not None else None,method=method or ('POST' if body is not None else 'GET'),headers={'Content-Type':'application/json'})
    try:
        with urllib.request.urlopen(req,timeout=30) as r:code=r.status;raw=r.read()
    except urllib.error.HTTPError as e:code=e.code;raw=e.read()
    assert code==status,(path,code,status,raw[:1200]);checks+=1;return json.loads(raw)
with socket.socket() as probe:assert probe.connect_ex(('127.0.0.1',5197))!=0,'Test port occupied'
db=TestDatabase()
try:
 with tempfile.TemporaryDirectory(prefix='tls-tournament-test-') as temp:
    log=open(Path(temp)/'server.log','w')
    def start():
        global process
        process=subprocess.Popen(['dotnet',str(dll)],cwd=project,env={**db.env,'ASPNETCORE_URLS':'http://localhost:5197'},stdout=log,stderr=log)
        for _ in range(100):
            try:return call('setup/')
            except urllib.error.URLError:time.sleep(.1)
        raise RuntimeError('Server not ready')
    def stop():
        global process
        if process:process.terminate();process.wait(timeout=10);process=None
    try:
        presetIds={}
        s=start()['data'];p=test_inputs.package();d1=(date.today()+timedelta(days=10)).isoformat();d2=(date.today()+timedelta(days=11)).isoformat()
        p['events'][0].update(startDate=d1,endDate=d2,maxProgramsPerParticipant=None)
        p['programs']=[{**p['programs'][0],'id':'p','minCapacity':2,'maxCapacity':16},{**p['programs'][0],'id':'p2','name':'Second program','minCapacity':2,'maxCapacity':16}]
        p['participants']=[{**p['participants'][0],'id':str(i),'name':f'Test Player {i}','email':f'p{i}@example.com'} for i in range(8)]
        p['entries']=[{'id':f'e{i}','eventId':p['events'][0]['id'],'programId':'p','participantIds':[str(i)],'name':'','seed':i+1 if i<4 else None,'status':'A'} for i in range(8)]
        p['entries'] += [{'id':f'second{i}','eventId':p['events'][0]['id'],'programId':'p2','participantIds':[str(i)],'name':'','seed':None,'status':'A'} for i in range(4)]
        req=urllib.request.Request(base+'setup/preview?kind=trs&revision=0',data=json.dumps(p).encode(),headers={'Content-Type':'application/octet-stream'})
        with urllib.request.urlopen(req) as r:preview=json.loads(r.read())
        s=call('setup/confirm',{'revision':0,'token':preview['token']})['data'];eventId=s['events'][0]['id'];pid=s['programs'][0]['id'];pid2=s['programs'][1]['id'];entries=[e for e in s['entries'] if e['programId']==pid];eventPath='tournament/'+eventId
        def view():return call(eventPath)
        def mutation(path,data,status=200):
            current=view()
            if path=='result':
                match=next(m for p in current['plans'] for m in p['matches'] if m['id']==data['matchId'])
                data={'matchVersion':match['matchVersion'],'reason':'Test correction','presetId':presetIds[match['programId']],**data}
            return call('tournament/'+path,{'revision':current['revision'],**data},status,'PUT')
        sessions=[{'date':d1,'start':'10:00:00','end':'11:30:00'},{'date':d1,'start':'12:30:00','end':'14:00:00'},{'date':d2,'start':'10:00:00','end':'13:00:00'}]
        eventPlan={'eventId':eventId,'sessions':sessions,'officials':[{'id':eventId+'-O001','name':'Official One'},{'id':eventId+'-O002','name':'Official Two'}],'matchMinutes':30,'restMinutes':0}
        invalid=copy.deepcopy(eventPlan);invalid['sessions'].append({'date':d1,'start':'11:00:00','end':'12:00:00'});mutation('event',{'plan':invalid},400)
        invalid=copy.deepcopy(eventPlan);invalid['sessions'][0]['date']='2000-01-01';mutation('event',{'plan':invalid},400)
        mutation('event',{'plan':eventPlan})
        rules={'name':'21 point test','gamesToWin':2,'pointsToWin':21,'winBy':2,'pointCap':30,'intervalAt':11,'intervalSeconds':60,'betweenGamesSeconds':120,'decidingEndsAt':11,'cardsEnabled':True,'challengesEnabled':False,'challengesPerGame':2,'countShuttles':True}
        mutation('draw-settings',{'eventId':eventId,'programIds':[pid],'groupCount':5,'advancePerGroup':1},400)
        mutation('draw-settings',{'eventId':eventId,'programIds':[pid],'groupCount':2,'advancePerGroup':2})
        preset=call('presets/',{'revision':view()['revision'],'preset':{'id':'','name':rules['name'],'status':'A','isDefault':True,'rules':rules}},method='PUT')['presets'][-1];presetIds[pid]=preset['id']
        generation={'eventId':eventId,'programIds':[pid],'courts':[1],'earliestStart':d1+'T10:00:00','matchMinutes':30}
        def generate(status=200):return call('tournament/preview',{'revision':view()['revision'],**generation},status)
        generate(400) # Seeding must be explicitly reviewed, including unseeded draws.
        seeds={e['id']:e['seed'] for e in entries};invalid={**seeds,entries[1]['id']:1};mutation('seeds',{'programId':pid,'seeds':invalid},400)
        mutation('seeds',{'programId':pid,'seeds':seeds})
        bad=copy.deepcopy(generation);bad['courts']=[99];call('tournament/preview',{'revision':view()['revision'],**bad},400)
        bad=copy.deepcopy(generation);bad['matchMinutes']=120;call('tournament/preview',{'revision':view()['revision'],**bad},400)
        assert view()['plans'][0]['matches']==[]
        draw=generate();assert len(draw['matches'])==12
        assert {m['start'][:10] for m in draw['matches']}=={d1,d2}
        assert all(m['court']==1 and m['umpireId'] in [eventId+'-O001',eventId+'-O002'] for m in draw['matches'])
        for m in draw['matches']:
            assert any(m['start']>=x['date']+'T'+x['start'] and m['end']<=x['date']+'T'+x['end'] for x in sessions)
        groupEntries=draw['groups'][0]['groups'];assert sorted(len(g['entryIds']) for g in groupEntries)==[4,4]
        seededFirstTwo=[entries[0]['id'],entries[1]['id']];assert all(sum(i in g['entryIds'] for i in seededFirstTwo)==1 for g in groupEntries)
        saved=call('tournament/confirm',{'revision':draw['revision'],'token':draw['token']})
        assert len(saved['plans'][0]['matches'])==12
        call('tournament/reset',{'revision':saved['revision'],'programId':pid},400)
        call('tournament/reset-schedule',{'revision':view()['revision'],'eventId':eventId,'roundKeys':[pid+':G:1']})
        call('tournament/reset',{'revision':view()['revision'],'programId':pid})
        assert call('setup/')['data']['imports'][0]['stage']=='P'
        assert view()['plans'][0]['matches']==[]
        draw=generate();saved=call('tournament/confirm',{'revision':draw['revision'],'token':draw['token']})
        generate(400) # Group results required for knockout.
        mutation('seeds',{'programId':pid,'seeds':seeds},400)
        mutation('draw-settings',{'eventId':eventId,'programIds':[pid],'groupCount':2,'advancePerGroup':2},400)
        current=call('setup/')['data'];assert current['imports'][0]['stage']=='F'
        a,b=saved['plans'][0]['matches'][:2]
        mutation('schedule',{'matchId':b['id'],'court':1,'start':a['start'],'umpireId':eventId+'-O001'},400)
        mutation('result',{'matchId':a['id'],'games':[{'a':21,'b':20},{'a':21,'b':19}]},400)
        mutation('result',{'matchId':a['id'],'games':[{'a':21,'b':10}]},400)
        for match in saved['plans'][0]['matches']:mutation('result',{'matchId':match['id'],'games':[{'a':21,'b':15},{'a':21,'b':17}]})
        v=view();assert sum(r['wins'] for g in v['standings'] for r in g['rows'])==12
        call('tournament/reset',{'revision':v['revision'],'programId':pid},400)
        generate(400) # No time left: do not extend the event dates silently.
        eventPlan['sessions'].append({'date':d2,'start':'14:00:00','end':'18:00:00'});mutation('event',{'plan':eventPlan})
        generation['courts']=[1,2];draw=generate();assert len(draw['matches'])==2 and all(m['roundLabel']=='Semifinals' for m in draw['matches'])
        assert all(m['start']>=d2+'T14:00:00' for m in draw['matches'])
        v=call('tournament/confirm',{'revision':draw['revision'],'token':draw['token']});semis=[m for m in v['plans'][0]['matches'] if m['phase']=='K']
        mutation('result',{'matchId':a['id'],'games':[{'a':21,'b':15},{'a':21,'b':17}]},400) # Group results locked after qualification.
        generate(400)
        for m in semis:mutation('result',{'matchId':m['id'],'games':[{'a':21,'b':15},{'a':21,'b':17}]})
        generation['courts']=[1];draw=generate();assert len(draw['matches'])==1 and draw['matches'][0]['roundLabel']=='Final' and draw['matches'][0]['court']==1
        v=call('tournament/confirm',{'revision':draw['revision'],'token':draw['token']});final=draw['matches'][0]
        mutation('result',{'matchId':final['id'],'games':[{'a':21,'b':15},{'a':21,'b':17}]})
        assert view()['plans'][0]['phase']=='C';generate(400)
        # A second program shares players: schedule globally, honour rest and use equipped courts.
        eventPlan['restMinutes']=30
        eventPlan['officials'].append({'id':eventId+'-O003','name':'Official Three'})
        eventPlan['sessions'].append({'date':d2,'start':'18:30:00','end':'22:00:00'})
        mutation('event',{'plan':eventPlan})
        challengeRules={**rules,'challengesEnabled':True}
        mutation('draw-settings',{'eventId':eventId,'programIds':[pid2],'groupCount':1,'advancePerGroup':3})
        seeds2={e['id']:None for e in s['entries'] if e['programId']==pid2}
        mutation('seeds',{'programId':pid2,'seeds':seeds2})
        challengePreset=call('presets/',{'revision':view()['revision'],'preset':{'id':'','name':'Challenge profile','status':'A','rules':challengeRules}},method='PUT')['presets'][-1];presetIds[pid2]=challengePreset['id']
        generation['programIds']=[pid2];generation['courts']=[2]
        # Challenge rules are assigned from a preset at pairing/result entry, after the draw.
        eventPlan['reviewCourts']=[2];mutation('event',{'plan':eventPlan})
        history=view()['plans'][0]['matches'];draw=generate()
        assert len(draw['matches'])==6 and all(m['court']==2 for m in draw['matches'])
        from datetime import datetime
        entryPeople={e['id']:set(e['participantIds']) for e in s['entries']}
        for m in draw['matches']:
            players=entryPeople[m['entryA']]|entryPeople[m['entryB']]
            for other in history:
                if not other['start']:continue
                overlaps=datetime.fromisoformat(m['start'])<datetime.fromisoformat(other['end']) and datetime.fromisoformat(other['start'])<datetime.fromisoformat(m['end'])
                assert not overlaps or (m['court']!=other['court'] and m['umpireId']!=other['umpireId'])
                if players & (entryPeople[other['entryA']]|entryPeople.get(other['entryB'],set())):
                    assert datetime.fromisoformat(m['start'])>=datetime.fromisoformat(other['end'])+timedelta(minutes=30) or datetime.fromisoformat(m['end'])+timedelta(minutes=30)<=datetime.fromisoformat(other['start'])
        v=call('tournament/confirm',{'revision':draw['revision'],'token':draw['token']});groups2=draw['matches'];m=groups2[0]
        mutation('schedule',{'matchId':m['id'],'court':m['court'],'start':m['start'],'umpireId':m['umpireId'],'serviceJudgeId':m['umpireId']},400)
        mutation('schedule',{'matchId':m['id'],'court':m['court'],'start':m['start'],'umpireId':m['umpireId'],'serviceJudgeId':eventId+'-O003'})
        current=call('setup/')['data'];call('setup/settings',{'revision':current['revision'],'timeZone':'UTC'},400,'PUT')
        current=call('setup/')['data'];call('setup/settings',{'revision':current['revision'],'timeZone':current['timeZone'],'courtCount':1},400,'PUT')
        late=(datetime.fromisoformat(m['end'])+timedelta(minutes=120)).isoformat()
        mutation('result',{'matchId':m['id'],'games':[{'a':21,'b':15},{'a':21,'b':17}],'finishedAt':late})
        assert view()['warnings'] # Overrun is flagged, not silently rescheduled.
        remaining=groups2[1:]
        # Move one outstanding match later without changing its draw or entry identities.
        second=remaining[0];mutation('schedule',{'matchId':second['id'],'court':2,'start':d2+'T19:30:00','umpireId':eventId+'-O002'})
        for index,m in enumerate(remaining):
            if index==1:mutation('result',{'matchId':m['id'],'games':[],'walkoverWinner':m['entryA']})
            else:mutation('result',{'matchId':m['id'],'games':[{'a':21,'b':15},{'a':21,'b':17}]})
        draw=generate();assert len(draw['matches'])==2 and sum(m['status']=='BY' for m in draw['matches'])==1
        v=call('tournament/confirm',{'revision':draw['revision'],'token':draw['token']})
        for m in draw['matches']:
            if m['status']=='BY':mutation('result',{'matchId':m['id'],'games':[{'a':21,'b':15},{'a':21,'b':17}]},400)
            else:mutation('result',{'matchId':m['id'],'games':[{'a':21,'b':15},{'a':21,'b':17}]})
        generation['courts']=[99];generate(400) # Courts must belong to the event.
        generation['courts']=[2];draw=generate();assert len(draw['matches'])==1 and draw['matches'][0]['roundLabel']=='Final'
        call('tournament/confirm',{'revision':draw['revision'],'token':draw['token']})
        m=draw['matches'][0];mutation('result',{'matchId':m['id'],'games':[{'a':21,'b':15},{'a':21,'b':17}]})
        old=view();stop();start();assert view()==old
        print(f'PASS: {checks} HTTP assertions; groups, seeds, sessions, multi-day allocation, conflicts, scoring, qualification and round-by-round courts verified.')
    except Exception:
        log.flush();print((Path(temp)/'server.log').read_text());raise
    finally:stop();log.close()
finally:db.close()

