import {cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react';
import {afterEach,expect,test,vi} from 'vitest';
import FixtureWorkspace from '../src/FixtureWorkspace';
import {useAdminRoute} from '../src/admin-route';
vi.mock('../src/api',()=>({api:async()=>({programs:[{id:'a',name:'Singles',status:'A',sport:'Badminton',entryType:'S'},{id:'b',name:'Doubles',status:'A',sport:'Badminton',entryType:'D'}],plans:[],entries:[],eventInfo:{startDate:'2026-10-28',endDate:'2026-10-30'}})}));
vi.mock('../src/SeedingPage',()=>({default:()=> <p>Seeding content</p>}));
vi.mock('../src/TournamentConsole',()=>({default:()=> <p>Draw content</p>}));
vi.mock('../src/EventSchedule',()=>({default:()=> <p>Schedule content</p>}));
function Harness(){const {route,go}=useAdminRoute();return <FixtureWorkspace eventId="e" programId={route.programId} initialStep={route.step} onBack={()=>{}} onChanged={async()=>{}} onPresets={()=>{}} onContextChange={(programId,step)=>go({...route,programId,step},!route.programId||!route.step)}/>;}
afterEach(()=>{cleanup();history.replaceState(null,'','/');});
test('real fixture workspace restores program and step through browser history',async()=>{
 history.replaceState(null,'','/fixtures/e?program=a&step=draw');render(<Harness/>);await screen.findByText('Draw content');
 fireEvent.click(screen.getByRole('button',{name:/3\. Schedule/}));await screen.findByText('Schedule content');expect(new URLSearchParams(location.search).get('step')).toBe('schedule');
 history.back();await screen.findByText('Draw content');await waitFor(()=>expect(new URLSearchParams(location.search).get('step')).toBe('draw'));
 fireEvent.change(screen.getByLabelText('Planning program'),{target:{value:'b'}});await screen.findByText('Seeding content');expect(new URLSearchParams(location.search).get('program')).toBe('b');
 history.back();await screen.findByText('Draw content');expect((screen.getByLabelText('Planning program') as HTMLSelectElement).value).toBe('a');
});
