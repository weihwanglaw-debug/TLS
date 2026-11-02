import {cleanup,fireEvent,render,screen} from '@testing-library/react';
import {afterEach,expect,test,vi} from 'vitest';
import TabletPairing,{venueTabletUrls,type PairingInvitation} from '../src/TabletPairing';
const invitation={code:'ABCD1234',expiresAt:'2026-10-27T02:03:00Z',tabletUrl:'http://localhost:5180/umpire',match:{court:1,programName:"Men's Singles"}} as PairingInvitation;
afterEach(cleanup);
test('shows local QR, countdown and manual fallback using the venue address',()=>{
 render(<TabletPairing invitation={invitation} urls={[invitation.tabletUrl,'http://192.168.1.10:5180/umpire']} now={Date.parse('2026-10-27T02:00:00Z')} busy={false} onRenew={()=>{}}/>);
 expect(screen.getByRole('status').textContent).toBe('Pair within 3:00');
 expect(screen.getByText('ABCD1234')).toBeTruthy();
 expect(screen.getByText('http://192.168.1.10:5180/umpire')).toBeTruthy();
 expect(screen.getByTitle('Scan to connect umpire tablet').parentElement?.tagName.toLowerCase()).toBe('svg');
 expect(screen.queryByRole('button',{name:'Generate New Code'})).toBeNull();
});
test('expiry removes QR and code and offers explicit regeneration',()=>{
 const renew=vi.fn();render(<TabletPairing invitation={invitation} urls={['http://192.168.1.10:5180/umpire']} now={Date.parse(invitation.expiresAt)} busy={false} onRenew={renew}/>);
 expect(screen.getByRole('status').textContent).toBe('Pairing invitation expired');
 expect(screen.queryByText('ABCD1234')).toBeNull();expect(screen.queryByTitle('Scan to connect umpire tablet')).toBeNull();
 fireEvent.click(screen.getByRole('button',{name:'Generate New Code'}));expect(renew).toHaveBeenCalledOnce();
});
test('does not encode a localhost or unspecified address for another device',()=>{
 expect(venueTabletUrls(['http://localhost:5180/umpire','http://127.0.0.1/umpire','http://[::1]/umpire','http://0.0.0.0/umpire','invalid','http://192.168.1.10:5180/umpire'])).toEqual(['http://192.168.1.10:5180/umpire']);
 render(<TabletPairing invitation={invitation} urls={[invitation.tabletUrl]} now={Date.parse('2026-10-27T02:00:00Z')} busy={false} onRenew={()=>{}}/>);
 expect(screen.queryByTitle('Scan to connect umpire tablet')).toBeNull();expect(screen.getByText(/No venue network address found/)).toBeTruthy();
});
