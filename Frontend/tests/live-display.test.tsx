import {afterEach,expect,test,vi} from 'vitest';
import {act,cleanup,fireEvent,render,screen} from '@testing-library/react';
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
test('manual pages stay selected when rotation is disabled',async()=>{
 vi.useFakeTimers();service.liveApi.mockResolvedValue({config:{...defaultDisplay,columns:1,rows:1,autoRotate:false},matches});
 render(<LiveDisplay/>);await act(async()=>{});fireEvent.click(screen.getByRole('button',{name:'Next'}));
 await act(async()=>{await vi.advanceTimersByTimeAsync(20000);});expect(screen.getByText('Player A1')).toBeTruthy();
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

test('master display configuration previews and saves grid, event and courts',()=>{
 const save=vi.fn().mockResolvedValue(undefined);
 render(<LiveDisplayConfig value={defaultDisplay} events={[{id:'E',name:'Tournament'}]} courtCount={4} busy={false} onSave={save}/>);
 fireEvent.change(screen.getByLabelText('Display columns'),{target:{value:'4'}});fireEvent.change(screen.getByLabelText('Display rows'),{target:{value:'4'}});
 fireEvent.change(screen.getByLabelText('Seconds per page'),{target:{value:'12'}});fireEvent.change(screen.getByLabelText('Display event'),{target:{value:'E'}});
 fireEvent.click(screen.getByLabelText('Court 2'));fireEvent.click(screen.getByRole('button',{name:'Save Live Display'}));
 expect(save).toHaveBeenCalledWith({...defaultDisplay,columns:4,rows:4,pageSeconds:12,eventId:'E',courts:[2]});
 expect(screen.getByLabelText('Display layout preview').children).toHaveLength(16);
 expect(screen.getByRole('link',{name:'Open Live Display'}).getAttribute('href')).toBe('/display');
});
test('score progression explains an empty rally history',()=>{
 render(<ScoreProgression match={{nameA:'Alice',nameB:'Bob',rules:{pointsToWin:21},rallies:[],rallyHistoryComplete:true} as unknown as LiveMatch}/>);
 expect(screen.getByText('No recorded rallies yet.')).toBeTruthy();expect(screen.queryByRole('img')).toBeNull();
});
