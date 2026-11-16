import {afterEach,expect,test,vi} from 'vitest';
import {act,cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react';
import LiveDisplay,{defaultDisplay} from '../src/LiveDisplay';
import LiveDisplayConfig from '../src/LiveDisplayConfig';
import ScoreProgression from '../src/ScoreProgression';
import type {LiveMatch} from '../src/live-types';
const service=vi.hoisted(()=>({liveApi:vi.fn()}));vi.mock('../src/live-api',()=>service);
afterEach(()=>{cleanup();vi.useRealTimers();service.liveApi.mockReset();});
const matches=Array.from({length:3},(_,i)=>({id:String(i),number:i+1,court:i+1,eventName:'Event',programName:'Singles',roundLabel:'Final',nameA:'Player A'+i,nameB:'Player B'+i,status:'IP',scoreA:12,scoreB:10,games:[{a:21,b:18}],serverSide:'A',matchWon:false}));
test('live display paginates at the configured interval and keeps scores aligned',async()=>{
 vi.useFakeTimers();service.liveApi.mockResolvedValue({config:{...defaultDisplay,columns:1,rows:2,pageSeconds:3},matches});
 render(<LiveDisplay/>);await act(async()=>{});
 expect(screen.getByText('Player A0')).toBeTruthy();expect(screen.queryByText('Player A2')).toBeNull();
 expect(screen.getAllByLabelText('Game 1 side A: 21')).toHaveLength(2);
 await act(async()=>{await vi.advanceTimersByTimeAsync(3000);});
 expect(screen.getByText('Page 2 / 2')).toBeTruthy();expect(screen.getByText('Player A2')).toBeTruthy();
 fireEvent.click(screen.getByRole('button',{name:'Previous'}));expect(screen.getByText('Player A0')).toBeTruthy();
});
test('pages advance automatically even with a legacy disabled rotation setting',async()=>{
 vi.useFakeTimers();service.liveApi.mockResolvedValue({config:{...defaultDisplay,columns:1,rows:1,autoRotate:false},matches});
 render(<LiveDisplay/>);await act(async()=>{});fireEvent.click(screen.getByRole('button',{name:'Next'}));
 await act(async()=>{await vi.advanceTimersByTimeAsync(20000);});expect(screen.getByText('Player A0')).toBeTruthy();
});
test('connection failure marks retained scores stale and pauses rotation',async()=>{
 vi.useFakeTimers();service.liveApi.mockResolvedValueOnce({config:{...defaultDisplay,columns:1,rows:1,pageSeconds:3},matches}).mockRejectedValue(new Error('Offline'));
 render(<LiveDisplay/>);await act(async()=>{});await act(async()=>{await vi.advanceTimersByTimeAsync(2500);});
 expect(screen.getByRole('alert').textContent).toContain('Connection lost');
 await act(async()=>{await vi.advanceTimersByTimeAsync(5000);});expect(screen.getByText('Player A0')).toBeTruthy();
});
test('score progression separates games and does not connect manual score gaps',()=>{
 const match={nameA:'Alice',nameB:'Bob',rules:{pointsToWin:21},rallyHistoryComplete:false,resumptions:[],rallies:[{game:1,beforeA:0,beforeB:0,a:1,b:0},{game:1,beforeA:10,beforeB:7,a:11,b:7},{game:2,beforeA:0,beforeB:0,a:0,b:1}]} as unknown as LiveMatch;
 render(<ScoreProgression match={match}/>);
 expect(screen.getAllByRole('img')).toHaveLength(2);expect(screen.getByRole('img',{name:'Game 1 points by recorded rally'}).querySelectorAll('path')).toHaveLength(4);
 expect(screen.getByText(/missing rallies are not reconstructed/)).toBeTruthy();
});

test('master display settings use row-first layouts and row-level saves without filters or preview',async()=>{
 const save=vi.fn().mockResolvedValue(true);
 render(<LiveDisplayConfig value={{...defaultDisplay,eventId:'old-event',courts:[1],autoRotate:false}} busy={false} onSave={save}/>);
 expect(screen.queryByLabelText('Display layout preview')).toBeNull();expect(screen.queryByText('Rotate pages automatically')).toBeNull();expect(screen.queryByLabelText('Display event')).toBeNull();
 fireEvent.click(screen.getByRole('button',{name:'Edit Layout (rows × columns)'}));
 fireEvent.change(screen.getByLabelText('Layout (rows × columns)'),{target:{value:'3x4'}});
 fireEvent.click(screen.getByRole('button',{name:'Save Layout (rows × columns)'}));
 await waitFor(()=>expect(save).toHaveBeenCalledWith({...defaultDisplay,rows:3,columns:4}));
 await waitFor(()=>expect(screen.queryByRole('button',{name:'Save Layout (rows × columns)'})).toBeNull());
 fireEvent.click(screen.getByRole('button',{name:'Edit Seconds per page'}));
 fireEvent.change(screen.getByLabelText('Seconds per page'),{target:{value:'12'}});
 fireEvent.click(screen.getByRole('button',{name:'Save Seconds per page'}));
 await waitFor(()=>expect(save).toHaveBeenLastCalledWith({...defaultDisplay,pageSeconds:12}));
});

test('score progression explains an empty rally history',()=>{
 render(<ScoreProgression match={{nameA:'Alice',nameB:'Bob',rules:{pointsToWin:21},rallies:[],rallyHistoryComplete:true} as unknown as LiveMatch}/>);
 expect(screen.getByText('No recorded rallies yet.')).toBeTruthy();expect(screen.queryByRole('img')).toBeNull();
});

test('confirmed cards show the saved winner and final games without duplicate current points',async()=>{
 service.liveApi.mockResolvedValue({config:defaultDisplay,matches:[{...matches[0],status:'C',winner:'B',matchWon:true,games:[{a:21,b:18},{a:21,b:19}]}]});
 const {container}=render(<LiveDisplay/>);
 await screen.findByText('Finished');
 expect(container.querySelector('.display-winner')?.textContent).toContain('Player B0');
 expect(container.querySelector('.display-winner-label')?.textContent).toBe('Winner');
 expect(screen.getByLabelText('Game 2 side A: 21')).toBeTruthy();
 expect(screen.queryByText('Points')).toBeNull();
 expect(container.querySelector('.display-current')).toBeNull();
 expect(container.querySelector('.display-serving')).toBeNull();
});

test('walkover cards identify the winner without displaying invented zero scores',async()=>{
 service.liveApi.mockResolvedValue({config:defaultDisplay,matches:[{...matches[0],status:'WO',winner:'A',games:[],scoreA:0,scoreB:0}]});
 const {container}=render(<LiveDisplay/>);await screen.findByText('Finished \u00b7 Walkover');
 expect(container.querySelector('.display-winner')?.textContent).toContain('Player A0');
 expect(container.querySelector('.display-current,.display-previous')).toBeNull();
});

test('polling replaces a finished card with active play then clears it on reset',async()=>{
 vi.useFakeTimers();service.liveApi.mockResolvedValueOnce({config:defaultDisplay,matches:[{...matches[0],status:'C',winner:'A'}]})
 .mockResolvedValueOnce({config:defaultDisplay,matches:[{...matches[0],id:'new',nameA:'Next Player'}]})
 .mockResolvedValue({config:defaultDisplay,matches:[]});
 const {container}=render(<LiveDisplay/>);await act(async()=>{});
 expect(screen.getByText('Winner')).toBeTruthy();
 await act(async()=>{await vi.advanceTimersByTimeAsync(2000);});
 expect(screen.getByText('Next Player')).toBeTruthy();expect(screen.queryByText('Finished')).toBeNull();expect(screen.queryByText('Winner')).toBeNull();
 expect(container.querySelector('.display-current')).toBeTruthy();
 await act(async()=>{await vi.advanceTimersByTimeAsync(2000);});
 expect(screen.getByText('Waiting for matches to start')).toBeTruthy();expect(screen.queryByText('Next Player')).toBeNull();
});

test('finished cards remain paginated and reconnect clears retained results after a reset',async()=>{
 vi.useFakeTimers();service.liveApi.mockResolvedValueOnce({config:{...defaultDisplay,columns:1,rows:1},matches:matches.map(m=>({...m,status:'C',winner:'A'}))})
 .mockRejectedValueOnce(new Error('Offline')).mockResolvedValue({config:defaultDisplay,matches:[]});
 render(<LiveDisplay/>);await act(async()=>{});
 expect(screen.getByText('Page 1 / 3')).toBeTruthy();fireEvent.click(screen.getByRole('button',{name:'Next'}));expect(screen.getByText('Player A1')).toBeTruthy();
 await act(async()=>{await vi.advanceTimersByTimeAsync(2000);});expect(screen.getByRole('alert').textContent).toContain('Connection lost');expect(screen.getByText('Winner')).toBeTruthy();
 await act(async()=>{await vi.advanceTimersByTimeAsync(2000);});expect(screen.queryByRole('alert')).toBeNull();expect(screen.queryByText('Winner')).toBeNull();expect(screen.getByText('Page 1 / 1')).toBeTruthy();
});
