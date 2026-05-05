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
builder.Services.AddHttpClient();
builder.Services
    .AddOptions<AppOptions>()
    .Bind(builder.Configuration)
    .Validate(o => !string.IsNullOrWhiteSpace(o.DeployProjectsDir), "DEPLOY_PROJECTS_DIR is required")
    .Validate(o => Path.IsPathRooted(o.DeployProjectsDir.Trim()), "DEPLOY_PROJECTS_DIR must be absolute")
    .Validate(
        o => string.IsNullOrWhiteSpace(o.AgentsJsonPath) || Path.IsPathRooted(o.AgentsJsonPath.Trim()),
        "AGENTS_JSON_PATH must be absolute when set")
    .ValidateOnStart();
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<AppOptions>>().Value;
    var deployProjectsDir = Path.GetFullPath(options.DeployProjectsDir.Trim());
    var folderForCopyDir = string.IsNullOrWhiteSpace(options.CopyFolderForDeployPath)
        ? Path.Combine(Directory.GetParent(deployProjectsDir)?.FullName ?? deployProjectsDir, "copy_folder_for_deploy")
        : options.CopyFolderForDeployPath.Trim();
    folderForCopyDir = Path.IsPathRooted(folderForCopyDir)
        ? Path.GetFullPath(folderForCopyDir)
        : Path.GetFullPath(Path.Combine(deployProjectsDir, folderForCopyDir));

    var stateFileNameRaw = string.IsNullOrWhiteSpace(options.StateFile)
        ? "status-dockers.json"
        : options.StateFile.Trim();
    var stateFileName = Path.GetFileName(stateFileNameRaw);
    if (string.IsNullOrWhiteSpace(stateFileName))
        stateFileName = "status-dockers.json";

    if (!Directory.Exists(folderForCopyDir))
        throw new InvalidOperationException($"Папка шаблона не найдена: {folderForCopyDir}. Укажите COPY_FOLDER_FOR_DEPLOY_PATH.");

    string? agentsJsonPath = null;
    if (!string.IsNullOrWhiteSpace(options.AgentsJsonPath))
        agentsJsonPath = Path.GetFullPath(options.AgentsJsonPath.Trim());

    return new ResolvedAppOptions
    {
        EnableSwagger = options.EnableSwagger,
        AgentsJsonPath = agentsJsonPath,
        DeployProjectsDir = deployProjectsDir,
        FolderForCopyDir = folderForCopyDir,
        StateFileName = stateFileName,
        ImageEnvKey = options.ImageEnvKey,
        PostgresPasswordEnvKey = options.PostgresPasswordEnvKey,
        PortAllocation = options.PortAllocation,
        HarborRepository = options.HarborRepository,
        RegistryUrl = options.RegistryUrl,
        RegistryUser = options.RegistryUser,
        RegistryPassword = options.RegistryPassword,
        ServiceLinkEnvKeys = options.ServiceLinkEnvKeys
    };
});
builder.Services.AddSingleton(sp =>
{
    var o = sp.GetRequiredService<ResolvedAppOptions>();
    return new AgentsJsonFile(o.AgentsJsonPath);
});
builder.Services.AddSingleton<AgentServicesSnapshotStore>();
builder.Services.AddSingleton<AgentSessionStore>();
builder.Services.AddSingleton<AgentHubPublisher>();
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
        Description = "Теги Harbor, статус compose, старт/стоп приложения",
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

var deployProjectsDir = settings.DeployProjectsDir;
var folderForCopyDir = settings.FolderForCopyDir;
var stateFileName = settings.StateFileName;
var imageEnvKey = settings.ImageEnvKey;
var postgresPasswordEnvKey = settings.PostgresPasswordEnvKey;
var portAllocationOptions = settings.PortAllocation;
var harborRepository = settings.HarborRepository;
var registryUrl = settings.RegistryUrl;
var registryUser = settings.RegistryUser;
var registryPassword = settings.RegistryPassword;
var serviceLinkEnvKeys = settings.ServiceLinkEnvKeys;

app.MapGet("/api/health", () => Results.Json(new { ok = true }));

app.MapAgentEndpoints();
app.MapHub<AgentsHub>("/hubs/agents");
app.MapHub<AgentTransportHub>("/hubs/agent");

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

app.MapGet("/api/status", async (AgentsJsonFile agentsStatusJsonFile, AgentServicesSnapshotStore snapshotsStore) =>
{
    try
    {
        var freshSnapshots = snapshotsStore.GetFreshStacks(TimeSpan.FromSeconds(35));

        var rawStackDirs = Directory.GetDirectories(deployProjectsDir)
            .Where(d => File.Exists(Path.Combine(d, stateFileName)))
            .ToList();
        var stackDirs = new List<string>(rawStackDirs.Count);
        foreach (var stackDir in rawStackDirs)
        {
            var stackName = Path.GetFileName(stackDir);
            var stackStateFile = Path.Combine(stackDir, stateFileName);
            var operationStatus = await StackStateStore.GetOperationStatusAsync(stackStateFile, stackName);
            if (string.Equals(operationStatus, "deleting", StringComparison.OrdinalIgnoreCase))
                continue;

            stackDirs.Add(stackDir);
        }

        foreach (var stackDir in stackDirs)
        {
            var stackName = Path.GetFileName(stackDir);
            var stackStateFile = Path.Combine(stackDir, stateFileName);
            if (!Directory.Exists(stackDir))
                continue;
            var servicesState = await DockerCompose.GetServicesStateAsync(stackDir);
            await StackStateStore.SaveStackServicesStateAsync(stackStateFile, stackName, servicesState);
        }

        var running = false;
        foreach (var stackDir in stackDirs)
        {
            var stackName = Path.GetFileName(stackDir);
            var stackStateFile = Path.Combine(stackDir, stateFileName);
            if (!await StackStateStore.HasAnyRunningServicesAsync(stackStateFile))
                continue;

            running = true;
            break;
        }

        //Console.WriteLine($"[status] running={running}");

        var stacks = new List<object>(stackDirs.Count);
        var includedTags = new HashSet<string>(StringComparer.Ordinal);
        var runningFromSnapshot = false;
        foreach (var stackDir in stackDirs)
        {
            var stackName = Path.GetFileName(stackDir);
            includedTags.Add(stackName);
            var stackStateFile = Path.Combine(stackDir, stateFileName);
            var stackEnvFile = StackWorkspaceManager.GetStackEnvFile(deployProjectsDir, stackName);
            var info = await StackStateStore.GetStackRuntimeInfoAsync(stackStateFile, stackName);
            var stackServiceLinks = File.Exists(stackEnvFile)
                ? DeployEnvLinks.TryRead(stackEnvFile, serviceLinkEnvKeys)
                : null;

            var hasSnapshot = freshSnapshots.TryGetValue(stackName, out var snapshot);
            var runningValue = hasSnapshot ? snapshot!.Running : info.Running;
            if (runningValue)
                runningFromSnapshot = true;
            var operationTypeValue = hasSnapshot ? snapshot!.OperationType ?? info.OperationType : info.OperationType;
            var operationStatusValue = hasSnapshot ? snapshot!.OperationStatus ?? info.OperationStatus : info.OperationStatus;
            var operationErrorValue = hasSnapshot ? snapshot!.OperationError ?? info.OperationError : info.OperationError;
            object? serviceLinksValue = hasSnapshot
                ? snapshot!.ServiceLinks
                : null;
            var servicesSnapshot = hasSnapshot
                ? snapshot!.Services
                : await DockerCompose.GetServicesStateAsync(stackDir);

            stacks.Add(new
            {
                tag = stackName,
                running = runningValue,
                operationType = operationTypeValue,
                operationStatus = operationStatusValue,
                operationError = operationErrorValue,
                serviceLinks = serviceLinksValue,
                services = servicesSnapshot.ToDictionary(
                    kv => kv.Key,
                    kv => new { state = kv.Value.State, health = kv.Value.Health },
                    StringComparer.Ordinal)
            });
        }

        foreach (var snapshotEntry in freshSnapshots)
        {
            if (includedTags.Contains(snapshotEntry.Key))
                continue;
            if (snapshotEntry.Value.Running)
                runningFromSnapshot = true;

            stacks.Add(new
            {
                tag = snapshotEntry.Key,
                running = snapshotEntry.Value.Running,
                operationType = snapshotEntry.Value.OperationType,
                operationStatus = snapshotEntry.Value.OperationStatus,
                operationError = snapshotEntry.Value.OperationError,
                serviceLinks = snapshotEntry.Value.ServiceLinks,
                services = snapshotEntry.Value.Services.ToDictionary(
                    kv => kv.Key,
                    kv => new { state = kv.Value.State, health = kv.Value.Health },
                    StringComparer.Ordinal)
            });
        }

        if (!running)
            running = runningFromSnapshot;

        return Results.Json(new { running, stacks, agents = agentsStatusJsonFile.ReadSnapshot() });
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
        var activeTag = (string?)null;
        var stackDirs = Directory.GetDirectories(deployProjectsDir)
            .Where(d => File.Exists(Path.Combine(d, stateFileName)))
            .ToList();
        foreach (var stackDir in stackDirs)
        {
            var stackName = Path.GetFileName(stackDir);
            var stackStateFile = Path.Combine(stackDir, stateFileName);
            if (!await StackStateStore.HasAnyRunningServicesAsync(stackStateFile))
                continue;

            activeTag = stackName;
            break;
        }

        if (string.IsNullOrWhiteSpace(activeTag))
            return Results.Json(new { error = "Нет активного стека для аллокации портов" }, statusCode: 400);

        var activeEnvFile = StackWorkspaceManager.GetStackEnvFile(deployProjectsDir, activeTag);
        var allocated = await PortAllocator.AllocateAndWriteEnvAsync(
            deployProjectsDir,
            stateFileName,
            activeTag,
            activeEnvFile,
            keys,
            portAllocationOptions.ScanMin,
            portAllocationOptions.ScanMax,
            ct);
        var activeStateFile = StackWorkspaceManager.GetStackStateFile(deployProjectsDir, activeTag, stateFileName);
        await StackStateStore.SetAllocatedPortsAsync(activeStateFile, activeTag, allocated);
        var payload = allocated.ToDictionary(kv => kv.Key, kv => kv.Value);
        return Results.Json(new { ok = true, allocated = payload });
    }
    catch (Exception e)
    {
        return Results.Json(new { error = e.Message }, statusCode: 500);
    }
});

app.MapPost("/api/stop", async (StopBody? body) =>
{
    try
    {
        var requestedTag = body?.Tag?.Trim();
        var stackDirs = Directory.GetDirectories(deployProjectsDir)
            .Where(d => File.Exists(Path.Combine(d, stateFileName)))
            .ToList();

        if (!string.IsNullOrWhiteSpace(requestedTag))
        {
            stackDirs = stackDirs
                .Where(d => string.Equals(Path.GetFileName(d), requestedTag, StringComparison.Ordinal))
                .ToList();
        }

        foreach (var stackDir in stackDirs)
        {
            var stackName = Path.GetFileName(stackDir);
            var stackStateFile = Path.Combine(stackDir, stateFileName);
            if (!Directory.Exists(stackDir))
                continue;

            await StackStateStore.SetOperationAsync(stackStateFile, stackName, operationType: "stop", operationStatus: "deleting");
            await DockerComposeFullCleanup.CleanupStackAsync(stackDir);
            if (File.Exists(stackStateFile))
                await StackStateStore.SetOperationAsync(stackStateFile, stackName, operationType: "stop", operationStatus: "success");
        }

        var running = false;
        foreach (var stackDir in stackDirs)
        {
            var stackStateFile = Path.Combine(stackDir, stateFileName);
            if (!await StackStateStore.HasAnyRunningServicesAsync(stackStateFile))
                continue;

            running = true;
            break;
        }

        return Results.Json(new { ok = true, running });
    }
    catch (Exception e)
    {
        return Results.Json(new { error = e.Message }, statusCode: 500);
    }
});

app.Run();
