import {afterEach,beforeEach,expect,test,vi} from 'vitest';
import {cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react';
import EventSettings from '../src/EventSettings';
globalThis.ResizeObserver=class {observe(){}unobserve(){}disconnect(){}} as unknown as typeof ResizeObserver;
const service=vi.hoisted(()=>({api:vi.fn()}));vi.mock('../src/api',()=>service);
const plan={eventId:'E',matchMinutes:40,restMinutes:10,officials:[],sessions:[],dailySessions:[],reviewCourts:[],courts:[],courtsConfigured:false,progression:'I'};
const view={revision:4,eventInfo:{id:'E',startDate:'2026-12-01',endDate:'2026-12-02'},datesEditable:true,eventPlan:plan};
const publication={revision:4,source:'TRS',enabled:false,liveUrl:'https://receiver.test/live',resultsUrl:'https://receiver.test/{programId}/results',intervalSeconds:5,hasCredential:true,apiKey:'visible-publishing-key-12345678901234567890',failures:[]};
beforeEach(()=>{vi.resetAllMocks();service.api.mockImplementation(async(path:string,method?:string)=>method==='PUT'?{...view,revision:5}:path.startsWith('trs/')?publication:view);});
afterEach(cleanup);

test('one edit control unlocks the event and one save submits every section together',async()=>{
 const changed=vi.fn();render(<EventSettings eventId="E" onChanged={changed}/>);
 fireEvent.click(await screen.findByRole('button',{name:'Edit Event'}));
 await screen.findByRole('button',{name:'Save Event'});
 expect(screen.getByRole('button',{name:'Start Date',exact:true})).toBeTruthy();
 expect(screen.queryByLabelText('Estimated match minutes')).toBeNull();
 fireEvent.click(screen.getByRole('switch',{name:'Enable Score Publishing'}));
 expect(screen.getByLabelText('Live-score API URL')).toBeTruthy();
 fireEvent.click(screen.getByRole('button',{name:'Save Event'}));
 await waitFor(()=>expect(changed).toHaveBeenCalledOnce());
 const saves=service.api.mock.calls.filter(([,method])=>method==='PUT');expect(saves).toHaveLength(1);
 expect(saves[0]).toEqual(['tournament/event-settings','PUT',expect.objectContaining({revision:4,startDate:view.eventInfo.startDate,endDate:view.eventInfo.endDate,plan,publishing:expect.objectContaining({enabled:true,apiKey:publication.apiKey})})]);
 expect(screen.queryByRole('button',{name:'Save Dates'})).toBeNull();expect(screen.queryByRole('button',{name:'Save Publishing Settings'})).toBeNull();
});

test('save failure retains the draft for correction without reporting success',async()=>{
 service.api.mockImplementation(async(path:string,method?:string)=>{if(method==='PUT')throw new Error('Result API URL must contain {programId}.');return path.startsWith('trs/')?publication:view;});
 const changed=vi.fn();render(<EventSettings eventId="E" onChanged={changed}/>);
 fireEvent.click(await screen.findByRole('button',{name:'Edit Event'}));await screen.findByRole('button',{name:'Save Event'});
 fireEvent.click(screen.getByRole('button',{name:'Add Official'}));fireEvent.change(screen.getByLabelText('Official name'),{target:{value:'Saved draft official'}});fireEvent.click(screen.getByRole('button',{name:'Save Event'}));
 expect(await screen.findByText('Result API URL must contain {programId}.')).toBeTruthy();expect(changed).not.toHaveBeenCalled();
 expect((screen.getByLabelText('Official name') as HTMLInputElement).value).toBe('Saved draft official');
});

test('existing date locks do not prevent editing other event settings',async()=>{
 service.api.mockImplementation(async(path:string)=>path.startsWith('trs/')?publication:{...view,datesEditable:false});
 render(<EventSettings eventId="E" onChanged={async()=>{}}/>);
 fireEvent.click(await screen.findByRole('button',{name:'Edit Event'}));await screen.findByRole('button',{name:'Save Event'});
 expect(screen.queryByRole('button',{name:'Start Date',exact:true})).toBeNull();
 expect((screen.getByRole('button',{name:'Add Official'}) as HTMLButtonElement).disabled).toBe(false);expect(screen.queryByLabelText('Estimated match minutes')).toBeNull();
});


test('saved credential remains readable after loading and saving',async()=>{
 const enabled={...publication,enabled:true};
 service.api.mockImplementation(async(path:string,method?:string)=>method==='PUT'?{...view,revision:5}:path.startsWith('trs/')?enabled:view);
 render(<EventSettings eventId="E" onChanged={async()=>{}}/>);
 const key=await screen.findByLabelText('API credential') as HTMLInputElement;expect(key.type).toBe('text');expect(key.value).toBe(enabled.apiKey);
 fireEvent.click(screen.getByRole('button',{name:'Edit Event'}));await screen.findByRole('button',{name:'Save Event'});
 const replacement='replacement-publishing-key-12345678901234567890';
 fireEvent.change(screen.getByLabelText('API credential'),{target:{value:replacement}});
 fireEvent.click(screen.getByRole('button',{name:'Save Event'}));await screen.findByRole('dialog',{name:'Saved successfully'});fireEvent.click(screen.getByRole('button',{name:'OK'}));await screen.findByRole('button',{name:'Edit Event'});
 expect((screen.getByLabelText('API credential') as HTMLInputElement).value).toBe(replacement);
 expect(service.api).toHaveBeenCalledWith('tournament/event-settings','PUT',expect.objectContaining({publishing:expect.objectContaining({apiKey:replacement})}));
});
