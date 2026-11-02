import {useState} from 'react';
import {cleanup,fireEvent,render,screen,within} from '@testing-library/react';
import {afterEach,expect,test} from 'vitest';
import OfficialsEditor,{officialRows,officialAssignment,officialsError,type OfficialRow} from '../src/OfficialsEditor';
afterEach(cleanup);
const officials=[{id:'u',name:'Peter'},{id:'s',name:'Jane'},{id:'l1',name:'David'},{id:'l2',name:'Ann'}];
function Editor(){const [rows,setRows]=useState<OfficialRow[]>(officialRows({umpireId:'u'}));return <><OfficialsEditor rows={rows} officials={officials} onChange={setRows} busy={id=>id==='l2'?' · Court 4':undefined}/><output>{JSON.stringify(officialAssignment(rows))}</output></>;}
test('adds optional roles, prevents duplicate selection, and removes a row',()=>{
 render(<Editor/>);fireEvent.click(screen.getByRole('button',{name:'+ Add official'}));
 expect((screen.getByLabelText('Position 2') as HTMLSelectElement).value).toBe('S');
 expect((within(screen.getByLabelText('Official 2')).getByRole('option',{name:'Peter · Already selected'}) as HTMLOptionElement).disabled).toBe(true);
 expect((within(screen.getByLabelText('Official 2')).getByRole('option',{name:'Ann · Busy · Court 4'}) as HTMLOptionElement).disabled).toBe(true);
 fireEvent.change(screen.getByLabelText('Official 2'),{target:{value:'s'}});fireEvent.click(screen.getByRole('button',{name:'+ Add official'}));
 expect((screen.getByLabelText('Position 3') as HTMLSelectElement).value).toBe('L');fireEvent.change(screen.getByLabelText('Official 3'),{target:{value:'l1'}});
 expect(JSON.parse(screen.getByRole('status').textContent!)).toEqual({umpireId:'u',serviceJudgeId:'s',lineJudgeIds:['l1']});
 fireEvent.click(screen.getByRole('button',{name:'Remove official 2'}));expect(JSON.parse(screen.getByRole('status').textContent!).serviceJudgeId).toBeNull();
});
test('saved roles round trip and invalid assignments are rejected',()=>{
 const saved={umpireId:'u',serviceJudgeId:'s',lineJudgeIds:['l1','l2']};const rows=officialRows(saved);expect(officialAssignment(rows)).toEqual(saved);expect(officialsError(rows,officials)).toBe('');
 expect(officialsError(rows,officials,id=>id==='l1'?'Busy':undefined)).toContain('busy');
 expect(officialsError([{officialId:'u',position:'U'},{officialId:'u',position:'L'}],officials)).toContain('only one position');
 expect(officialsError([{officialId:'u',position:'L'}],officials)).toContain('exactly one umpire');
 expect(officialsError([...rows,{officialId:'l2',position:'S'}],officials)).toContain('at most one service judge');
});
test('tablet replacement disables all role editing',()=>{
 render(<OfficialsEditor rows={officialRows({umpireId:'u',serviceJudgeId:'s'})} officials={officials} disabled onChange={()=>{}}/>);
 expect((screen.getByLabelText('Official 1').closest('fieldset') as HTMLFieldSetElement).disabled).toBe(true);
});
