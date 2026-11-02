import {cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react';
import {afterEach,beforeEach,expect,test,vi} from 'vitest';
const service=vi.hoisted(()=>({api:vi.fn()}));vi.mock('../src/api',()=>service);
import SeedingPage from '../src/SeedingPage';
import {FixtureList} from '../src/FoundationPages';
import type {View} from '../src/setup-types';
const data={revision:1,program:{id:'p',name:"Men's Singles",entryType:'S',sport:'Badminton'},eventName:'Test Event',locked:false,reviewed:false,mode:'M',seeds:{a:1,b:null},importedSeeds:{a:1,b:null},categoryAvailable:true,sbaAvailable:true,seedCount:1,rankingFingerprint:'hash',rankingFetchedAt:null,entries:[{id:'a',name:'a',players:[{name:'First Entry',sbaId:'SBA001',club:'Imported Club'}],ranking:null},{id:'b',name:'b',players:[{name:'Second Entry',sbaId:'SBA002',club:'Other Club'}],ranking:null}]};
afterEach(cleanup);beforeEach(()=>{vi.resetAllMocks();service.api.mockImplementation(async(path:string,method='GET',body?:{seeds:unknown;mode:string})=>method==='GET'?structuredClone(data):path.endsWith('/suggest')?{seeds:{a:null,b:1},rankingFingerprint:'hash',matched:1,requested:1}:{...structuredClone(data),seeds:body?.seeds,mode:body?.mode,reviewed:true});});
test('clickable column distinguishes import review, explicit no-seeding and draw lock',()=>{
 const onSeeding=vi.fn();const view={data:{events:[{id:'e',name:'Event',startDate:'2026-10-20',endDate:'2026-10-21'}],programs:[{id:'p',eventId:'e',name:'Program',sport:'Badminton',entryType:'S',status:'A'}],entries:[{id:'a',programId:'p',participantIds:['person1','person2'],seed:1,status:'A'}],programPlans:[]},readiness:[]} as unknown as View;
 const page=render(<FixtureList view={view} onManage={()=>{}} onSeeding={onSeeding}/>);expect(screen.getByText('Pending')).toBeTruthy();expect(screen.getByRole('button',{name:'Seeding for Program: 1 seeded entry · Review required'}).textContent).toBe('1/2');expect(screen.queryByText('Preparation')).toBeNull();
 fireEvent.click(screen.getByRole('button',{name:'Seeding for Program: 1 seeded entry · Review required'}));expect(onSeeding).toHaveBeenCalledWith('e','p');
 view.data.programPlans=[{programId:'p',seeds:{a:null},seedsReviewed:true,matches:[{}]}];page.rerender(<FixtureList view={view} onManage={()=>{}} onSeeding={onSeeding}/>);
 expect(screen.getByRole('button',{name:'Seeding for Program: No seeding required'})).toBeTruthy();expect(screen.getByRole('button',{name:'Seeding for Program: No seeding required'}).textContent).toBe('NA/2');expect(screen.getByText('Seeds / Participants')).toBeTruthy();expect(screen.getByText('Draw generated')).toBeTruthy();
});
test('SBA preview changes neither persisted nor draft seeds until applied; save carries ranking fingerprint',async()=>{
 const changed=vi.fn();render(<SeedingPage programId="p" onChanged={changed} onBack={()=>{}}/>);await screen.findByText('1 seeded entry');
 fireEvent.click(screen.getByRole('button',{name:'Suggest Seeds from SBA'}));await screen.findByRole('dialog',{name:'Review SBA Suggestions'});
 expect(service.api.mock.calls.filter(c=>c[1]==='PUT')).toHaveLength(0);
 fireEvent.click(screen.getByRole('button',{name:'Cancel',exact:true}));expect((screen.getByLabelText('Seed for First Entry') as HTMLInputElement).value).toBe('1');
 fireEvent.click(screen.getByRole('button',{name:'Suggest Seeds from SBA'}));await screen.findByRole('dialog',{name:'Review SBA Suggestions'});
 fireEvent.click(screen.getByRole('button',{name:'Apply Suggestions to Draft'}));expect((screen.getByLabelText('Seed for Second Entry') as HTMLInputElement).value).toBe('1');
 expect(service.api.mock.calls.filter(c=>c[1]==='PUT')).toHaveLength(0);
 fireEvent.click(screen.getByRole('button',{name:'Save Reviewed Seeding'}));await screen.findByRole('dialog',{name:'Saved successfully'});
 expect(service.api).toHaveBeenLastCalledWith('seeding/p','PUT',{revision:1,seeds:{a:null,b:1},mode:'S',seedCount:1,rankingFingerprint:'hash'});expect(changed).toHaveBeenCalledOnce();
});
test('duplicate seeds disable saving and cancelling a no-seed draft writes nothing',async()=>{
 const back=vi.fn();render(<SeedingPage programId="p" onChanged={async()=>{}} onBack={back}/>);await screen.findByLabelText('Seed for Second Entry');
 fireEvent.change(screen.getByLabelText('Seed for Second Entry'),{target:{value:'1'}});expect(screen.getByRole('alert').textContent).toContain('unique');expect((screen.getByRole('button',{name:'Save Reviewed Seeding'}) as HTMLButtonElement).disabled).toBe(true);
 fireEvent.click(screen.getByRole('switch',{name:'Use Seeding'}));expect(screen.getByRole('button',{name:'Confirm No Seeding Required'})).toBeTruthy();
 fireEvent.click(screen.getByRole('button',{name:'Cancel',exact:true}));expect(back).toHaveBeenCalledOnce();expect(service.api.mock.calls.filter(c=>c[1]==='PUT')).toHaveLength(0);
});

test('chosen seed count limits manual values without clearing them',async()=>{
 render(<SeedingPage programId="p" onChanged={async()=>{}} onBack={()=>{}}/>);await screen.findByLabelText('Number of Seeds');
 fireEvent.change(screen.getByLabelText('Number of Seeds'),{target:{value:'2'}});fireEvent.change(screen.getByLabelText('Seed for Second Entry'),{target:{value:'2'}});
 fireEvent.change(screen.getByLabelText('Number of Seeds'),{target:{value:'1'}});expect(screen.getByRole('alert').textContent).toContain('Number of Seeds');expect((screen.getByLabelText('Seed for Second Entry') as HTMLInputElement).value).toBe('2');
 expect((screen.getByRole('button',{name:'Save Reviewed Seeding'}) as HTMLButtonElement).disabled).toBe(true);
});
test('team rows expand imported rosters and other sports cannot use SBA',async()=>{
 const team=structuredClone(data);team.program.entryType='T';team.program.sport='Basketball';team.sbaAvailable=false;team.entries[0].name='Group 1';team.entries[0].players[0].club='North School';
 service.api.mockResolvedValue(team);render(<SeedingPage programId="p" onChanged={async()=>{}} onBack={()=>{}}/>);
 await screen.findByRole('button',{name:/North School/});expect(screen.queryByText('First Entry')).toBeNull();expect(screen.queryByRole('columnheader',{name:'SBA ID(s)'})).toBeNull();expect((screen.getByRole('button',{name:'Suggest Seeds from SBA'}) as HTMLButtonElement).disabled).toBe(true);
 fireEvent.click(screen.getByRole('button',{name:/North School/}));expect(screen.getByText('First Entry · North School')).toBeTruthy();expect(screen.getByLabelText('Number of Seeds')).toBeTruthy();
});

test.each([
 ['ids','No SBA IDs found in list'],
 ['category','No matching SBA category'],
 ['matches','No matching SBA entries'],
 ['sport','Badminton singles/doubles only'],
])('unavailable SBA reason is visible without a tooltip: %s',async(reason,text)=>{
 const unavailable=structuredClone(data);unavailable.sbaAvailable=false;
 if(reason==='ids')unavailable.entries.forEach(e=>e.players.forEach(p=>p.sbaId=''));
 if(reason==='category')unavailable.categoryAvailable=false;
 if(reason==='sport')unavailable.program.sport='Tennis';
 service.api.mockResolvedValue(unavailable);render(<SeedingPage programId="p" onChanged={async()=>{}} onBack={()=>{}}/>);
 expect(await screen.findByText(text)).toBeTruthy();const toggle=screen.getByRole('button',{name:'Suggest Seeds from SBA'});expect(toggle.closest('[title]')).toBeNull();expect(toggle.getAttribute('aria-describedby')).toBe('sba-unavailable');
 expect((screen.getByLabelText('Number of Seeds') as HTMLInputElement).disabled).toBe(false);fireEvent.click(screen.getByRole('switch',{name:'Use Seeding'}));expect(screen.queryByLabelText('Number of Seeds')).toBeNull();expect(screen.queryByRole('button',{name:'Suggest Seeds from SBA'})).toBeNull();expect(screen.queryByRole('table')).toBeNull();fireEvent.click(screen.getByRole('switch',{name:'Use Seeding'}));expect((screen.getByLabelText('Number of Seeds') as HTMLInputElement).disabled).toBe(false);
});


test('Reset Seeding requires confirmation and reloads an unreviewed decision',async()=>{
 let reset=false;service.api.mockImplementation(async(path:string,method='GET')=>{if(path==='tournament/reset-seeding'){reset=true;return {revision:2};}return {...structuredClone(data),reviewed:!reset,seeds:reset?{a:null,b:null}:data.seeds};});
 const changed=vi.fn();render(<SeedingPage programId="p" onChanged={changed} onBack={()=>{}}/>);await screen.findByText('1 seeded entry');fireEvent.click(screen.getByRole('button',{name:'Options'}));fireEvent.click(screen.getByRole('menuitem',{name:'Reset Seeding'}));await screen.findByRole('dialog',{name:'Reset Seeding'});expect(reset).toBe(false);fireEvent.click(screen.getByRole('button',{name:'Confirm Reset Seeding'}));await screen.findByText('Seeding reset. Review seeding before generating a draw.');expect(changed).toHaveBeenCalledOnce();expect(service.api).toHaveBeenCalledWith('tournament/reset-seeding','POST',{revision:1,programId:'p'});expect((screen.getByLabelText('Seed for First Entry') as HTMLInputElement).value).toBe('');
});
