import {expect,test,afterEach} from 'vitest';
import {cleanup,render,screen} from '@testing-library/react';
import {fixtureProgress} from '../src/fixture-progress';
import {BracketView} from '../src/components/admin/fixtures/BracketView';
import type {BracketState,MatchEntry} from '../src/types/config';
afterEach(cleanup);
const matches=(count:number,round:number,status:string)=>Array.from({length:count},(_,i)=>({id:'m'+round+i,phase:'K',round,status,roundLabel:count===1?'Final':count===2?'Semifinals':'Quarterfinals'}));
test('overview advances the label only when the current program round is complete',()=>{
 const plan={format:'KO',phase:'K',lastRound:1,seedsReviewed:true,matches:matches(4,1,'C')};expect(fixtureProgress(plan)).toMatchObject({stage:'Semifinals',draw:'Pending draw',nextStep:'draw',schedule:'Pending'});
 const semis={...plan,lastRound:2,matches:[...plan.matches,...matches(2,2,'SC')]};expect(fixtureProgress(semis)).toMatchObject({stage:'Semifinals',draw:'Draw generated',schedule:'Pending',nextStep:'schedule'});
 expect(fixtureProgress({...semis,matches:semis.matches.map(m=>({...m,start:'2026-11-02T10:00:00',court:1}))}).schedule).toBe('Scheduled');
 expect(fixtureProgress({...semis,lastRound:3,matches:[...semis.matches,...matches(1,3,'C')]})).toMatchObject({stage:'Final',completed:true,draw:'Completed',schedule:'Completed'});
});
test('round robin completes without inventing a next draw; groups lead to the correct knockout stage',()=>{
 const plan={format:'RR',phase:'G',seedsReviewed:true,matches:matches(3,1,'C').map(m=>({...m,phase:'G'}))};expect(fixtureProgress(plan)).toMatchObject({stage:'Round Robin',completed:true});expect(fixtureProgress({...plan,format:'GK',groupCount:2,advancePerGroup:2})).toMatchObject({stage:'Semifinals',draw:'Pending draw'});expect(fixtureProgress()).toMatchObject({stage:'Opening round',nextStep:'seeding'});
});
function bracket(count=4){return {format:'knockout',phase:'knockout',groups:[],matches:Array.from({length:count},(_,i)=>({id:'m'+i,round:1,status:'SC',team1:{id:'a'+i,label:'First '+i,participants:['First '+i]},team2:{id:'b'+i,label:'Second '+i,participants:['Second '+i]},games:[],courtNo:i===0?'2':'',matchDate:i===0?'2026-11-02':'',startTime:i===0?'10:00':'',endTime:i===0?'10:30':''})),seeds:[]} as unknown as BracketState;}
test('projected semifinal feeders stay connected to the final and show saved schedule metadata',()=>{
 const {container}=render(<BracketView bracketState={bracket()}/>);const connectors=container.querySelectorAll('g[fill="none"]');expect(connectors).toHaveLength(3);const projected=connectors[2].querySelectorAll('path');expect(projected).toHaveLength(4);const spine=projected[2].getAttribute('d')!.match(/[-\d.]+/g)!.map(Number),arm=projected[3].getAttribute('d')!.match(/[-\d.]+/g)!.map(Number);expect(spine[0]).toBe(arm[0]);expect(arm[1]).toBeGreaterThanOrEqual(spine[1]);expect(arm[1]).toBeLessThanOrEqual(spine[2]);expect(screen.getByText(/Court 2.*10:00–10:30/)).toBeTruthy();
});
test('a single feeder still has a vertical path to its next-round card',()=>{
 const {container}=render(<BracketView bracketState={bracket(3)}/>);const connector=container.querySelectorAll('g[fill="none"]')[1];expect(connector.querySelectorAll('path')).toHaveLength(3);
});

test('results access survives a pending next round and supports partially scheduled matches',()=>{
 const plan={format:'KO',phase:'K',lastRound:1,seedsReviewed:true,matches:matches(4,1,'SC').map((m,i)=>({...m,start:i===0?'2026-11-02T10:00:00':null,court:i===0?1:null}))};
 expect(fixtureProgress(plan)).toMatchObject({resultsAvailable:true,scheduled:1,unscheduled:3,schedule:'Partly scheduled',nextStep:'schedule'});
 expect(fixtureProgress({...plan,matches:plan.matches.map(m=>({...m,status:'C'}))})).toMatchObject({resultsAvailable:true,nextDraw:true,nextStep:'draw'});
 expect(fixtureProgress({...plan,matches:plan.matches.map(m=>({...m,start:'2026-11-02T10:00:00',court:1}))})).toMatchObject({nextStep:'results'});
});
