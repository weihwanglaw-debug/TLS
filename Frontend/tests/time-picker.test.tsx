import {useState} from 'react';
import {cleanup,fireEvent,render,screen} from '@testing-library/react';
import {afterEach,expect,test,vi} from 'vitest';
import TimePicker from '../src/TimePicker';
afterEach(cleanup);
function Example({value='10:00',save=()=>{}}:{value?:string;save?:()=>void}){
 const [time,setTime]=useState(value);
 return <form onSubmit={save}><TimePicker label="Session start" value={time} onChange={setTime}/><output>{time}</output></form>;
}
test('selects noon and arbitrary minutes without submitting the event form',()=>{
 const save=vi.fn();render(<Example save={save}/>);
 fireEvent.click(screen.getByRole('button',{name:'Session start'}));
 fireEvent.click(screen.getByRole('button',{name:'12',exact:true}));
 fireEvent.click(screen.getByRole('button',{name:'PM',exact:true}));
 fireEvent.change(screen.getByLabelText('Minute'),{target:{value:'07'}});
 fireEvent.click(screen.getByRole('button',{name:/^Set /i}));
 expect(screen.getByText('12:07')).toBeTruthy();expect(save).not.toHaveBeenCalled();
});
test('midnight converts to 00 and selected quick minutes are highlighted',()=>{
 render(<Example value="23:59"/>);
 fireEvent.click(screen.getByRole('button',{name:'Session start'}));
 fireEvent.click(screen.getByRole('button',{name:'12',exact:true}));
 fireEvent.click(screen.getByRole('button',{name:'AM',exact:true}));
 fireEvent.click(screen.getByRole('button',{name:'Minute 00'}));
 expect(screen.getByRole('button',{name:'Minute 00'}).getAttribute('aria-pressed')).toBe('true');
 fireEvent.click(screen.getByRole('button',{name:/^Set /i}));
 expect(screen.getByText('00:00')).toBeTruthy();
});
test('cancel retains the saved value and invalid minutes cannot be confirmed',()=>{
 render(<Example/>);fireEvent.click(screen.getByRole('button',{name:'Session start'}));
 fireEvent.change(screen.getByLabelText('Minute'),{target:{value:'60'}});
 expect(screen.getByRole('button',{name:/^Set /i}).disabled).toBe(true);
 fireEvent.click(screen.getByRole('button',{name:'Cancel',exact:true}));
 expect(screen.getByText('10:00')).toBeTruthy();
});

test.each([['11:59','Increase time by one minute','12:00','12:00 PM'],['23:59','Increase time by one minute','00:00','12:00 AM'],['00:00','Decrease time by one minute','23:59','11:59 PM'],['12:00','Decrease time by one minute','11:59','11:59 AM']])('one-minute adjustment from %s carries hour and period correctly',(value,action,expected,label)=>{
 const save=vi.fn();render(<Example value={value} save={save}/>);fireEvent.click(screen.getByRole('button',{name:'Session start'}));fireEvent.click(screen.getByRole('button',{name:action}));
 expect(screen.getByRole('button',{name:'Set '+label})).toBeTruthy();expect(screen.getByRole('status',{hidden:true}).textContent).toBe(value);
 fireEvent.click(screen.getByRole('button',{name:'Set '+label}));expect(screen.getByRole('status',{hidden:true}).textContent).toBe(expected);expect(save).not.toHaveBeenCalled();
});
test('cancel discards minute stepping and reopening restores the field value',()=>{
 render(<Example value="14:07"/>);fireEvent.click(screen.getByRole('button',{name:'Session start'}));fireEvent.click(screen.getByRole('button',{name:'Increase time by one minute'}));fireEvent.click(screen.getByRole('button',{name:'Cancel',exact:true}));
 expect(screen.getByRole('status',{hidden:true}).textContent).toBe('14:07');fireEvent.click(screen.getByRole('button',{name:'Session start'}));expect(screen.getByRole('button',{name:'Set 2:07 PM'})).toBeTruthy();
});
test('start and end pickers operate independently even while the start is later than the end',()=>{
 function Pair(){const [start,setStart]=useState('10:00'),[end,setEnd]=useState('12:00');return <><TimePicker label="Start" value={start} onChange={setStart}/><TimePicker label="End" value={end} onChange={setEnd}/><output>{start+' / '+end}</output></>;}
 render(<Pair/>);fireEvent.click(screen.getByRole('button',{name:'Start',exact:true}));fireEvent.click(screen.getByRole('button',{name:'3',exact:true}));fireEvent.click(screen.getByRole('button',{name:'PM',exact:true}));fireEvent.click(screen.getByRole('button',{name:'Set 3:00 PM'}));
 expect(screen.getByRole('status',{hidden:true}).textContent).toBe('15:00 / 12:00');
 fireEvent.click(screen.getByRole('button',{name:'End',exact:true}));fireEvent.click(screen.getByRole('button',{name:'Increase time by one minute'}));fireEvent.click(screen.getByRole('button',{name:'Set 12:01 PM'}));expect(screen.getByRole('status',{hidden:true}).textContent).toBe('15:00 / 12:01');
});
