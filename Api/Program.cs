using AutoUpRelease.Api;
using Microsoft.OpenApi.Models;

EnvLoader.LoadOptionalEnvFiles();

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port.Trim()}");

builder.Services.AddCors(o =>
{
    o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});
builder.Services.AddHttpClient();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Auto up release API",
        Version = "v1",
        Description = "Ветки GitHub, статус compose, старт/стоп приложения",
    });
});

var app = builder.Build();
app.UseCors();

var swaggerEnabled = app.Environment.IsDevelopment()
    || string.Equals(app.Configuration["EnableSwagger"], "true", StringComparison.OrdinalIgnoreCase);
if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/swagger/v1/swagger.json", "v1");
        o.RoutePrefix = "swagger";
    });
}

var config = app.Configuration;
var env = app.Environment;

// В Docker можно задать REPO_ROOT, если в конфиге относительные пути к deploy.
var repoRootEnv = Environment.GetEnvironmentVariable("REPO_ROOT")?.Trim();
var repoRoot = !string.IsNullOrWhiteSpace(repoRootEnv)
    ? Path.GetFullPath(repoRootEnv)
    : Path.GetFullPath(Path.Combine(env.ContentRootPath, ".."));

string ResolvePath(string? value, string root, string defaultRelative)
{
    if (string.IsNullOrWhiteSpace(value))
        return Path.Combine(root, defaultRelative);
    var v = value.Trim();
    return Path.IsPathRooted(v) ? Path.GetFullPath(v) : Path.GetFullPath(Path.Combine(root, v));
}

var composeDir = ResolvePath(config["COMPOSE_DIR"], repoRoot, "deploy");
var envFile = !string.IsNullOrWhiteSpace(config["ENV_FILE"]?.Trim())
    ? ResolvePath(config["ENV_FILE"], repoRoot, "deploy")
    : Path.Combine(composeDir, ".env");
var imageEnvKey = config["IMAGE_ENV_KEY"] ?? "IMAGE_TAG";
var branchPrefix = config["BRANCH_PREFIX"] ?? "release/2";
var portAllocationOptions = config.GetSection("PortAllocation").Get<PortAllocationOptions>() ?? new PortAllocationOptions();
var ghOwner = config["GITHUB_OWNER"] ?? "";
var ghRepo = config["GITHUB_REPO"] ?? "";
var ghToken = config["GITHUB_TOKEN"] ?? "";

app.MapGet("/api/health", () => Results.Json(new { ok = true }));

app.MapGet("/api/branches", async (IHttpClientFactory httpFactory) =>
{
    try
    {
        var items = await GitHubBranches.FetchAllAsync(httpFactory, ghOwner, ghRepo, ghToken, branchPrefix);
        return Results.Json(new { items });
    }
    catch (Exception e)
    {
        return Results.Json(new { error = e.Message }, statusCode: 500);
    }
});

app.MapGet("/api/status", async () =>
{
    try
    {
        var running = await DockerCompose.IsRunningAsync(composeDir);
        var activeTag = EnvFile.ReadTag(envFile, imageEnvKey);
        return Results.Json(new { running, activeTag });
    }
    catch (Exception e)
    {
        return Results.Json(new { error = e.Message }, statusCode: 500);
    }
});

app.MapPost("/api/allocate-ports", async (CancellationToken ct) =>
{
    var keys = portAllocationOptions.Keys
        .Select(k => k.Trim())
        .Where(k => k.Length > 0)
        .ToList();
    if (keys.Count == 0)
        return Results.Json(new { error = "В PortAllocation.Keys нет ни одного имени переменной" }, statusCode: 400);
    try
    {
        var allocated = await PortAllocator.AllocateAndWriteEnvAsync(
            envFile,
            keys,
            portAllocationOptions.ScanMin,
            portAllocationOptions.ScanMax,
            ct);
        var payload = allocated.ToDictionary(kv => kv.Key, kv => kv.Value);
        return Results.Json(new { ok = true, allocated = payload });
    }
    catch (Exception e)
    {
        return Results.Json(new { error = e.Message }, statusCode: 500);
    }
});

app.MapPost("/api/start", async (StartBody? body, CancellationToken ct) =>
{
    var tag = body?.Tag?.Trim();
    if (string.IsNullOrEmpty(tag))
        return Results.Json(new { error = "Нужен tag" }, statusCode: 400);
    try
    {
        if (body?.AllocatePorts == true)
        {
            var keys = portAllocationOptions.Keys
                .Select(k => k.Trim())
                .Where(k => k.Length > 0)
                .ToList();
            if (keys.Count == 0)
                return Results.Json(new { error = "allocatePorts: true, но PortAllocation.Keys пуст" }, statusCode: 400);
            await PortAllocator.AllocateAndWriteEnvAsync(
                envFile,
                keys,
                portAllocationOptions.ScanMin,
                portAllocationOptions.ScanMax,
                ct);
        }

        await EnvFile.WriteTagAsync(envFile, imageEnvKey, tag);
        await DockerCompose.RunAsync(composeDir, "up", "-d");
        var running = await DockerCompose.IsRunningAsync(composeDir);
        return Results.Json(new { ok = true, running });
    }
    catch (Exception e)
    {
        return Results.Json(new { error = e.Message }, statusCode: 500);
    }
});

app.MapPost("/api/stop", async () =>
{
    try
    {
        await DockerCompose.RunAsync(composeDir, "down");
        var running = await DockerCompose.IsRunningAsync(composeDir);
        return Results.Json(new { ok = true, running });
    }
    catch (Exception e)
    {
        return Results.Json(new { error = e.Message }, statusCode: 500);
    }
});

app.Run();
