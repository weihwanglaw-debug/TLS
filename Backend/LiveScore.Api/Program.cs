using LiveScore.Api;
using TLS.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true).AddEnvironmentVariables().AddCommandLine(args);
if (builder.Configuration["LiveScore:Database"] != null) throw new InvalidOperationException("SQLite runtime storage is no longer supported. Configure ConnectionStrings:TLSConnection.");
var connection = builder.Configuration.GetConnectionString("TLSConnection") ?? throw new InvalidOperationException("Configure ConnectionStrings:TLSConnection.");
builder.Services.AddDbContextFactory<TLSDbContext>(o => o.UseSqlServer(connection));
if (string.IsNullOrEmpty(builder.Configuration["urls"])) builder.WebHost.UseUrls("http://0.0.0.0:5180");
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 2 * 1024 * 1024);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow);
builder.Services.AddControllers();
builder.Services.AddScoped<SbaLocalAdminFilter>();
builder.Services.AddSingleton<SetupStore>();
builder.Services.AddSingleton<LiveRuntime>();
builder.Services.AddDataProtection();
builder.Services.AddHttpClient("trs-publication", client => client.Timeout = TimeSpan.FromSeconds(10))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<TrsPublication>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TrsPublication>());
builder.Services.AddScoped<TrsIntegration>();
var app = builder.Build();
if (args.Contains("--initialize-sql") || args.Contains("--upgrade-imports") || args.Contains("--upgrade-tournament") || args.Contains("--upgrade-live") || args.Contains("--upgrade-sba") || args.Contains("--upgrade-business-ids") || args.Contains("--clear-test-records") || args.Contains("--schema-script"))
{
    using var db = app.Services.GetRequiredService<IDbContextFactory<TLSDbContext>>().CreateDbContext();
    DatabaseSetup.Run(db, args); return;
}
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    if (context.Request.Path.StartsWithSegments("/api") && context.Request.Method != "GET")
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (origin.Length > 0 && (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Authority != context.Request.Host.Value || uri.Scheme != context.Request.Scheme))
        { context.Response.StatusCode = 403; await context.Response.WriteAsJsonAsync(new { message = "Use the local console to make changes." }); return; }
    }
    try { await next(); if(context.Response.StatusCode==400&&!context.Response.HasStarted&&context.Response.ContentType is null)await context.Response.WriteAsJsonAsync(new{message="Invalid request. Check required fields and remove unexpected properties."}); }
    catch (RuleException ex) { context.Response.StatusCode = ex.Status; await context.Response.WriteAsJsonAsync(new { message = ex.Message }); }
    catch (BadHttpRequestException ex) { context.Response.StatusCode = ex.StatusCode; await context.Response.WriteAsJsonAsync(new { message = "Invalid request. Check the JSON structure and field types." }); }
    catch (Exception ex)
    { app.Logger.LogError(ex, "Local request failed"); context.Response.StatusCode = 500; await context.Response.WriteAsJsonAsync(new { message = "The local server could not save this change. Refresh before retrying." }); }
});
app.MapControllers();
app.MapSetup();
app.MapTournament();
ScheduleEndpoints.Map(app);
app.MapLive();
GamePresets.Map(app);
SeedingEndpoints.Map(app);
TrsIntegration.Map(app);
TrsPublication.Map(app);
app.UseDefaultFiles(); app.UseStaticFiles();
app.Map("/api/{**path}", () => Results.NotFound(new { message = "API endpoint not available." }));
app.MapFallbackToFile("index.html");
app.Run();
