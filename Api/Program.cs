using AutoUpRelease.Api;
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

var composeDir = string.IsNullOrWhiteSpace(config["COMPOSE_DIR"])
    ? "/opt/vneocheredi"
    : config["COMPOSE_DIR"]!.Trim();

if (!Path.IsPathRooted(composeDir))
    throw new InvalidOperationException("COMPOSE_DIR должен быть абсолютным путем до папки с docker-compose.yml.");

composeDir = Path.GetFullPath(composeDir);

var envFile = string.IsNullOrWhiteSpace(config["ENV_FILE"])
    ? Path.Combine(composeDir, ".env")
    : config["ENV_FILE"]!.Trim();

if (!Path.IsPathRooted(envFile))
    throw new InvalidOperationException("ENV_FILE должен быть абсолютным путем до файла .env целевого приложения.");

envFile = Path.GetFullPath(envFile);
if (!string.Equals(Path.GetFileName(envFile), ".env", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("ENV_FILE должен указывать на файл .env целевого приложения.");
var imageEnvKey = config["IMAGE_ENV_KEY"] ?? "IMAGE_TAG";
var branchPrefix = config["BRANCH_PREFIX"] ?? "release/2";
var portAllocationOptions = config.GetSection("PortAllocation").Get<PortAllocationOptions>() ?? new PortAllocationOptions();
var ghOwner = config["GITHUB_OWNER"] ?? "";
var ghRepo = config["GITHUB_REPO"] ?? "";
var ghToken = config["GITHUB_TOKEN"] ?? "";
var serviceLinkEnvKeys = config.GetSection("ServiceLinkEnvKeys").Get<ServiceLinkEnvKeys>() ?? new ServiceLinkEnvKeys();

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
        var serviceLinks = running ? DeployEnvLinks.TryRead(envFile, serviceLinkEnvKeys) : null;
        return Results.Json(new { running, activeTag, serviceLinks });
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
        var serviceLinks = running ? DeployEnvLinks.TryRead(envFile, serviceLinkEnvKeys) : null;
        return Results.Json(new { ok = true, running, serviceLinks });
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
