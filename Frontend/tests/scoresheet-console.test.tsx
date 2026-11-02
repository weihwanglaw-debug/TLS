import {cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react';
import {afterEach,expect,test,vi} from 'vitest';
import ScoresheetButton from '../src/ScoresheetButton';
const service=vi.hoisted(()=>({liveApi:vi.fn()}));
vi.mock('../src/live-api',()=>service);
afterEach(()=>{cleanup();vi.restoreAllMocks();service.liveApi.mockReset();});
test('console retrieves the recorded match and prints the shared scoresheet',async()=>{
 service.liveApi.mockResolvedValue({id:'match-1',eventName:'Event',programName:'Singles',number:1,roundLabel:'Final',umpireName:'Jane',court:1,games:[{a:21,b:10},{a:21,b:12}],gamesWonA:2,gamesWonB:0,scoreA:21,scoreB:12,rules:{gamesToWin:2},players:[{id:'a',name:'Alice',side:'A'},{id:'b',name:'Bob',side:'B'}],nameA:'Alice',nameB:'Bob',matchWon:true,rallyHistoryComplete:true,rallies:[],startedAt:null,finishedAt:null});
 const print=vi.spyOn(window,'print').mockImplementation(()=>{});
 render(<ScoresheetButton matchId="match-1"/>);fireEvent.click(screen.getByRole('button',{name:'View Scoresheet'}));
 await screen.findByRole('article',{name:'Badminton paper scoresheet'});
 expect(service.liveApi).toHaveBeenCalledWith('dashboard/scoresheet/match-1');
 fireEvent.click(screen.getByRole('button',{name:'Print Scoresheet'}));expect(print).toHaveBeenCalledOnce();
});
test('a failed scoresheet fetch cannot print stale content',async()=>{
 service.liveApi.mockRejectedValue(new Error('Match not found.'));
 render(<ScoresheetButton matchId="missing"/>);fireEvent.click(screen.getByRole('button',{name:'View Scoresheet'}));
 await screen.findByRole('alert');await waitFor(()=>expect((screen.getByRole('button',{name:'Print Scoresheet'}) as HTMLButtonElement).disabled).toBe(true));
});
