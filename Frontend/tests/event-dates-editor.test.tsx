import {afterEach,expect,test,vi} from 'vitest';
import {cleanup,render,screen} from '@testing-library/react';
import EventDatesEditor from '../src/EventDatesEditor';
afterEach(cleanup);
test('date fields follow the shared edit mode',()=>{
 const props={start:'2026-12-01',end:'2026-12-02',editing:false,onStart:vi.fn(),onEnd:vi.fn()};
 const page=render(<EventDatesEditor {...props}/>);
 expect(screen.queryByRole('button',{name:'Edit Dates'})).toBeNull();expect(screen.queryByRole('button',{name:'Start Date',exact:true})).toBeNull();
 page.rerender(<EventDatesEditor {...props} editing/>);expect(screen.getByRole('button',{name:'Start Date',exact:true})).toBeTruthy();
 expect(screen.getByRole('button',{name:'End Date',exact:true})).toBeTruthy();
});
