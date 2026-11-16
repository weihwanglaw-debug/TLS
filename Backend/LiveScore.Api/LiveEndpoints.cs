namespace LiveScore.Api;
public static class LiveEndpoints
{
    public static void MapLive(this WebApplication app)
    {
        app.MapGet("/api/display",(SetupStore store,HttpContext context)=>
        {
            if(!LiveAccess.VenuePeer(context.Connection.RemoteIpAddress))return Results.Json(new{message="Live display is available only on the venue network."},statusCode:403);
            return Results.Ok(Display(store.Read(),DateTimeOffset.UtcNow));
        });
        var admin=app.MapGroup("/api/dashboard");
        admin.AddEndpointFilter(async(c,next)=>{if(!LiveAccess.LocalAdmin(c.HttpContext))return Results.Json(new{message="Use the organiser console on the local server."},statusCode:403);return await next(c);});
        admin.MapGet("/",(string? eventId,SetupStore store,LiveRuntime live,HttpContext context)=>LiveDashboard.View(store.Read(),live,context,eventId));
        admin.MapGet("/scoresheet/{matchId}",(string matchId,SetupStore store)=>{var s=store.Read();var m=TournamentScheduler.Matches(s).SingleOrDefault(m=>m.Id==matchId);return m is null?Results.NotFound():Results.Ok(LiveRuntime.Row(s,m));});
        admin.MapPost("/pair",(PairCourtRequest request,LiveRuntime live,HttpContext context)=>live.PairCourt(request,context));
        admin.MapPost("/pairing",(RenewPairingRequest request,LiveRuntime live,HttpContext context)=>live.RenewPairing(request,context));
        admin.MapPost("/resume",(ResumeTabletRequest request,LiveRuntime live,HttpContext context)=>live.Resume(request,context));
        admin.MapPost("/confirm",(MatchControlRequest request,LiveRuntime live)=>{live.Confirm(request);return Results.Ok(new{message="Result confirmed and court released."});});
        admin.MapPost("/paper",(MatchControlRequest request,LiveRuntime live)=>{live.Paper(request);return Results.Ok(new{message="Tablet revoked. Continue on paper and enter the final result."});});
        admin.MapPost("/court",(CourtControlRequest request,LiveRuntime live)=>{live.Close(request);return Results.Ok(new{message="Court availability saved."});});
        admin.MapPost("/cancel",(CancelCourtRequest request,LiveRuntime live)=>{live.Cancel(request);return Results.Ok(new{message="Waiting assignment released."});});
        var tablet=app.MapGroup("/api/tablet");
        tablet.AddEndpointFilter(async(c,next)=>{if(!LiveAccess.VenuePeer(c.HttpContext.Connection.RemoteIpAddress))return Results.Json(new{message="Tablet access is available only on the venue network."},statusCode:403);return await next(c);});
        tablet.MapPost("/pair",(PairTabletRequest request,LiveRuntime live,HttpContext context)=>live.PairTablet(request,context));
        tablet.MapGet("/state",(SetupStore store,LiveRuntime live,HttpContext context)=>live.TabletView(store.Read(),LiveAccess.Bearer(context)));
        tablet.MapPost("/heartbeat",(LiveRuntime live,HttpContext context)=>live.Heartbeat(LiveAccess.Bearer(context),context));
        tablet.MapPost("/command",(TabletCommand request,LiveRuntime live,HttpContext context)=>live.Command(LiveAccess.Bearer(context),request,context));
    }

    public static object Display(SetupData s,DateTimeOffset now)
    {
        var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,SetupStore.Zone(s.TimeZone)).DateTime);
        var events=s.Programs.ToDictionary(p=>p.Id,p=>s.Events.Single(e=>e.Id==p.EventId));
        bool FinishedToday(TournamentMatch m)
        {
            if(m.Status is not (MatchStatus.Completed or MatchStatus.Walkover)||!m.Start.HasValue)return false;
            var ev=events[m.ProgramId];
            return ev.StartDate<=today&&today<=ev.EndDate&&DateOnly.FromDateTime(m.Start.Value)==today&&DateOnly.FromDateTime(TournamentScheduler.Finish(m))==today;
        }
        var matches=TournamentScheduler.Matches(s).Where(m=>m.Court.HasValue&&(MatchStatus.Occupied(m.Status)||FinishedToday(m)))
            .GroupBy(m=>m.Court).Select(g=>g.OrderByDescending(m=>MatchStatus.Occupied(m.Status))
                .ThenByDescending(TournamentScheduler.Finish).ThenByDescending(m=>m.Number).ThenBy(m=>m.Id,StringComparer.Ordinal).First())
            .OrderBy(m=>m.Court).Select(m=>{
                var p=s.Programs.Single(p=>p.Id==m.ProgramId);var row=LiveRuntime.Row(s,m);
                var winner=m.WinnerId is null?null:m.WinnerId==m.EntryA?"A":m.WinnerId==m.EntryB?"B":null;
                return new{id=m.Id,number=m.Number,court=m.Court,programName=p.Name,eventName=events[m.ProgramId].Name,
                    row.RoundLabel,nameA=row.NameA,nameB=row.NameB,status=m.Status,scoreA=row.ScoreA,scoreB=row.ScoreB,
                    games=m.Games,serverSide=m.Live?.Serving?.ServerSide,matchWon=row.MatchWon,winner};
            }).ToArray();
        return new{config=s.Venue.Display.ForScreen(),serverTime=now,matches};
    }
}
