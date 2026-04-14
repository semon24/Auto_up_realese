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
        Description = "Теги Harbor, статус compose, старт/стоп приложения",
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
var templateDir = string.IsNullOrWhiteSpace(config["COPY_FOLDER_FOR_DEPLOY_PATH"])
    ? Path.Combine(Directory.GetParent(composeDir)?.FullName ?? composeDir, "copy_folder_for_deploy")
    : config["COPY_FOLDER_FOR_DEPLOY_PATH"]!.Trim();
templateDir = Path.IsPathRooted(templateDir)
    ? Path.GetFullPath(templateDir)
    : Path.GetFullPath(Path.Combine(composeDir, templateDir));
var statePath = string.IsNullOrWhiteSpace(config["STATE_FILE"])
    ? composeDir
    : config["STATE_FILE"]!.Trim();
statePath = Path.IsPathRooted(statePath)
    ? Path.GetFullPath(statePath)
    : Path.GetFullPath(Path.Combine(composeDir, statePath));
var stateFile = string.Equals(Path.GetExtension(statePath), ".json", StringComparison.OrdinalIgnoreCase)
    ? statePath
    : Path.Combine(statePath, "stateStack.json");
var imageEnvKey = config["IMAGE_ENV_KEY"] ?? "IMAGE_TAG";
var portAllocationOptions = config.GetSection("PortAllocation").Get<PortAllocationOptions>() ?? new PortAllocationOptions();
var harborRepository = config["HARBOR_REPOSITORY"] ?? "vneocheredi/admin";
var registryUrl = config["REGISTRY_URL"] ?? "";
var registryUser = config["REGISTRY_USER"] ?? "";
var registryPassword = config["REGISTRY_PASSWORD"] ?? "";
var serviceLinkEnvKeys = config.GetSection("ServiceLinkEnvKeys").Get<ServiceLinkEnvKeys>() ?? new ServiceLinkEnvKeys();

if (!Directory.Exists(templateDir))
    throw new InvalidOperationException($"Папка шаблона не найдена: {templateDir}. Укажите COPY_FOLDER_FOR_DEPLOY_PATH.");

string GetStackDir(string tag) => Path.Combine(composeDir, tag);
string GetStackEnvFile(string tag) => Path.Combine(GetStackDir(tag), ".env");

void CopyDirectoryRecursive(string sourceDir, string targetDir)
{
    Directory.CreateDirectory(targetDir);

    foreach (var file in Directory.GetFiles(sourceDir))
    {
        var destFile = Path.Combine(targetDir, Path.GetFileName(file));
        if (!File.Exists(destFile))
            File.Copy(file, destFile);
    }

    foreach (var sourceSubDir in Directory.GetDirectories(sourceDir))
    {
        var destSubDir = Path.Combine(targetDir, Path.GetFileName(sourceSubDir));
        CopyDirectoryRecursive(sourceSubDir, destSubDir);
    }
}

void EnsureStackWorkspace(string tag)
{
    var stackDir = GetStackDir(tag);
    if (!Directory.Exists(stackDir))
    {
        Directory.CreateDirectory(stackDir);
        CopyDirectoryRecursive(templateDir, stackDir);
    }

    var stackEnvFile = GetStackEnvFile(tag);
    if (!File.Exists(stackEnvFile))
    {
        var templateEnv = Path.Combine(templateDir, ".env");
        if (File.Exists(templateEnv))
            File.Copy(templateEnv, stackEnvFile);
        else
            File.WriteAllText(stackEnvFile, string.Empty);
    }
}

app.MapGet("/api/health", () => Results.Json(new { ok = true }));

app.MapGet("/api/tags", async (IHttpClientFactory httpFactory) =>
{
    try
    {
        var items = await HarborTags.FetchAllAsync(
            httpFactory,
            registryUrl,
            registryUser,
            registryPassword,
            harborRepository);

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
        var stackNames = await StackStateStore.GetStackNamesAsync(stateFile);
        foreach (var stackName in stackNames)
        {
            var stackDir = GetStackDir(stackName);
            if (!Directory.Exists(stackDir))
                continue;

            var servicesState = await DockerCompose.GetServicesStateAsync(stackDir);
            await StackStateStore.WriteAsync(stateFile, stackName, servicesState);
        }

        var running = await StackStateStore.HasAnyRunningServicesAsync(stateFile);
        var activeTag = await StackStateStore.GetAnyRunningStackNameAsync(stateFile);
        var activeEnvFile = !string.IsNullOrWhiteSpace(activeTag) ? GetStackEnvFile(activeTag) : null;
        var serviceLinks = running && !string.IsNullOrWhiteSpace(activeEnvFile)
            ? DeployEnvLinks.TryRead(activeEnvFile, serviceLinkEnvKeys)
            : null;

        Console.WriteLine($"[status] running={running}, activeTag={activeTag ?? "<null>"}");
        Console.WriteLine($"[status] serviceLinks={System.Text.Json.JsonSerializer.Serialize(serviceLinks)}");

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
        var activeTag = await StackStateStore.GetAnyRunningStackNameAsync(stateFile);
        if (string.IsNullOrWhiteSpace(activeTag))
            return Results.Json(new { error = "Нет активного стека для аллокации портов" }, statusCode: 400);

        var activeEnvFile = GetStackEnvFile(activeTag);
        var allocated = await PortAllocator.AllocateAndWriteEnvAsync(
            activeEnvFile,
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
        await DockerCompose.LoginAsync(registryUrl, registryUser, registryPassword);

        EnsureStackWorkspace(tag);
        var stackDir = GetStackDir(tag);
        var stackEnvFile = GetStackEnvFile(tag);
        await DockerCompose.EnsureVersionedResourcesAsync(tag, stackEnvFile, imageEnvKey);

        if (body?.AllocatePorts != false)
        {
            var keys = portAllocationOptions.Keys
                .Select(k => k.Trim())
                .Where(k => k.Length > 0)
                .ToList();
            if (keys.Count == 0)
                return Results.Json(new { error = "allocatePorts включен, но PortAllocation.Keys пуст" }, statusCode: 400);
            await PortAllocator.AllocateAndWriteEnvAsync(
                stackEnvFile,
                keys,
                portAllocationOptions.ScanMin,
                portAllocationOptions.ScanMax,
                ct);
        }

        await EnvFile.WriteTagAsync(stackEnvFile, imageEnvKey, tag);
        await DockerCompose.RunAsync(stackDir, null, "up", "-d");
        var servicesState = await DockerCompose.GetServicesStateAsync(stackDir);
        var startStackName = tag;
        await StackStateStore.WriteAsync(stateFile, startStackName, servicesState);
        var running = await StackStateStore.HasAnyRunningServicesAsync(stateFile);
        var serviceLinks = running ? DeployEnvLinks.TryRead(stackEnvFile, serviceLinkEnvKeys) : null;
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
        var stackNames = await StackStateStore.GetStackNamesAsync(stateFile);
        foreach (var stackName in stackNames)
        {
            var stackDir = GetStackDir(stackName);
            if (!Directory.Exists(stackDir))
            {
                await StackStateStore.DeleteStackAsync(stateFile, stackName);
                continue;
            }

            await DockerCompose.RunAsync(stackDir, null, "down");
            await StackStateStore.DeleteStackAsync(stateFile, stackName);
        }

        var running = await StackStateStore.HasAnyRunningServicesAsync(stateFile);
        return Results.Json(new { ok = true, running });
    }
    catch (Exception e)
    {
        return Results.Json(new { error = e.Message }, statusCode: 500);
    }
});

app.Run();
