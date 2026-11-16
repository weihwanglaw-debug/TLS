import {afterEach,expect,test,vi} from 'vitest';
import {audienceScheduleHtml,printFixture} from '../src/lib/fixturePrint';
import type {MatchEntry} from '../src/types/config';
const match=(id:string,date='2026-11-06',time='11:00')=>({id,matchDate:date,startTime:time,endTime:'11:40',courtNo:'4',groupId:'G1',round:1,roundLabel:'Group stage',status:'SC',games:[],officials:[{role:'Umpire',name:'Private official'}],remark:'Private note',team1:{id:'a',label:'Club',participants:['Alice <One>','Beth & Two'],seed:1},team2:{id:'b',label:'Club',participants:['Cathy','Diane']}} as MatchEntry);
afterEach(()=>vi.restoreAllMocks());
function documentFor(html:string){return new DOMParser().parseFromString(html,'text/html');}
test('audience sheet prints static courts, dates, compact teams and no administration',()=>{
 const m=match('m');const doc=documentFor(audienceScheduleHtml('Mixed <Doubles>',[m],{G1:'Group A'}));
 expect(doc.querySelector('.schedule-program')!.textContent).toBe('Mixed <Doubles>');
 expect(doc.querySelector('.schedule-date')!.textContent).toContain('Friday, 6 November 2026');
 expect(doc.querySelector('.schedule-court')!.textContent).toBe('4');
 expect(doc.querySelector('.schedule-time')!.textContent).toBe('11:00 - 11:40');
 expect(doc.querySelector('.schedule-match')!.textContent).toContain('Alice <One> / Beth & Two');
 expect(doc.querySelector('.schedule-seed')!.textContent).toBe('1');
 expect(doc.querySelectorAll('thead tr:last-child th')).toHaveLength(5);
 expect(doc.querySelector('button,input,select,script')).toBeNull();
 expect(doc.body.textContent).not.toMatch(/Private|umpire|completed|Action/);
});
test('days and time slots are chronological across groups, missing slots remain visible, and input is unchanged',()=>{
 const rows=[match('late','2026-11-06','19:00'),match('unknown','',''),match('next','2026-11-07','09:00'),{...match('early'),groupId:'G2'},match('tie'),match('missing','2026-11-06','')];
 const before=JSON.stringify(rows),doc=documentFor(audienceScheduleHtml('Doubles',rows,{G1:'Group A',G2:'Group B'}));
 expect([...doc.querySelectorAll<HTMLTableRowElement>('tbody tr')].map(row=>row.cells[2].textContent)).toEqual(['Group B','Group A','Group A','Group A','Group A','Group A']);
 expect([...doc.querySelectorAll('.schedule-time')].map(node=>node.textContent)).toEqual(['11:00 - 11:40','11:00 - 11:40','19:00 - 11:40','To be scheduled','09:00 - 11:40','To be scheduled']);
 expect(doc.querySelectorAll('table')).toHaveLength(3);expect(doc.body.textContent).toContain('Awaiting scheduling');expect(JSON.stringify(rows)).toBe(before);
});
test('scores and public outcomes print without private reasons',()=>{
 const doc=documentFor(audienceScheduleHtml('Final',[{...match('m'),groupId:undefined,roundLabel:'Final',status:'C',games:[{p1:'21',p2:'15'},{p1:'21',p2:'18'}],resultKind:'R'}]));
 expect(doc.querySelector('.schedule-score')!.textContent).toContain('21 - 15');expect(doc.querySelector('.schedule-outcome')!.textContent).toBe('Retirement');
});
test('audience printing retains static values and print page rules while bracket printing remains available',()=>{
 const write=vi.fn();vi.spyOn(window,'open').mockReturnValue({document:{open:vi.fn(),write,close:vi.fn()}} as unknown as Window);
 const html=audienceScheduleHtml('Doubles',[match('m')]);
 expect(printFixture({current:null},'Event & Open','Asia/Singapore',html)).toBeNull();
 expect(write.mock.calls[0][0]).toContain('Match Schedule');expect(write.mock.calls[0][0]).toContain('table-header-group');expect(write.mock.calls[0][0]).toContain('page-break-inside: avoid');
 const node=document.createElement('div');node.innerHTML='<svg viewBox="0 0 100 100"><text>Final</text></svg><button>Admin</button>';
 printFixture({current:node},'Event','Asia/Singapore');
 expect(write.mock.calls[1][0]).toContain('viewBox="0 0 100 100"');expect(write.mock.calls[1][0]).not.toContain('>Admin<');
});
