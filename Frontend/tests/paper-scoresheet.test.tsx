import {afterEach,expect,test,vi} from 'vitest';
import {cleanup,fireEvent,render,screen,within} from '@testing-library/react';
import PaperScoresheet from '../src/PaperScoresheet';
import ResumeTabletForm from '../src/ResumeTabletForm';
import type {LiveMatch} from '../src/live-types';
afterEach(cleanup);
const match=()=>({id:'m',number:3,eventName:'Community Badminton',programName:"Men's Singles",roundLabel:'Round of 16',court:2,umpireName:'Jane',games:[],scoreA:5,scoreB:4,gamesWonA:0,gamesWonB:0,status:'IP',paperMode:true,version:4,rules:{gamesToWin:2,pointCap:30,pointsToWin:21},players:[{id:'a',name:'Player A',side:'A'},{id:'b',name:'Player B',side:'B'}],nameA:'Player A',nameB:'Player B',serverId:'a',receiverId:'b',serving:{serverSide:'A',leftSide:'A'},rallyHistoryComplete:true,rallies:[{game:1,beforeA:0,beforeB:0,a:1,b:0,winnerSide:'A',serverId:'a',receiverId:'b',leftSide:'A',at:'2026-10-26T07:00:00Z'},{game:1,beforeA:1,beforeB:0,a:1,b:1,winnerSide:'B',serverId:'a',receiverId:'b',leftSide:'A',at:'2026-10-26T07:00:10Z'}],startedAt:'2026-10-26T07:00:00Z',finishedAt:null,resultDetails:{cards:[],challenges:[],shuttlesUsed:0}} as unknown as LiveMatch);
test('paper scoresheet uses horizontal player bands and rally cells instead of a record table',()=>{
 render(<PaperScoresheet match={match()}/>);
 expect(screen.queryByRole('table')).toBeNull();expect(screen.getByLabelText('Badminton paper scoresheet')).toBeTruthy();
 const grid=screen.getByLabelText('Game 1 scoring grid');expect(grid.querySelectorAll('.paper-score-band')).toHaveLength(2);
 expect(within(grid).getByLabelText('Rally 1: 1–0, side A: 1').textContent).toBe('1');expect(within(grid).getByLabelText('Rally 1: 1–0, side B').textContent).toBe('');
 expect(screen.getByText('Community Badminton')).toBeTruthy();expect(screen.getByText('Jane')).toBeTruthy();
});
test('doubles includes four player names; long games continue on additional strips',()=>{
 const m=match();m.players.push({id:'a2',name:'Partner A',side:'A'},{id:'b2',name:'Partner B',side:'B'});
 m.rallies=Array.from({length:59},(_,i)=>({...m.rallies[0],beforeA:Math.floor(i/2),beforeB:Math.floor(i/2),a:Math.ceil((i+1)/2),b:Math.floor((i+1)/2),winnerSide:i%2?'B':'A'}));
 render(<PaperScoresheet match={m}/>);expect(screen.getAllByText('Partner A').length).toBeGreaterThan(1);expect(screen.getAllByText('Partner B').length).toBeGreaterThan(1);expect(document.querySelectorAll('.paper-strip')).toHaveLength(3);
});
test('resumption is labelled without inventing the missing rallies, and completed matches have no extra game',()=>{
 const m=match();m.resumptions=[{game:1,a:12,b:10,rallyIndex:2,at:'2026-10-26T07:10:00Z'}];m.rallyHistoryComplete=false;
 const page=render(<PaperScoresheet match={m}/>);expect(screen.getByLabelText('Resumed at 12–10, side A: 12').textContent).toContain('12');expect(screen.getByText(/preceding paper rallies not reconstructed/)).toBeTruthy();
 m.games=[{a:21,b:10},{a:21,b:15}];m.matchWon=true;page.rerender(<PaperScoresheet match={m}/>);expect(screen.queryByLabelText('Game 3 scoring grid')).toBeNull();
});
test('resume form sends corrected scores and confirmed doubles positions while fixing court and umpire',()=>{
 const m=match();m.players.push({id:'a2',name:'Partner A',side:'A'},{id:'b2',name:'Partner B',side:'B'});const resume=vi.fn();render(<ResumeTabletForm match={m} busy={false} onResume={resume} onCancel={()=>{}}/>);
 expect(screen.getByText('Court 2 · Umpire: Jane')).toBeTruthy();
 fireEvent.change(screen.getByLabelText('Current score A'),{target:{value:'12'}});fireEvent.change(screen.getByLabelText('Current score B'),{target:{value:'10'}});fireEvent.change(screen.getByLabelText('Resume server'),{target:{value:'a2'}});fireEvent.change(screen.getByLabelText('Resume receiver'),{target:{value:'b2'}});
 fireEvent.click(screen.getByRole('button',{name:'Save & Generate Access Code'}));expect(resume).toHaveBeenCalledWith({games:[],scoreA:12,scoreB:10,serving:{serverSide:'A',serverId:'a2',receiverId:'b2',leftSide:'A'}});
});
test('between-game resume defaults to zero and retains completed games; busy form cannot submit',()=>{
 const m=match();m.games=[{a:21,b:15}];m.awaitingNextGame=true;const resume=vi.fn();render(<ResumeTabletForm match={m} busy={true} onResume={resume} onCancel={()=>{}}/>);
 expect((screen.getByLabelText('Current score A') as HTMLInputElement).value).toBe('0');expect((screen.getByLabelText('Current score B') as HTMLInputElement).value).toBe('0');expect((screen.getByRole('button',{name:'Remove Last Game'}) as HTMLButtonElement).disabled).toBe(true);fireEvent.click(screen.getByRole('button',{name:'Saving...'}));expect(resume).not.toHaveBeenCalled();
});

test('wrapped game is explicitly continued and has totals only in the final block',()=>{
 const m=match();m.games=[{a:6,b:21}];m.awaitingNextGame=true;
 let a=0,b=0;m.rallies=Array.from({length:27},(_,i)=>{const beforeA=a,beforeB=b;const winner=i<6?'A':'B';if(winner==='A')a++;else b++;return {...m.rallies[0],game:1,beforeA,beforeB,a,b,winnerSide:winner};});
 render(<PaperScoresheet match={m}/>);const grid=screen.getByLabelText('Game 1 scoring grid'),blocks=grid.querySelectorAll('.paper-strip-scroll');
 expect(blocks).toHaveLength(2);expect(blocks[0].textContent).toContain('continues below');expect(blocks[1].textContent).toContain('Game 1 — continued');
 expect(blocks[0].querySelectorAll('.paper-strip-total')).toHaveLength(0);expect(blocks[1].querySelectorAll('.paper-strip-total')).toHaveLength(2);
 expect(within(grid).getByLabelText('Game 1 side A final score').textContent).toBe('6');
 const winner=within(grid).getByLabelText('Game 1 side B final score, game winner');expect(winner.textContent).toBe('21');expect(winner.classList.contains('paper-game-winner')).toBe(true);
 expect([...grid.querySelectorAll('.paper-player-names.paper-game-winner')].every(n=>n.textContent==='Player B')).toBe(true);
});
test('shuttle usage sits in match metadata and winner is marked in the correct side panel',()=>{
 const m=match();m.games=[{a:21,b:10},{a:14,b:21},{a:19,b:21}];m.matchWon=true;m.resultDetails.shuttlesUsed=2;
 render(<PaperScoresheet match={m}/>);expect(document.querySelector('.paper-sheet-meta')!.textContent).toContain('Shuttles used: 2');
 expect(within(screen.getByLabelText('Side B players')).getByText('Winner')).toBeTruthy();expect(within(screen.getByLabelText('Side A players')).queryByText('Winner')).toBeNull();expect(document.querySelector('.paper-sheet>footer')).toBeNull();
 const game1=screen.getByLabelText('Game 1 scoring grid');expect(within(game1).getByLabelText('Game 1 side A final score, game winner')).toBeTruthy();
});
test('unfinished game has no game or match winner highlight and preserves zero shuttles',()=>{
 render(<PaperScoresheet match={match()}/>);expect(screen.queryByText('Winner')).toBeNull();expect(document.querySelectorAll('.paper-game-winner')).toHaveLength(0);
 expect(document.querySelector('.paper-sheet-meta')!.textContent).toContain('Shuttles used: 0');expect(screen.getByLabelText('Game 1 side A current score').textContent).toBe('5');
});
test('a 59-rally doubles game wraps three times, preserves both names, and highlights side A',()=>{
 const m=match();m.players.push({id:'a2',name:'Partner A',side:'A'},{id:'b2',name:'Partner B',side:'B'});m.games=[{a:30,b:29},{a:21,b:15}];m.matchWon=true;
 let a=0,b=0;m.rallies=Array.from({length:59},(_,i)=>{const beforeA=a,beforeB=b;const winner=i%2?'B':'A';if(winner==='A')a++;else b++;return {...m.rallies[0],game:1,beforeA,beforeB,a,b,winnerSide:winner};});
 render(<PaperScoresheet match={m}/>);const grid=screen.getByLabelText('Game 1 scoring grid');expect(grid.querySelectorAll('.paper-strip')).toHaveLength(3);expect(grid.querySelectorAll('.paper-strip-total')).toHaveLength(2);expect(within(grid).getAllByText(/Game 1 — continued/)).toHaveLength(2);expect(within(screen.getByLabelText('Side A players')).getByText('Winner')).toBeTruthy();
 expect(grid.querySelectorAll('.paper-player-names.paper-game-winner')).toHaveLength(3);expect(grid.querySelector('.paper-player-names.paper-game-winner')!.textContent).toContain('Partner A');
});
