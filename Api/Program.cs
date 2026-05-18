using AutoUpRelease.Api;
using AutoUpRelease.Api.Agents;
using AutoUpRelease.Api.Agents.Hubs;
using AutoUpRelease.Api.Agents.Json;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;

EnvLoader.LoadOptionalEnvFiles();

var builder = WebApplication.CreateBuilder(args);

var port = builder.Configuration["PORT"];
if (!string.IsNullOrWhiteSpace(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port.Trim()}");

builder.Services.AddCors(o =>
{
    o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});
builder.Services
    .AddOptions<AppOptions>()
    .Bind(builder.Configuration)
    .Validate(
        o => string.IsNullOrWhiteSpace(o.AgentsJsonPath) || Path.IsPathRooted(o.AgentsJsonPath.Trim()),
        "AGENTS_JSON_PATH must be absolute when set")
    .ValidateOnStart();
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<AppOptions>>().Value;
    string? agentsJsonPath = null;
    if (!string.IsNullOrWhiteSpace(options.AgentsJsonPath))
        agentsJsonPath = Path.GetFullPath(options.AgentsJsonPath.Trim());

    return new ResolvedAppOptions
    {
        EnableSwagger = options.EnableSwagger,
        AgentsJsonPath = agentsJsonPath
    };
});
builder.Services.AddSingleton(sp =>
{
    var o = sp.GetRequiredService<ResolvedAppOptions>();
    return new AgentsJsonFile(o.AgentsJsonPath);
});
builder.Services.AddSingleton<AgentServicesSnapshotStore>();
builder.Services.AddSingleton<AgentSessionStore>();
builder.Services.AddSingleton<HarborTagsOrchestrator>();
builder.Services.AddSignalR(options =>
{
    options.KeepAliveInterval = TimeSpan.FromSeconds(10);
    options.ClientTimeoutInterval = TimeSpan.FromMinutes(3);
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Auto up release API",
        Version = "v1",
        Description = "Агенты (SignalR), хабы /hubs/ui и /hubs/agent; теги Harbor — invoke GetHarborTags(agentHostName) на UI-хабе, запрос уходит выбранному агенту.",
    });
});

var app = builder.Build();
var settings = app.Services.GetRequiredService<ResolvedAppOptions>();
AgentsJsonBootstrap.EnsureExists(settings.AgentsJsonPath);
app.UseCors();

var swaggerEnabled = app.Environment.IsDevelopment() || settings.EnableSwagger;
if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/swagger/v1/swagger.json", "v1");
        o.RoutePrefix = "swagger";
    });
}

app.MapGet("/api/health", () => Results.Json(new { ok = true }));

app.MapAgentEndpoints();
app.MapHub<UiHub>("/hubs/ui");
app.MapHub<AgentHub>("/hubs/agent");

app.Run();
