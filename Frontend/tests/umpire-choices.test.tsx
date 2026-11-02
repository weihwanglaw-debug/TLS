import {cleanup,fireEvent,render,screen} from '@testing-library/react';
import {afterEach,expect,test,vi} from 'vitest';
import UmpireChoices from '../src/UmpireChoices';
afterEach(cleanup);
test('player choices select IDs and visibly identify the selection without dropdowns',()=>{
 const changed=vi.fn();
 const {container,rerender}=render(<UmpireChoices label="Server" value="one" options={[{value:'one',label:'Player One'},{value:'two',label:'Player Two'}]} onChange={changed}/>);
 expect(container.querySelector('select')).toBeNull();
 expect(screen.getByRole('button',{name:'Player One'}).getAttribute('aria-pressed')).toBe('true');
 fireEvent.click(screen.getByRole('button',{name:'Player Two'}));
 expect(changed).toHaveBeenCalledWith('two');
 rerender(<UmpireChoices label="Server" value="two" options={[{value:'one',label:'Player One'},{value:'two',label:'Player Two'}]} onChange={changed}/>);
 expect(screen.getByRole('button',{name:'Player Two'}).getAttribute('aria-pressed')).toBe('true');
});
test('single available player is displayed automatically without an unnecessary selector',()=>{
 render(<UmpireChoices label="Receiver" value="one" options={[{value:'one',label:'Only Receiver'}]} onChange={()=>{}}/>);
 expect(screen.getByText('Only Receiver')).toBeTruthy();
 expect(screen.getByText('Automatically selected')).toBeTruthy();
 expect(screen.queryByRole('button')).toBeNull();
});
test('locked choices cannot be changed',()=>{
 const changed=vi.fn();
 render(<UmpireChoices label="Serving side" disabled value="A" options={[{value:'A',label:'Side A'},{value:'B',label:'Side B'}]} onChange={changed}/>);
 fireEvent.click(screen.getByRole('button',{name:'Side B'}));
 expect(changed).not.toHaveBeenCalled();
});
