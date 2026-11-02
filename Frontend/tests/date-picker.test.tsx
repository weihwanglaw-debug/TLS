import {useState} from 'react';
import {cleanup,fireEvent,render,screen} from '@testing-library/react';
import {afterEach,expect,test,vi} from 'vitest';
import DatePicker,{localDate,dateValue} from '../src/DatePicker';
import DateTimePicker from '../src/DateTimePicker';
import {Dialog,DialogContent,DialogTitle} from '../src/components/ui/dialog';
afterEach(cleanup);
function Example({initial='2026-10-19T10:00',disabled=false,save=()=>{}}:{initial?:string;disabled?:boolean;save?:()=>void}){const [value,setValue]=useState(initial);return <form onSubmit={e=>{e.preventDefault();save();}}><DateTimePicker label="Earliest start" required value={value} onChange={setValue} disabled={disabled}/><output>{value}</output><button>Save</button></form>;}
function day(n:number){const d=screen.getByRole('dialog');return [...d.querySelectorAll<HTMLButtonElement>('button[name="day"]')].find(b=>b.textContent===String(n)&&!b.classList.contains('day-outside'))!;}
test('calendar changes venue date without shifting time or submitting the fixture form',()=>{
 const save=vi.fn();render(<Example save={save}/>);fireEvent.click(screen.getByRole('button',{name:'Earliest start date'}));
 expect(day(19).getAttribute('aria-selected')).toBe('true');fireEvent.click(day(20));
 expect(screen.getByText('2026-10-20T10:00')).toBeTruthy();expect(save).not.toHaveBeenCalled();
 fireEvent.click(screen.getByRole('button',{name:'Earliest start time'}));fireEvent.click(screen.getByRole('button',{name:'12',exact:true}));fireEvent.click(screen.getByRole('button',{name:'AM',exact:true}));fireEvent.click(screen.getByRole('button',{name:/^Set /i}));
 expect(screen.getByText('2026-10-20T00:00')).toBeTruthy();expect(save).not.toHaveBeenCalled();
});
test('calendar enforces range, supports leap dates, cancel and clear',()=>{
 const change=vi.fn();render(<DatePicker label="Event date" value="2028-02-29" min="2028-02-28" max="2028-02-29" onChange={change}/>);
 fireEvent.click(screen.getByRole('button',{name:'Event date'}));expect(day(27).disabled).toBe(true);expect(day(29).disabled).toBe(false);
 fireEvent.click(screen.getByRole('button',{name:'Cancel'}));expect(change).not.toHaveBeenCalled();
 fireEvent.click(screen.getByRole('button',{name:'Event date'}));fireEvent.click(screen.getByRole('button',{name:'Clear Date'}));expect(change).toHaveBeenCalledWith('');
 expect(localDate('2027-02-29')).toBeUndefined();expect(dateValue(localDate('2028-02-29')!)).toBe('2028-02-29');
});
test('empty required date/time cannot submit; time can be entered before date',()=>{
 const save=vi.fn();const {container}=render(<Example initial="" save={save}/>);
 expect((container.querySelector('form') as HTMLFormElement).checkValidity()).toBe(false);
 fireEvent.click(screen.getByRole('button',{name:'Earliest start time'}));fireEvent.click(screen.getByRole('button',{name:/^Set /i}));
 expect(screen.getByText('T10:00')).toBeTruthy();expect((container.querySelector('form') as HTMLFormElement).checkValidity()).toBe(false);
 fireEvent.click(screen.getByRole('button',{name:'Earliest start date'}));fireEvent.click(day(19));
 expect((container.querySelector('form') as HTMLFormElement).checkValidity()).toBe(true);expect(save).not.toHaveBeenCalled();
});
test('locked fields cannot open and picker inside a modal preserves its parent',()=>{
 const page=render(<Example disabled/>);expect(screen.getByRole('button',{name:'Earliest start date'}).disabled).toBe(true);expect(screen.getByRole('button',{name:'Earliest start time'}).disabled).toBe(true);page.unmount();
 const close=vi.fn();render(<Dialog open onOpenChange={close}><DialogContent><DialogTitle>Result</DialogTitle><DatePicker label="Finish date" value="2026-10-19" onChange={()=>{}}/></DialogContent></Dialog>);
 fireEvent.click(screen.getByRole('button',{name:'Finish date'}));fireEvent.click(screen.getByRole('button',{name:'Cancel'}));expect(screen.getByRole('dialog',{name:'Result'})).toBeTruthy();expect(close).not.toHaveBeenCalled();
});

test('optional result times can be cleared and refreshed server values are displayed',()=>{
 const change=vi.fn();const page=render(<DateTimePicker label="Actual finish" value="2026-10-19T22:07" onChange={change}/>);
 fireEvent.click(screen.getByRole('button',{name:'Clear Actual finish'}));expect(change).toHaveBeenCalledWith('');
 page.rerender(<DateTimePicker label="Actual finish" value="2026-10-20T00:01" onChange={change}/>);
 expect(screen.getByText('20 Oct 2026')).toBeTruthy();expect(screen.getByText('12:01 AM')).toBeTruthy();
});

test('time stepping across midnight changes only the time part of a date-time field',()=>{
 render(<Example initial="2026-11-02T23:59"/>);fireEvent.click(screen.getByRole('button',{name:'Earliest start time'}));fireEvent.click(screen.getByRole('button',{name:'Increase time by one minute'}));fireEvent.click(screen.getByRole('button',{name:'Set 12:00 AM'}));expect(screen.getByText('2026-11-02T00:00')).toBeTruthy();
});
