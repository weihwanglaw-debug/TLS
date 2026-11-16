import type React from 'react';
import type {MatchEntry} from '../types/config';
import {getEntryDisplay} from './entryDisplay';
import {venueTime} from '../live-types';
const formatConfiguredDateTime=(date:Date,timeZone='Asia/Singapore',_format?:string)=>venueTime(date.toISOString(),timeZone);
const escape=(value:string)=>value.replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]!));
export function audienceScheduleHtml(program:string,matches:MatchEntry[],groups:Record<string,string>={}) {
  const ordered=[...matches].sort((a,b)=>{
    const date=(a.matchDate||'9999').localeCompare(b.matchDate||'9999');
    return date||(a.startTime||'99:99').padEnd(8,':00').localeCompare((b.startTime||'99:99').padEnd(8,':00'));
  });
  const days=new Map<string,MatchEntry[]>();
  for(const match of ordered){const rows=days.get(match.matchDate)||[];rows.push(match);days.set(match.matchDate,rows);}
  const team=(entry:MatchEntry['team1'])=>`${entry.seed!=null?'<span class="schedule-seed">'+escape(String(entry.seed))+'</span> ':''}${escape(getEntryDisplay(entry,'compact').main||'To be confirmed')}`;
  const score=(m:MatchEntry)=>{
    if(m.status==='BY'||m.team1.label==='BYE'||m.team2.label==='BYE')return 'BYE';
    if(m.walkover||m.status==='WO')return 'Walkover';
    const games=m.games.filter(g=>g.p1!==''&&g.p2!=='').map(g=>escape(g.p1)+' - '+escape(g.p2)).join('<br>');
    const outcome=({R:'Retirement',D:'Disqualification',M:'Admin decision'} as Record<string,string>)[m.resultKind||''];
    return (games||'&mdash;')+(outcome?'<span class="schedule-outcome">'+outcome+'</span>':'');
  };
  return `<section class="audience-schedule"><h2 class="schedule-program">${escape(program)}</h2>${[...days].map(([date,rows])=>{
    const title=date?new Intl.DateTimeFormat('en-GB',{weekday:'long',day:'numeric',month:'long',year:'numeric',timeZone:'UTC'}).format(new Date(date+'T00:00:00Z')):'Awaiting scheduling';
    return `<table class="schedule-table"><colgroup><col style="width:13%"><col style="width:8%"><col style="width:15%"><col style="width:52%"><col style="width:12%"></colgroup><thead><tr class="schedule-date"><th colspan="5">${escape(title)}</th></tr><tr><th>Time</th><th>Court</th><th>Group / Round</th><th>Match</th><th>Score</th></tr></thead><tbody>${rows.map((m,i)=>{
      const time=m.startTime?escape(m.startTime.slice(0,5))+(m.endTime?' - '+escape(m.endTime.slice(0,5)):''):'To be scheduled';
      const stage=m.groupId?(groups[m.groupId]||m.roundLabel||'Group '+m.groupId):m.roundLabel||'Round '+m.round;
      const start=i===0||m.startTime!==rows[i-1].startTime;
      return `<tr${start?' class="schedule-slot"':''}><td class="schedule-time">${time}</td><td class="schedule-court">${m.courtNo?escape(m.courtNo):'TBC'}</td><td>${escape(stage)}</td><td class="schedule-match"><span class="schedule-pair"><span class="schedule-team">${team(m.team1)}</span><span class="schedule-vs">vs</span><span class="schedule-team">${team(m.team2)}</span></span></td><td class="schedule-score">${score(m)}</td></tr>`;
    }).join('')}</tbody></table>`;
  }).join('')}</section>`;
}

function resolveCssVars(el: Element): void {
  const computed = window.getComputedStyle(document.documentElement);
  const resolve  = (val: string) =>
    val.replace(/var\(([^)]+)\)/g, (_, v) => computed.getPropertyValue(v.trim()).trim());

  // Fix inline style properties
  el.querySelectorAll<HTMLElement>("[style]").forEach(node => {
    const s = node.style;
    for (let i = 0; i < s.length; i++) {
      const prop = s[i];
      const val  = s.getPropertyValue(prop);
      if (val.includes("var(--")) s.setProperty(prop, resolve(val));
    }
  });

  // Fix SVG elements: fill/stroke attributes AND style.fill / style.stroke
  el.querySelectorAll("*").forEach(node => {
  // Attributes
    ["fill", "stroke", "color"].forEach(attr => {
      const v = node.getAttribute(attr) ?? "";
      if (v.includes("var(--")) node.setAttribute(attr, resolve(v));
    });
  // style.fill / style.stroke on SVG/HTML elements
    const s = (node as HTMLElement).style;
    if (!s) return;
    ["fill", "stroke", "color", "background", "background-color", "border-color"].forEach(prop => {
      const v = s.getPropertyValue(prop);
      if (v && v.includes("var(--")) s.setProperty(prop, resolve(v));
    });
  });
}

type CapturedBracket = {
  outerHtml: string;        // full clone HTML (for normal print)
  svgViewBox: string | null; // "0 0 W H" or null
  svgInnerHtml: string | null; // content inside <svg> (for split print)
};

type PrintPaperSize = "A4 portrait" | "A4 landscape" | "A3 landscape";

function captureBracket(bracketRef: React.RefObject<HTMLDivElement | null>): CapturedBracket {
  if (!bracketRef.current) return {
    outerHtml: "<p style=\"color:#999;font-style:italic\">No bracket generated.</p>",
    svgViewBox: null,
    svgInnerHtml: null,
  };

  const clone = bracketRef.current.cloneNode(true) as HTMLElement;
  resolveCssVars(clone);
  clone.querySelectorAll(".minimap, button, select, input, [data-print-exclude='true']").forEach(n => n.remove());
  clone.querySelectorAll<HTMLElement>("[style]").forEach(n => {
    n.style.cursor = "default";
    n.style.boxShadow = "none";
    n.style.transition = "none";
  });
  clone.querySelectorAll("svg").forEach(svg => {
    svg.setAttribute("xmlns", "http://www.w3.org/2000/svg");
    svg.style.overflow = "visible";
    svg.style.maxWidth = "none";
  });
  clone.style.border = "none";
  clone.style.padding = "0";
  clone.style.overflow = "visible";
  clone.style.borderRadius = "0";

  // Grab viewBox from the main SVG for split printing
  const svg = bracketRef.current.querySelector("svg");
  const svgViewBox = svg?.getAttribute("viewBox") ?? null;
  const svgClone   = clone.querySelector("svg");
  const svgInnerHtml = svgClone?.innerHTML ?? null;

  return { outerHtml: clone.outerHTML, svgViewBox, svgInnerHtml };
}

// Legacy wrapper used for normal (non-split) print
function captureSvgHtml(bracketRef: React.RefObject<HTMLDivElement | null>): string {
  return captureBracket(bracketRef).outerHtml;
}

function openPrintWindow(
  eventName:   string,
  programName: string,
  groupHtml:   string,
  svgHtml:     string,       // pure SVG string(s) - already safe to embed
  autoPrint:   boolean,
  isSplit?:    boolean,
  paperSize:   PrintPaperSize = "A4 landscape",
  timeZone?:   string,
  dateTimeFormat?: string,
): string | null {
  const now = formatConfiguredDateTime(new Date(), timeZone, dateTimeFormat);

  const html = `<!DOCTYPE html><html lang="en"><head>
<meta charset="UTF-8">
<title>${programName} - Draw Sheet</title>
<style>
  * { box-sizing: border-box; margin: 0; padding: 0; }
  body { font-family: 'Segoe UI', Arial, sans-serif; color: #111; background: white; padding: 28px 32px; font-size: 13px; }

  /* Header */
  .ph { border-bottom: 3px solid #1E3A5F; padding-bottom: 14px; margin-bottom: 24px; display:flex; justify-content:space-between; align-items:flex-end; }
  .ph-left h1 { font-size: 20px; font-weight: 800; color: #1E3A5F; margin-bottom: 2px; }
  .ph-left h2 { font-size: 14px; font-weight: 400; color: #555; }
  .ph-meta    { font-size: 11px; color: #aaa; text-align:right; }

  /* Section title */
  .sec-title { font-size: 10px; font-weight: 700; text-transform: uppercase; letter-spacing: .08em;
    color: #1E3A5F; border-bottom: 2px solid #1E3A5F; padding-bottom: 4px; margin-bottom: 14px; }

  /* Group tables */
  .group-block { margin-bottom: 22px; }
  .group-name  { background: #1E3A5F; color: white; padding: 5px 10px; font-weight: 700; font-size: 12px; }
  table { width: 100%; border-collapse: collapse; font-size: 11px; margin-bottom: 0; }
  th { background: #2d4f7c; color: white; padding: 5px 8px; text-align: left; font-size: 10px; font-weight: 600; }
  td { padding: 5px 8px; border-bottom: 1px solid #eee; }
  tr:nth-child(even) td { background: #f7f9fb; }
  .seed-badge { background: #1E3A5F; color: white; font-size: 9px; font-weight: 700;
    padding: 1px 4px; margin-right: 3px; display: inline-block; }

  /* Audience schedule: standalone rules, independent of application utility CSS. */
  .audience-schedule { margin-bottom: 24px; }
  .schedule-program { font-size: 17px; color: #1e3a5f; margin: 0 0 14px; }
  .schedule-table { table-layout: fixed; margin: 0 0 20px; border: 1px solid #bcc8d5; font-size: 12px; }
  .schedule-table th { padding: 8px 10px; font-size: 10px; letter-spacing: .04em; text-transform: uppercase; }
  .schedule-table .schedule-date th { background: #eaf0f6; color: #1e3a5f; font-size: 13px; letter-spacing: .02em; padding: 11px 10px; border-bottom: 1px solid #bcc8d5; }
  .schedule-table td { padding: 10px; vertical-align: middle; line-height: 1.45; overflow-wrap: anywhere; }
  .schedule-table tr.schedule-slot td { border-top: 2px solid #b6c5d5; }
  .schedule-time { font-weight: 600; font-variant-numeric: tabular-nums; }
  .schedule-court { text-align: center; font-weight: 700; font-size: 15px; }
  .schedule-match { font-size: 12px; }
  .schedule-pair { display: grid; grid-template-columns: minmax(0,1fr) 18px minmax(0,1fr); align-items: center; gap: 8px; }
  .schedule-team { display: block; }
  .schedule-vs { display: inline; color: #65768a; font-size: 10px; text-align: center; }
  .schedule-seed { display: inline-block; vertical-align: baseline; border: 1px solid #9bafc3; border-radius: 3px; color: #1e3a5f; font-size: 9px; font-weight: 700; padding: 0 4px; white-space: nowrap; }
  .schedule-score { text-align: center; font-weight: 600; font-variant-numeric: tabular-nums; }
  .schedule-outcome { display: block; font-weight: 400; font-size: 10px; }
  .schedule-table thead { display: table-header-group; }
  .schedule-table tr { break-inside: avoid; page-break-inside: avoid; }
  .schedule-program { break-after: avoid; }

  /* Bracket section */
  .bracket-wrap { margin-top: 24px; filter: grayscale(1); }
  .bracket-inner { overflow: visible; background: white !important; }
  .bracket-inner > div { background: white !important; color: #111 !important; }
  .bracket-inner svg { background: white !important; }
  .bracket-inner svg * {
    color: #111 !important;
    print-color-adjust: exact !important;
    -webkit-print-color-adjust: exact !important;
  }
  .bracket-inner foreignObject > div {
    background: white !important;
    border: 1.5px solid #111 !important;
  }
  .bracket-inner foreignObject div {
    color: #111 !important;
    border-color: #111 !important;
  }
  .bracket-inner foreignObject > div > div {
    background: white !important;
  }
  .bracket-inner foreignObject > div > div + div {
    border-top: 1px solid #111 !important;
  }
  .bracket-inner path,
  .bracket-inner line {
    stroke: #111 !important;
  }
  .bracket-inner text {
    fill: #111 !important;
  }

  /* Footer */
  .pf { margin-top: 24px; padding-top: 10px; border-top: 1px solid #ddd;
    font-size: 10px; color: #aaa; display: flex; justify-content: space-between; }

  /* Print bar (hidden when printing) */
  .no-print { background: #1E3A5F; color: white; padding: 10px 20px; margin: -28px -32px 28px;
    display: flex; align-items: center; justify-content: space-between; gap: 12px; }
  .no-print span { font-size: 13px; font-weight: 700; }
  .no-print button { background: white; color: #1E3A5F; border: none; font-weight: 700;
    font-size: 12px; padding: 6px 16px; cursor: pointer; }

  /* Split-print page break */
  .page-break { display: none; }
  .pg2-header { display: none; }

  @media print {
    .no-print { display: none; }
    body { padding: 10px; }
    @page { margin: 8mm; size: ${paperSize}; }
  * { -webkit-print-color-adjust: exact !important; print-color-adjust: exact !important; color-adjust: exact !important; }

  /* Scale bracket SVG to fit page */
    .bracket-inner { width: 100%; overflow: visible; }
    .bracket-inner > div { border: none !important; padding: 0 !important; border-radius: 0 !important; max-height: none !important; overflow: visible !important; }
    .bracket-inner svg { width: 100% !important; height: auto !important; max-width: 100% !important; }

  /* Split mode: each half fills its page */
    .bracket-inner.split svg { width: 100% !important; height: auto !important; }
    .page-break { display: block; page-break-after: always; break-after: page; height: 0; }
    .pg2-header { display: block; border-bottom: 3px solid #1E3A5F; padding-bottom: 10px; margin-bottom: 16px; }
    .pg2-header h1 { font-size: 18px; font-weight: 800; color: #1E3A5F; }
    .pg2-header h2 { font-size: 13px; font-weight: 400; color: #555; }
    .bracket-wrap { page-break-before: auto; }
    .pf { display: none; }
  }
</style>
</head><body>

<div class="no-print">
  <span>${programName} - Draw Sheet</span>
  <button onclick="window.print()">🖨 Print / Save as PDF</button>
</div>

<div class="ph">
  <div class="ph-left">
    <h1>${eventName}</h1>
    <h2>${programName} - Draw Sheet</h2>
  </div>
  <div class="ph-meta">Generated<br>${now}</div>
</div>

${groupHtml}

${svgHtml ? '<div class="bracket-wrap">' +
  (isSplit
    ? '<div class="sec-title">Knockout Bracket - Top Half</div><div class="bracket-inner">' + svgHtml + '</div>'
    : '<div class="sec-title">Knockout Bracket</div><div class="bracket-inner">' + svgHtml + '</div>'
  ) + '</div>'
  : ""}

<div class="pf">
  <span>${eventName} - ${programName}</span>
  <span>Generated ${now}</span>
</div>

${autoPrint ? `<script>
window.addEventListener("load", function() {
  setTimeout(function() {
    window.print();
  // Close after print dialog dismissed (works in most modern browsers)
    window.addEventListener("afterprint", function() { window.close(); });
  // Fallback: close after 30 s in case afterprint doesn't fire
    setTimeout(function() { window.close(); }, 30000);
  }, 600);
});
</script>` : ""}
</body></html>`;

  const w = window.open("", "_blank");
  if (!w) return "Pop-up blocked. Please allow pop-ups for this site and try again.";
  w.document.open();
  w.document.write(html);
  w.document.close();
  return null;
}

export function printFixture(ref:React.RefObject<HTMLDivElement|null>,event:string,timeZone:string,audienceHtml?:string){return openPrintWindow(escape(event),audienceHtml===undefined?'Fixture and Result':'Match Schedule',audienceHtml??captureSvgHtml(ref),'',true,false,'A4 landscape',timeZone);}
