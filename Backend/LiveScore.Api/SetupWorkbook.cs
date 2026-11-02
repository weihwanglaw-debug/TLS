using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace LiveScore.Api;

public static class SetupWorkbook
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly string[][] Columns = [
        ["EventID","EventName","SportsType","StartDate","EndDate"],
        ["ProgramID","ProgramName","GameType","Gender","MinAge","MaxAge"],
        ["ParticipantID","ProgramID","GroupID","Name","Dob","Gender","Email","Mobile","Club/School/Company","Nationality","SbaId","Seed"] ];
    private static readonly string[] Sheets = ["Event","Program","Participant"];
    public static byte[] Template() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Templates","TLS-event-template.xlsx"));
    private static XDocument Xml(ZipArchive zip,string path)
    {
        var entry=zip.GetEntry(path) ?? throw new RuleException($"Workbook is missing {path}.");
        using var stream=entry.Open();
        using var reader=XmlReader.Create(stream,new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=8_000_000 });
        return XDocument.Load(reader);
    }
    public static SetupPackage Read(Stream stream,string timeZone)
    {
        using var zip=new ZipArchive(stream,ZipArchiveMode.Read,true);
        if(zip.Entries.Count>100 || zip.Entries.Sum(x=>x.Length)>20_000_000) throw new RuleException("Workbook is too large after decompression.");
        var shared=zip.GetEntry("xl/sharedStrings.xml") is null ? [] : Xml(zip,"xl/sharedStrings.xml").Descendants(S+"si").Select(x=>string.Concat(x.Descendants(S+"t").Select(t=>t.Value))).ToArray();
        var dateStyles = new HashSet<int>();
        if(zip.GetEntry("xl/styles.xml") != null)
        {
            var styles=Xml(zip,"xl/styles.xml");
            var custom=styles.Descendants(S+"numFmt").Where(x=>System.Text.RegularExpressions.Regex.IsMatch((string?)x.Attribute("formatCode") ?? "", "[dy]",System.Text.RegularExpressions.RegexOptions.IgnoreCase)).Select(x=>(int)x.Attribute("numFmtId")!).ToHashSet();
            var formats=styles.Root?.Element(S+"cellXfs")?.Elements(S+"xf").ToList() ?? [];
            for(var i=0;i<formats.Count;i++) { var n=(int?)formats[i].Attribute("numFmtId") ?? 0; if(n is >=14 and <=22 || custom.Contains(n)) dateStyles.Add(i); }
        }
        var workbook=Xml(zip,"xl/workbook.xml");
        bool date1904=(string?)workbook.Root?.Element(S+"workbookPr")?.Attribute("date1904") is "1" or "true";
        var rels=Xml(zip,"xl/_rels/workbook.xml.rels").Root!.Elements().ToDictionary(x=>(string)x.Attribute("Id")!,x=>(string)x.Attribute("Target")!);
        var result=new SetupPackage { SchemaVersion=2,Source="EXCEL",SourceSystemId="workbook",SourceTimeZone=timeZone,ExportedAt=DateTimeOffset.UtcNow };
        var errors=new List<string>();
        var seenSheets=new HashSet<string>();
        var participantRows=new List<(string ProgramId,string GroupId,SetupPerson Person,int? Seed,string Row)>();
        string sport="";

        foreach(var sheet in workbook.Descendants(S+"sheet"))
        {
            var name=(string?)sheet.Attribute("name") ?? ""; var index=Array.IndexOf(Sheets,name);
            if(name=="_Lists")continue;
            if(index<0) throw new RuleException($"Unexpected sheet {name}. Use only Event, Program and Participant from the current template.");
            if(!seenSheets.Add(name))throw new RuleException($"Duplicate {name} sheet.");
            var rid=(string?)sheet.Attribute(XName.Get("id","http://schemas.openxmlformats.org/officeDocument/2006/relationships"));
            if(rid==null || !rels.TryGetValue(rid,out var target) || target.Contains("..") || target.Contains('\\')) throw new RuleException("Invalid worksheet relationship.");
            var path=target.StartsWith('/') ? target.TrimStart('/') : "xl/"+target;
            var rows=Xml(zip,path).Descendants(S+"row").ToList();
            if(rows.Count>10001) throw new RuleException($"{name}: maximum 10000 rows.");
            Dictionary<int,string> Cells(XElement row)
            {
                var cells=new Dictionary<int,string>();
                foreach(var cell in row.Elements(S+"c"))
                {
                    if(cell.Element(S+"f")!=null) throw new RuleException($"{name} row {(string?)row.Attribute("r")}: formulas are not allowed. Paste values instead.");
                    var reference=(string?)cell.Attribute("r") ?? ""; var col=0;
                    foreach(var ch in reference.TakeWhile(char.IsLetter)) col=col*26+(char.ToUpperInvariant(ch)-'A'+1);
                    var type=(string?)cell.Attribute("t"); var value=cell.Element(S+"v")?.Value ?? "";
                    if(type=="s") { if(!int.TryParse(value,out var si) || si<0 || si>=shared.Length) throw new RuleException("Invalid shared string."); value=shared[si]; }
                    else if(type=="inlineStr") value=string.Concat(cell.Descendants(S+"t").Select(x=>x.Value));
                    else if(type=="d") value=DateTimeOffset.Parse(value,CultureInfo.InvariantCulture).ToString("yyyy-MM-dd");
                    else if(dateStyles.Contains((int?)cell.Attribute("s") ?? -1) && double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var serial))
                        value=DateTime.FromOADate(serial+(date1904?1462:0)).ToString("yyyy-MM-dd");
                    cells[col-1]=value.Trim();
                }
                return cells;
            }
            if(rows.Count==0) continue;
            var header=Cells(rows[0]);
            if(header.Values.Distinct().Count()!=header.Count) throw new RuleException($"{name}: duplicate column headings.");
            if(header.Count!=Columns[index].Length || !Columns[index].All(c=>header.ContainsValue(c))) throw new RuleException($"{name}: use the template headers: {string.Join(", ",Columns[index])}.");
            foreach(var row in rows.Skip(1))
            {
                var cells=Cells(row); if(cells.Values.All(string.IsNullOrWhiteSpace))continue;
                string V(string key)=>cells.GetValueOrDefault(header.Single(x=>x.Value==key).Key,"");
                int N(string key)=>int.Parse(V(key),CultureInfo.InvariantCulture);
                int? O(string key)=>string.IsNullOrWhiteSpace(V(key))?null:N(key);
                DateOnly D(string key)=>DateOnly.ParseExact(V(key),"yyyy-MM-dd",CultureInfo.InvariantCulture);

                try
                {
                    switch(index)
                    {
                        case 0:
                            if(result.Events.Count>0)throw new RuleException("Event: one workbook may contain exactly one event.");
                            sport=V("SportsType") switch { "Badminton"=>"Badminton", "Non-badminton"=>"Non-badminton", _=>throw new RuleException($"Event row {(string?)row.Attribute("r")}: SportsType must be Badminton or Non-badminton.") };
                            result.Events.Add(new(V("EventID"),V("EventName"),"",D("StartDate"),D("EndDate"),null,"D"));break;
                        case 1:
                            var type=V("GameType") switch {"Singles"=>"S","Doubles"=>"D",_=>throw new RuleException($"Program row {(string?)row.Attribute("r")}: GameType must be Singles or Doubles.")};
                            var size=type=="S"?1:2;
                            result.Programs.Add(new(V("ProgramID"),"",V("ProgramName"),"",type,size,size,N("MinAge"),N("MaxAge"),V("Gender"),2,10000,"E","A"));break;
                        case 2:
                            var person=new SetupPerson(V("ParticipantID"),"",V("Name"),D("Dob"),V("Gender"),V("Email"),V("Mobile"),V("Club/School/Company"),V("Nationality"),V("SbaId"));
                            participantRows.Add((V("ProgramID"),V("GroupID"),person,O("Seed"),(string?)row.Attribute("r")??"?"));break;
                    }
                }
                catch(Exception ex) when(ex is FormatException or OverflowException) { errors.Add($"{name} row {(string?)row.Attribute("r")}: check integer fields and use YYYY-MM-DD dates."); }
            }
        }
        if(errors.Count>0) throw new RuleException(string.Join("\n",errors.Take(30)));
        if(seenSheets.Count!=3 || result.Events.Count!=1)throw new RuleException("Use the current three-sheet template with exactly one Event row.");
        var eventId=result.Events[0].Id;
        result.Programs=result.Programs.Select(p=>p with {EventId=eventId,Sport=sport}).ToList();
        var people=new Dictionary<string,SetupPerson>(StringComparer.Ordinal);
        foreach(var item in participantRows)
        {
            if(string.IsNullOrWhiteSpace(item.GroupId)||item.GroupId.Length>160)throw new RuleException($"Participant row {item.Row}: GroupID is required (maximum 160 characters).");
            var person=item.Person with {EventId=eventId};
            if(people.TryGetValue(person.Id,out var old) && old!=person)throw new RuleException($"Participant row {item.Row}: repeated ParticipantID {person.Id} has conflicting personal details.");
            people[person.Id]=person;
        }
        result.Participants=people.Values.ToList();
        foreach(var group in participantRows.GroupBy(x=>(x.ProgramId,x.GroupId)))
        {
            if(group.Select(x=>x.Person.Id).Distinct().Count()!=group.Count())throw new RuleException($"Participant rows {string.Join(", ",group.Select(x=>x.Row))}: duplicate participant in the same program/group.");
            var seeds=group.Where(x=>x.Seed.HasValue).Select(x=>x.Seed).Distinct().ToList();
            if(seeds.Count>1)throw new RuleException($"Participant rows {string.Join(", ",group.Select(x=>x.Row))}: Seed belongs to the group and must agree.");
            // A GroupID is local to its program; retain both parts without ambiguous concatenation.
            var key=System.Text.Json.JsonSerializer.Serialize(new[]{group.Key.ProgramId,group.Key.GroupId});
            var id="group:"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key)));
            result.Entries.Add(new(id,eventId,group.Key.ProgramId,group.Select(x=>x.Person.Id).ToArray(),group.Key.GroupId,seeds.SingleOrDefault(),"A"));
        }
        return result;
    }
}
