import {cleanup,fireEvent,render,screen} from '@testing-library/react';
import {afterEach,expect,test,vi} from 'vitest';
import {ResultsTab} from '../src/components/admin/fixtures/ResultsTab';
import type {BracketState,MatchEntry} from '../src/types/config';
const csv=vi.hoisted(()=>vi.fn());
vi.mock('../src/lib/exportCsv',()=>({exportFixtureRoundCsv:csv}));
afterEach(()=>{cleanup();csv.mockClear();});
const match=(id:string,date:string,time:string,groupId?:string)=>({id,groupId,round:1,roundLabel:'Final',matchDate:date,startTime:time,endTime:'',courtNo:'1',status:'SC',games:[],officials:[],team1:{id:id+'a',label:id},team2:{id:id+'b',label:'Opponent'}} as MatchEntry);
function state(group:boolean):BracketState {
 const matches=[match('Late','2026-11-06','19:00'),match('No date','','11:00'),match('Next day','2026-11-07','09:00'),match('Early','2026-11-06','11:00'),match('Equal','2026-11-06','11:00:00'),match('No time','2026-11-06','')];
 return {phase:group?'group':'knockout',format:group?'group_knockout':'single_elimination',config:{},groups:group?[{id:'A',name:'Group A',teams:[],matches:matches.map(m=>({...m,groupId:'A'}))},{id:'B',name:'Group B',teams:[],matches:[match('Other group','2026-11-06','08:00','B')]}]:[],matches:group?[]:matches} as BracketState;
}
test.each([true,false])('table and export follow start time without mutating the draw (groups=%s)',group=>{
 const bracket=state(group),before=JSON.stringify(bracket);
 const props={bracketState:bracket,eventName:'Event',programName:'Program',onOpenScore:vi.fn(),onUpdateSchedule:async()=>{}};
 const page=render(<ResultsTab {...props}/>);
 const names=()=>Array.from(page.container.querySelectorAll('tbody tr')).map(row=>row.querySelector('td')!.textContent);
 const expected=['Early','Equal','Late','Next day','No date','No time',...(group?['Other group']:[])];
 expect(names()).toEqual(expected);
 fireEvent.click(screen.getByRole('button',{name:'Export CSV'}));
 expect(csv.mock.calls[0][3].map((m:MatchEntry)=>m.team1.label)).toEqual(expected);
 expect(JSON.stringify(bracket)).toBe(before);
 const changed=structuredClone(bracket);
 (group?changed.groups[0].matches:changed.matches)[0].startTime='10:00';
 page.rerender(<ResultsTab {...props} bracketState={changed}/>);
 expect(names()[0]).toBe('Late');
});

test('print content follows the selected knockout round',()=>{
 const bracket=state(false);bracket.matches.push({...match('Semi','2026-11-06','10:00'),round:2,roundLabel:'Next round'});
 let print=()=>'';
 render(<ResultsTab bracketState={bracket} eventName="Event" programName="Program" onOpenScore={()=>{}} onUpdateSchedule={async()=>{}} onPrintReady={run=>{print=run;}}/>);
 expect(print()).toContain('Semi');expect(print()).not.toContain('Early');
 fireEvent.change(screen.getByRole('combobox'),{target:{value:'round:1'}});
 expect(print()).toContain('Early');expect(print()).not.toContain('Semi');
});
