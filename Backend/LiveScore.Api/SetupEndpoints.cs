using System.Collections.Concurrent;

using System.Net;

using System.Text.Json;

namespace LiveScore.Api;



public static class SetupEndpoints

{

    private sealed record Preview(long Revision,SetupPackage Package,string? ReplaceEventId,DateTimeOffset Expires);

    private static readonly ConcurrentDictionary<string,Preview> Previews=new();

    public static void MapSetup(this WebApplication app)

    {

        var group=app.MapGroup("/api/setup");

        group.AddEndpointFilter(async(context,next)=>

        {

            var ip=context.HttpContext.Connection.RemoteIpAddress;

            if(!LiveAccess.LocalAdmin(context.HttpContext))return Results.Json(new{message="Event preparation is available only on the local server."},statusCode:403);

            try{return await next(context);}

            catch(Exception ex) when(ex is JsonException or InvalidDataException or System.Xml.XmlException or FormatException or OverflowException or ArgumentException)

            {return Results.BadRequest(new{message="Invalid file format. Use the current Excel template or TRS event transfer. "+(ex is JsonException ? "Check required fields, field types and unexpected properties." : "Check worksheet headings and cell values.")});}

        });

        group.MapGet("/",(SetupStore store)=>{var s=store.Read();return new{data=s,readiness=ImportWorkflow.Readiness(s)};});

        group.MapGet("/events/{eventId}/removal",(string eventId,SetupStore store)=>
        {
            var s=store.Read();var e=RemovableEvent(s,eventId);
            return new{revision=s.Revision,name=e.Name,programs=s.Programs.Count(p=>p.EventId==eventId),participants=s.Participants.Count(p=>p.EventId==eventId),entries=s.Entries.Count(p=>p.EventId==eventId)};
        });

        group.MapDelete("/events/{eventId}",(string eventId,long revision,SetupStore store)=>
        {
            var state=store.Change(revision,"EVENT_REMOVED",s=>
            {
                RemovableEvent(s,eventId);
                var programs=s.Programs.Where(p=>p.EventId==eventId).Select(p=>p.Id).ToHashSet();
                s.ProgramPlans.RemoveAll(p=>programs.Contains(p.ProgramId));
                s.EventPlans.RemoveAll(p=>p.EventId==eventId);
                s.Entries.RemoveAll(p=>p.EventId==eventId);
                s.Participants.RemoveAll(p=>p.EventId==eventId);
                s.Programs.RemoveAll(p=>p.EventId==eventId);
                s.Imports.RemoveAll(p=>p.EventId==eventId);
                foreach(var key in s.ImportIdentities.Where(p=>p.Value.EventId==eventId).Select(p=>p.Key).ToArray())s.ImportIdentities.Remove(key);
                s.Events.RemoveAll(p=>p.Id==eventId);
            },auditView:s=>new{events=s.Events.Where(p=>p.Id==eventId).ToArray(),programs=s.Programs.Count(p=>p.EventId==eventId),participants=s.Participants.Count(p=>p.EventId==eventId),entries=s.Entries.Count(p=>p.EventId==eventId)});
            return new{data=state,readiness=ImportWorkflow.Readiness(state)};
        });

        group.MapGet("/zones",()=>TimeZoneInfo.GetSystemTimeZones().Select(x=>new{id=x.HasIanaId?x.Id:TimeZoneInfo.TryConvertWindowsIdToIanaId(x.Id,out var i)?i:x.Id,name=x.DisplayName}));

        group.MapPut("/settings",(SetupSettings request,SetupStore store)=>store.Change(request.Revision,"SETTINGS",s=>{SetupStore.Zone(request.TimeZone);if(s.TimeZone!=request.TimeZone)TournamentRules.Need(!s.ProgramPlans.Any(p=>p.Matches.Count>0),"Timezone cannot change after fixtures are generated.");

            s.TimeZone=request.TimeZone;
            if(request.LiveDisplay is not null)
            {
                var d=request.LiveDisplay;
                TournamentRules.Need(d.Columns is >=1 and <=6&&d.Rows is >=1 and <=6,"Display rows and columns must be 1–6.");
                TournamentRules.Need(d.PageSeconds is >=3 and <=120,"Display page duration must be 3–120 seconds.");
                TournamentRules.Need(d.EventId is not null&&(d.EventId==""||s.Events.Any(e=>e.Id==d.EventId)),"Choose an imported display event.");
                TournamentRules.Need(d.Courts is not null&&d.Courts.Distinct().Count()==d.Courts.Length&&d.Courts.All(c=>c>=1&&c<=s.CourtCount),"Choose valid, distinct display courts.");
                s.Venue.Display=d;
            }

            if(request.CourtCount is not null){TournamentRules.Need(request.CourtCount>=1&&request.CourtCount<=50,"Court count must be 1â€“50.");TournamentRules.Need(!TournamentScheduler.Matches(s).Any(m=>m.Court>request.CourtCount)&&!s.EventPlans.Any(p=>p.ScheduleDays.Any(d=>d.Courts.Any(c=>c>request.CourtCount))),"A court is still assigned to a fixture; its venue number cannot be removed.");TournamentRules.Need(!s.Venue.ClosedCourts.Any(c=>c>request.CourtCount)&&!s.Venue.Bindings.Any(b=>b.Court>request.CourtCount),"Reopen or release the affected courts before reducing the court count.");s.CourtCount=request.CourtCount.Value;}

            if(request.AppName is not null) {if(string.IsNullOrWhiteSpace(request.AppName)||request.AppName.Length>100)throw new RuleException("Application name is required (maximum 100 characters).");s.AppName=request.AppName.Trim();}

            string Logo(string value){if(value.Length>2048||value.Any(char.IsControl)||!(value==""||value.StartsWith("/")&&!value.StartsWith("//")||Uri.TryCreate(value,UriKind.Absolute,out var uri)&&uri.Scheme=="https"))throw new RuleException("Logo URL must be HTTPS or a local path starting with /.");return value;}

            if(request.LogoLightUrl is not null)s.LogoLightUrl=Logo(request.LogoLightUrl.Trim());

            if(request.LogoDarkUrl is not null)s.LogoDarkUrl=Logo(request.LogoDarkUrl.Trim());}));

        group.MapGet("/template",()=>Results.File(SetupWorkbook.Template(),"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet","TLS-event-template.xlsx"));

        group.MapPost("/preview",async(HttpRequest request,SetupStore store)=>

        {

            if(!long.TryParse(request.Query["revision"],out var revision))throw new RuleException("A current revision is required.");

            var kind=request.Query["kind"].ToString(); if(kind is not ("excel" or "trs"))throw new RuleException("Choose Excel or TRS JSON.");

            var replace=request.Query["replaceEventId"].ToString();string? replaceId=replace.Length==0?null:replace;

            var state=store.Read();SetupStore.CheckRevision(state,revision);

            if(request.ContentLength>1_500_000)throw new RuleException("Choose a file smaller than 1.5 MB.");

            using var content=new MemoryStream();var buffer=new byte[8192];int n;

            while((n=await request.Body.ReadAsync(buffer))>0){if(content.Length+n>1_500_000)throw new RuleException("Choose a file smaller than 1.5 MB.");content.Write(buffer,0,n);}content.Position=0;

            var package=kind=="excel"?SetupWorkbook.Read(content,state.TimeZone):JsonSerializer.Deserialize<SetupPackage>(content,JsonDefaults.Options)??throw new RuleException("Empty JSON file.");

            if(kind=="trs"&&package.Source!="TRS")throw new RuleException("This upload requires a TRS export with source TRS.");

            var data=ImportWorkflow.Check(state,package,replaceId);

            foreach(var item in Previews.Where(x=>x.Value.Expires<DateTimeOffset.UtcNow))Previews.TryRemove(item.Key,out _);

            if(Previews.Count>=100)throw new RuleException("Too many previews. Wait for older previews to expire.",429);

            var token=Guid.NewGuid().ToString("N");Previews[token]=new(revision,package,replaceId,DateTimeOffset.UtcNow.AddMinutes(10));

            return Results.Ok(new{token,revision,data,readiness=ImportWorkflow.Readiness(data),source=package.Source,sourceSystemId=package.SourceSystemId,sourceEventId=package.Events[0].Id,sourceTimeZone=package.SourceTimeZone,exportedAt=package.ExportedAt,replacing=replaceId is not null});

        });

        group.MapPost("/confirm",(SetupConfirm request,SetupStore store)=>

        {

            if(request.Token is null||!Previews.TryGetValue(request.Token,out var preview)||preview.Expires<DateTimeOffset.UtcNow)throw new RuleException("Preview expired or already confirmed. Refresh and upload again.",409);

            if(request.Revision!=preview.Revision)throw new RuleException("Preview changed. Upload again.",409);

            var state=ImportWorkflow.Commit(store,request.Revision,preview.Package,preview.ReplaceEventId);

            Previews.TryRemove(request.Token,out _);return new{data=state,readiness=ImportWorkflow.Readiness(state)};

        });

    }

    private static SetupEvent RemovableEvent(SetupData s,string eventId)
    {
        var e=s.Events.SingleOrDefault(e=>e.Id==eventId)??throw new RuleException("This event no longer exists. Refresh the event list.",404);
        var programs=s.Programs.Where(p=>p.EventId==eventId).Select(p=>p.Id).ToHashSet();
        if(s.ProgramPlans.Any(p=>programs.Contains(p.ProgramId)&&p.Matches.Count>0))
            throw new RuleException("This event cannot be removed because fixtures have been generated.",409);
        return e;
    }

}

