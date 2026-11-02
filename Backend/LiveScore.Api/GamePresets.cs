using System.Text.Json;
namespace LiveScore.Api;

public sealed class GamePreset
{
    public string Id {get;set;}="";
    public string Name {get;set;}="";
    public string Status {get;set;}="A";
    public bool IsDefault {get;set;}
    public BadmintonRules Rules {get;set;}=new();
}
public sealed record PresetRequest(long Revision,GamePreset Preset);
public sealed record MatchPresetRequest(long Revision,string MatchId,string PresetId,long MatchVersion,bool ConfirmRoundChange=false);
public static class GamePresets
{
    public static BadmintonRules Copy(BadmintonRules rules)=>JsonSerializer.Deserialize<BadmintonRules>(JsonSerializer.Serialize(rules,JsonDefaults.Options),JsonDefaults.Options)!;
    public static bool Locked(TournamentMatch match)=>match.Status is not (MatchStatus.Scheduled or MatchStatus.Bye)||match.Live?.StartedAtUtc is not null||match.ActualStartedAt.HasValue||match.ActualFinishedAt.HasValue||match.Games.Count>0||match.Live?.Corrections.Count>0;
    public static GamePreset? Default(SetupData s)=>s.Venue.Presets.SingleOrDefault(p=>p.IsDefault&&p.Status=="A");
    public static void ApplyDefault(SetupData s,IEnumerable<TournamentMatch> matches)
    {
        var preset=Default(s);TournamentRules.Need(preset is not null,"Set an active default in Game Setting Preset before generating a draw.");
        foreach(var m in matches.Where(m=>m.Status==MatchStatus.Scheduled&&!m.RulesAssigned&&!Locked(m)))
        {m.PresetId=preset.Id;m.Rules=Copy(preset.Rules);m.RulesAssigned=true;}
    }
    public static void Assign(SetupData s,TournamentMatch match,string? presetId,bool confirmRoundChange=false)
    {
        TournamentRules.Need(match.Status!=MatchStatus.Bye,"BYEs do not use scoring presets.");
        presetId=string.IsNullOrEmpty(presetId)?match.PresetId??Default(s)?.Id:presetId;
        TournamentRules.Need(!string.IsNullOrEmpty(presetId),"Choose a saved game preset before scoring this match.");
        // Keep the match's frozen snapshot when a master has been edited or deactivated.
        if(match.RulesAssigned&&match.PresetId==presetId)return;
        TournamentRules.Need(!Locked(match),"This match has started or recorded a result. Its scoring rules are locked.");
        TournamentRules.Need(confirmRoundChange||!s.Venue.Bindings.Any(b=>b.MatchId==match.Id),"Changing this match's preset revokes its pairing code and tablet session. Confirm the preset change.");
        var preset=s.Venue.Presets.SingleOrDefault(p=>p.Id==presetId&&p.Status=="A");TournamentRules.Need(preset is not null,"Choose an active game preset.");
        match.PresetId=presetId;match.Rules=Copy(preset.Rules);match.RulesAssigned=true;
        if(match.Live is not null)match.Live.Version++;
        s.Venue.Bindings.RemoveAll(b=>b.MatchId==match.Id);
    }
    public static void Map(WebApplication app)
    {
        var group=app.MapGroup("/api/presets");group.AddEndpointFilter(async(c,next)=>LiveAccess.LocalAdmin(c.HttpContext)?await next(c):Results.StatusCode(403));
        group.MapGet("/",(SetupStore store)=>{var s=store.Read();return new{revision=s.Revision,presets=s.Venue.Presets};});
        group.MapPut("/",(PresetRequest request,SetupStore store)=>
        {
            var s=store.Change(request.Revision,"GAME_PRESET_SAVE",s=>
            {
                var p=request.Preset;TournamentRules.Need(p is not null&&!string.IsNullOrWhiteSpace(p.Name)&&p.Name.Trim().Length<=100&&p.Status is "A" or "X","Name the preset and choose a valid status.");
                TournamentRules.Need(p.Id is not null&&p.Id.Length<=160&&p.Rules is not null,"Preset ID and rules are required.");TournamentRules.Need(!p.IsDefault||p.Status=="A","The default preset must be active. Choose another default before deactivating it.");p.Name=p.Name.Trim();p.Rules.Name=p.Name;TournamentRules.ValidateRules(p.Rules);
                TournamentRules.Need(!s.Venue.Presets.Any(x=>x.Id!=p.Id&&x.Name.Equals(p.Name,StringComparison.OrdinalIgnoreCase)),"A preset with this name already exists.");
                if(p.IsDefault)foreach(var other in s.Venue.Presets)other.IsDefault=false;
                if(p.Id==""){TournamentRules.Need(s.Venue.Presets.Count<100,"Maximum 100 presets.");p.Id=BusinessIds.Next(s,"GP",4);s.Venue.Presets.Add(p);}
                else{var index=s.Venue.Presets.FindIndex(x=>x.Id==p.Id);TournamentRules.Need(index>=0,"Preset not found.");s.Venue.Presets[index]=p;}
                if(p.IsDefault)ApplyDefault(s,TournamentScheduler.Matches(s).Where(m=>!s.Venue.Bindings.Any(b=>b.MatchId==m.Id)));
            });return new{revision=s.Revision,presets=s.Venue.Presets};
        });
        group.MapPost("/assign",(MatchPresetRequest request,SetupStore store)=>
        {
            var s=store.Change(request.Revision,"MATCH_PRESET_ASSIGN",s=>
            {
                var m=TournamentScheduler.Matches(s).SingleOrDefault(m=>m.Id==request.MatchId);TournamentRules.Need(m is not null&&m.Status==MatchStatus.Scheduled,"Choose an unstarted match.");
                if(m.MatchVersion!=request.MatchVersion)throw new RuleException("Match changed. Refresh before choosing rules.",409);
                GamePresets.Assign(s,m,request.PresetId,request.ConfirmRoundChange);
            });return new{revision=s.Revision};
        });
    }
}
