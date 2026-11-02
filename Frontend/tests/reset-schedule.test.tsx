import {cleanup,fireEvent,render,screen} from '@testing-library/react';
import {afterEach,beforeEach,expect,test,vi} from 'vitest';
const service=vi.hoisted(()=>({api:vi.fn()}));vi.mock('../src/api',()=>service);
import ResetScheduleDialog from '../src/ResetScheduleDialog';
const data={revision:10,rounds:[{key:'a:K:1',name:'A · Final',reason:''},{key:'b:K:1',name:'B · Final',reason:'A match has started or recorded a result'},{key:'c:K:1',name:'C · Final',reason:'Release waiting tablet assignments first'}]};
afterEach(cleanup);beforeEach(()=>{vi.resetAllMocks();service.api.mockImplementation(async(path:string)=>path.endsWith('/reset-schedule')&&path!=='tournament/reset-schedule'?structuredClone(data):{});});
test('Select All includes only eligible rounds and confirmation is the only write',async()=>{
 const done=vi.fn();render(<ResetScheduleDialog eventId="event" onClose={()=>{}} onDone={done}/>);await screen.findByText('A · Final');
 expect((screen.getByRole('switch',{name:'B · Final'}) as HTMLButtonElement).disabled).toBe(true);expect(screen.getByText('Release waiting tablet assignments first')).toBeTruthy();
 fireEvent.click(screen.getByRole('switch',{name:'Select All Eligible Rounds'}));expect(service.api).toHaveBeenCalledTimes(1);
 fireEvent.click(screen.getByRole('button',{name:'Confirm Reset Schedule'}));await vi.waitFor(()=>expect(done).toHaveBeenCalledOnce());
 expect(service.api).toHaveBeenLastCalledWith('tournament/reset-schedule','POST',{revision:10,eventId:'event',roundKeys:['a:K:1']});
});
test('Cancel makes no reset request',async()=>{
 const close=vi.fn();render(<ResetScheduleDialog eventId="event" onClose={close} onDone={async()=>{}}/>);await screen.findByText('A · Final');fireEvent.click(screen.getByRole('button',{name:'Cancel'}));expect(close).toHaveBeenCalledOnce();expect(service.api).toHaveBeenCalledTimes(1);
});
test('a stale reset refreshes eligibility and requires selection again',async()=>{
 service.api.mockImplementation(async(path:string)=>{if(path==='tournament/reset-schedule')throw new Error('Data changed in another window.');return structuredClone(data);});
 render(<ResetScheduleDialog eventId="event" onClose={()=>{}} onDone={async()=>{}}/>);await screen.findByText('A · Final');fireEvent.click(screen.getByRole('switch',{name:'Select All Eligible Rounds'}));fireEvent.click(screen.getByRole('button',{name:'Confirm Reset Schedule'}));await screen.findByText('Data changed in another window.');fireEvent.click(screen.getByRole('button',{name:'OK'}));await vi.waitFor(()=>expect((screen.getByRole('button',{name:'Confirm Reset Schedule'}) as HTMLButtonElement).disabled).toBe(true));expect(service.api.mock.calls.filter(c=>c[0]==='tournament/reset-schedule')).toHaveLength(1);
});
