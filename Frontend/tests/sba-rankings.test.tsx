import {cleanup,fireEvent,render,screen,waitFor,within} from '@testing-library/react';
import {afterEach,beforeEach,expect,test,vi} from 'vitest';
const api=vi.hoisted(()=>({apiGetSbaRankings:vi.fn(),apiGetSbaRankingTypes:vi.fn(),apiImportSbaRankings:vi.fn()}));
vi.mock('../src/lib/api',()=>api);
import SbaRankings from '../src/SbaRankings';
// Synthetic component inputs only; runtime rankings are SQL-backed.
const ranks=[{id:1,rankingType:"Men's Singles",ranking:2,accumulatedScore:400,tournaments:2,player1:{name:'Ranking Player',sbaId:'SBA001',club:'Ranking Club',dob:'1991-01-10'},player2:null},{id:2,rankingType:"Men's Doubles",ranking:1,accumulatedScore:600,tournaments:4,player1:{name:'Partner One',sbaId:'SBA002',club:'Pair Club',dob:''},player2:{name:'Partner Two',sbaId:'SBA003',club:'Pair Club',dob:''}}];
afterEach(cleanup);
beforeEach(()=>{vi.resetAllMocks();api.apiGetSbaRankings.mockResolvedValue({data:ranks,error:null});api.apiGetSbaRankingTypes.mockResolvedValue({data:[{value:"Men's Singles",label:"Men's Singles"},{value:"Men's Doubles",label:"Men's Doubles"}],error:null});});
test('standalone TRS ranking grid searches either partner and sorts scores',async()=>{
 render(<SbaRankings/>);await screen.findAllByText('Ranking Player');
 const table=within(screen.getByRole('table'));
 expect(table.getAllByRole('row')).toHaveLength(3);
 fireEvent.click(table.getByText('Score'));expect(table.getAllByRole('row')[1].textContent).toContain('Ranking Player');
 fireEvent.click(table.getByText('Score'));expect(table.getAllByRole('row')[1].textContent).toContain('Partner One');
 fireEvent.change(screen.getByPlaceholderText('Search SBA ID or player name...'),{target:{value:'SBA003'}});
 expect(table.queryByText('Ranking Player')).toBeNull();expect(table.getByText('Partner Two')).toBeTruthy();
 fireEvent.change(screen.getByRole('combobox'),{target:{value:"Men's Singles"}});
 await waitFor(()=>expect(api.apiGetSbaRankings).toHaveBeenLastCalledWith({type:"Men's Singles"}));
 expect((screen.getByPlaceholderText('Search SBA ID or player name...') as HTMLInputElement).value).toBe('');
});
test('workbook upload shows the unchanged TRS import summary and alert',async()=>{
 api.apiImportSbaRankings.mockResolvedValue({data:{importedRows:3,categories:[{rankingType:"Men's Singles",rows:3}],addedClubs:1,addedClubNames:['New Club'],skippedSheets:['Notes']},error:null});
 const {container}=render(<SbaRankings/>);await screen.findAllByText('Ranking Player');
 const file=new File(['test workbook'],'rankings.xlsx');fireEvent.change(container.querySelector('input[type="file"]')!,{target:{files:[file]}});
 await screen.findByRole('dialog',{name:'Import completed'});expect(api.apiImportSbaRankings).toHaveBeenCalledWith(file);
 expect(screen.getByText('Replaced list with 3 rows and added 1 clubs.')).toBeTruthy();
 fireEvent.click(screen.getByRole('button',{name:'OK'}));
 expect(screen.getByText('Import successful - 3 rows imported as a fresh list')).toBeTruthy();expect(screen.getByText('Added clubs: New Club')).toBeTruthy();expect(screen.getByText('Skipped sheets: Notes')).toBeTruthy();
});
test('failed import retains the list and presents the TRS error popup',async()=>{
 api.apiImportSbaRankings.mockResolvedValue({data:null,error:{message:'SBA workbook contains invalid data.'}});
 const {container}=render(<SbaRankings/>);await screen.findAllByText('Ranking Player');
 fireEvent.change(container.querySelector('input[type="file"]')!,{target:{files:[new File(['bad'],'rankings.xlsx')]}});
 await screen.findByRole('dialog',{name:'Import failed'});expect(screen.getByText('SBA workbook contains invalid data.')).toBeTruthy();
 fireEvent.click(screen.getByRole('button',{name:'OK'}));expect(within(screen.getByRole('table')).getByText('Ranking Player')).toBeTruthy();
});
