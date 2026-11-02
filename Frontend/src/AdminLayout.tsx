import {useState,type ReactNode} from 'react';
import {CalendarDays,Settings,Trophy,ChevronLeft,Menu,Moon,Sun,LayoutDashboard,Users,SlidersHorizontal,UserCog,ListOrdered} from 'lucide-react';
type Props={children:ReactNode;page:string;busy:boolean;onNavigate:(page:string)=>void;theme:string;onToggleTheme:()=>void;appName:string;logoUrl:string};
export default function AdminLayout({children,page,busy,onNavigate,theme,onToggleTheme,appName,logoUrl}:Props){
  const [collapsed,setCollapsed]=useState(true),[hovered,setHovered]=useState(false);
  const expanded=!collapsed||hovered;
  const links=[{key:'dashboard',label:'Dashboard',icon:LayoutDashboard,available:true},{key:'events',label:'Event',icon:CalendarDays,available:true},{key:'participants',label:'Participant',icon:Users,available:true},{key:'fixtures',label:'Fixture and Result',icon:Trophy,available:true},{key:'sba',label:'SBA Rankings',icon:ListOrdered,available:true},{key:'presets',label:'Game Setting Preset',icon:SlidersHorizontal,available:true},{key:'settings',label:'Master Setting',icon:Settings,available:true},{key:'users',label:'User Management',icon:UserCog,available:false}];
  return <div className="flex min-h-screen">
    <aside className={`${expanded?'w-60':'w-16'} fixed top-0 bottom-0 flex flex-col overflow-y-auto overflow-x-hidden transition-all duration-300 z-40`} style={{background:'var(--color-hero-bg)',color:'var(--color-hero-text)'}} onMouseEnter={()=>collapsed&&setHovered(true)} onMouseLeave={()=>setHovered(false)}>
      <div className="flex items-center justify-between px-3 py-4" style={{borderBottom:'1px solid var(--admin-header-border)'}}>
        {expanded&&<div className="flex items-center gap-2 px-1">{logoUrl?<img src={logoUrl} alt={appName||'Admin'} className="h-9 w-9 flex-shrink-0 object-contain"/>:<Trophy className="h-5 w-5 flex-shrink-0"/>}<span className="font-bold text-sm whitespace-nowrap">Admin Panel</span></div>}
        <button onClick={()=>{setCollapsed(!collapsed);setHovered(false);}} className="p-2 hover:bg-white/10 transition-colors flex-shrink-0" aria-label={collapsed?'Expand sidebar':'Collapse sidebar'} title={collapsed?'Expand sidebar':'Collapse sidebar'}>{collapsed&&!hovered?<Menu className="h-4 w-4"/>:<ChevronLeft className="h-4 w-4"/>}</button>
      </div>
      <nav className="flex-1 py-4" aria-label="Main navigation">{[{label:'Dashboard',keys:['dashboard']},{label:'Event Management',keys:['events','participants','fixtures']},{label:'Configuration',keys:['sba','presets','settings','users']}].map((group,index)=><div key={group.label} role="group" aria-label={group.label} className={index?'mt-4 pt-4 border-t border-white/15':''}>
        {expanded&&index>0&&<p className="px-4 mb-2 text-xs font-semibold uppercase tracking-wider opacity-60">{group.label}</p>}
        <div className="space-y-1">{links.filter(link=>group.keys.includes(link.key)).map(link=><button key={link.key} disabled={busy||!link.available} onClick={()=>onNavigate(link.key)} title={!link.available?'Not available in this foundation build':!expanded?link.label:undefined} aria-label={link.label} aria-current={page===link.key?'page':undefined} className={`flex items-center gap-3 py-3 text-sm font-medium transition-colors w-full ${expanded?'px-4':'px-0 justify-center'} ${page===link.key?'bg-white/15':'hover:bg-white/10'}`}><link.icon className="h-4 w-4 flex-shrink-0"/>{expanded&&<span className="whitespace-nowrap">{link.label}</span>}</button>)}</div>
      </div>)}</nav>
      <div className={`mt-auto ${expanded?'px-4':'px-2'} pb-4`}><button onClick={onToggleTheme} title="Toggle theme" aria-label="Toggle theme" className={`flex items-center gap-2 text-sm hover:bg-white/10 py-2.5 w-full transition-colors ${expanded?'px-3 mb-2':'justify-center px-0 mb-2'}`}>{theme==='a'?<Moon className="h-4 w-4 flex-shrink-0"/>:<Sun className="h-4 w-4 flex-shrink-0"/>}{expanded&&(theme==='a'?'Dark Mode':'Light Mode')}</button></div>
    </aside>
    <main className="flex-1 min-w-0 p-6 md:p-10 transition-all duration-300 ml-16" style={{backgroundColor:'var(--color-page-bg)'}}>{children}</main>
  </div>;
}
