import {cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react';
import {afterEach,beforeEach,expect,test,vi} from 'vitest';
import SetupApp from '../src/SetupApp';
import {adminUrl,readAdminRoute} from '../src/admin-route';

const service=vi.hoisted(()=>({api:vi.fn()}));vi.mock('../src/api',()=>service);
vi.mock('../src/LiveDashboard',()=>({default:({onEventSettings}:any)=><button onClick={()=>onEventSettings('e:1')}>Open event settings</button>}));
vi.mock('../src/SbaRankings',()=>({default:()=> <p>Ranking module</p>}));
vi.mock('../src/GameSettingPresets',()=>({default:()=> <p>Preset module</p>}));
vi.mock('../src/MasterConfig',()=>({default:()=> <p>Master module</p>}));
vi.mock('../src/EventSettings',()=>({default:()=> <p>Event settings panel</p>}));
vi.mock('../src/EventDatesEditor',()=>({default:()=>null}));
vi.mock('../src/EventImport',()=>({default:({onBack,kind}:any)=><><p>Import {kind}</p><button onClick={onBack}>Cancel import</button></>}));
vi.mock('../src/FixtureWorkspace',()=>({default:({eventId,programId,initialStep,onContextChange,onBack}:any)=><><p>Workspace {eventId} {programId} {initialStep}</p><button onClick={()=>onContextChange('p:1','schedule')}>Open scheduling step</button><button onClick={onBack}>Fixture list</button></>}));

const view={data:{revision:1,today:'2026-10-28',events:[{id:'e:1',name:'Test Event',startDate:'2026-10-28',endDate:'2026-10-30'}],programs:[{id:'p:1',eventId:'e:1',name:'Singles',sport:'Badminton',entryType:'S',status:'A'}],entries:[],participants:[],imports:[],programPlans:[]},readiness:[]};
beforeEach(()=>{history.replaceState(null,'','/dashboard');service.api.mockReset();service.api.mockImplementation(async(path:string)=>path==='setup/zones'?[]:structuredClone(view));});
afterEach(()=>{cleanup();history.replaceState(null,'','/');});

test('route addresses round-trip event/program identifiers and reject invalid paths',()=>{
 const route={page:'fixtures' as const,eventId:'event:a/b',programId:'program:1',step:'schedule' as const};
 const url=adminUrl(route),parsed=new URL(url,location.origin);expect(readAdminRoute(parsed.pathname,parsed.search)).toEqual(route);
 expect(readAdminRoute('/unknown')).toEqual({page:'not-found'});
 expect(readAdminRoute('/events/%E0%A4%A')).toEqual({page:'not-found'});
 expect(readAdminRoute('/fixtures/e','?step=bad')).toEqual({page:'not-found'});
});

test('sidebar changes addresses, browser history restores modules, and refresh restores event details',async()=>{
 const app=render(<SetupApp/>);await screen.findByRole('button',{name:'Open event settings'});
 fireEvent.click(screen.getByRole('button',{name:'Event'}));await screen.findByRole('heading',{name:'Event'});expect(location.pathname).toBe('/events');
 fireEvent.click(screen.getByRole('button',{name:'Master Setting'}));await screen.findByText('Master module');expect(location.pathname).toBe('/settings');
 history.back();await waitFor(()=>expect(location.pathname).toBe('/events'));await screen.findByRole('heading',{name:'Event'});
 history.forward();await screen.findByText('Master module');expect(location.pathname).toBe('/settings');
 fireEvent.click(screen.getByRole('button',{name:'Dashboard'}));fireEvent.click(await screen.findByRole('button',{name:'Open event settings'}));
 await screen.findByText('Event settings panel');expect(location.pathname).toBe('/events/e%3A1');
 app.unmount();render(<SetupApp/>);await screen.findByText('Event settings panel');expect(location.pathname).toBe('/events/e%3A1');
});

test('fixture filters and workspace context are in the URL and restore on history navigation',async()=>{
 history.replaceState(null,'','/fixtures');render(<SetupApp/>);
 fireEvent.change(await screen.findByLabelText('Fixture event'),{target:{value:'e:1'}});
 expect(new URLSearchParams(location.search).get('event')).toBe('e:1');
 fireEvent.change(screen.getByLabelText('Fixture program'),{target:{value:'p:1'}});expect(new URLSearchParams(location.search).get('program')).toBe('p:1');
 fireEvent.click(screen.getByRole('button',{name:'Schedule Event'}));await screen.findByText(/Workspace e:1/);expect(location.pathname).toBe('/fixtures/e%3A1');expect(location.search).toBe('?step=schedule');
 fireEvent.click(screen.getByRole('button',{name:'Open scheduling step'}));expect(new URLSearchParams(location.search).get('program')).toBe('p:1');
 fireEvent.click(screen.getByRole('button',{name:'Fixture list'}));await screen.findByLabelText('Fixture event');expect((screen.getByLabelText('Fixture event') as HTMLSelectElement).value).toBe('e:1');
 history.back();await screen.findByText('Workspace e:1 p:1 schedule');
});

test('participant filters, import pages and missing records have usable direct addresses',async()=>{
 history.replaceState(null,'','/participants?event=e%3A1&program=p%3A1');const app=render(<SetupApp/>);
 expect((await screen.findByLabelText('Participant event') as HTMLSelectElement).value).toBe('e:1');expect((screen.getByLabelText('Participant program') as HTMLSelectElement).value).toBe('p:1');
 fireEvent.change(screen.getByLabelText('Participant event'),{target:{value:''}});expect(location.search).toBe('');
 fireEvent.click(screen.getByRole('button',{name:'Event'}));fireEvent.click(await screen.findByRole('button',{name:'Import Excel'}));await screen.findByText('Import excel');expect(location.pathname).toBe('/events/import/excel');
 fireEvent.click(screen.getByRole('button',{name:'Cancel import'}));await screen.findByRole('heading',{name:'Event'});expect(location.pathname).toBe('/events');
 app.unmount();history.replaceState(null,'','/events/deleted');render(<SetupApp/>);await screen.findByText(/This address is invalid/);fireEvent.click(screen.getByRole('button',{name:'Go to Dashboard'}));expect(location.pathname).toBe('/dashboard');
});
