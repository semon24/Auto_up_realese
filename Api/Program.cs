using AutoUpRelease.Api;
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

    return new ResolvedAppOptions
    {
        EnableSwagger = options.EnableSwagger,
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
var settings = app.Services.GetRequiredService<ResolvedAppOptions>();

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
        var rawStackDirs = Directory.GetDirectories(deployProjectsDir)
            .Where(d => File.Exists(Path.Combine(d, stateFileName)))
            .ToList();
        var stackDirs = new List<string>(rawStackDirs.Count);
        foreach (var stackDir in rawStackDirs)
        {
            var stackName = Path.GetFileName(stackDir);
            var stackStateFile = Path.Combine(stackDir, stateFileName);
            if (await StackStateStore.IsDeletingAsync(stackStateFile, stackName))
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
        string? activeTag = null;
        foreach (var stackDir in stackDirs)
        {
            var stackName = Path.GetFileName(stackDir);
            var stackStateFile = Path.Combine(stackDir, stateFileName);
            if (!await StackStateStore.HasAnyRunningServicesAsync(stackStateFile))
                continue;

            running = true;
            activeTag = stackName;
            break;
        }

        var activeEnvFile = !string.IsNullOrWhiteSpace(activeTag)
            ? StackWorkspaceManager.GetStackEnvFile(deployProjectsDir, activeTag)
            : null;
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

    var stackDir = StackWorkspaceManager.GetStackDir(deployProjectsDir, tag);
    var stackEnvFile = StackWorkspaceManager.GetStackEnvFile(deployProjectsDir, tag);
    var stackStateFile = StackWorkspaceManager.GetStackStateFile(deployProjectsDir, tag, stateFileName);

    try
    {
        await DockerCompose.LoginAsync(registryUrl, registryUser, registryPassword);

        StackWorkspaceManager.EnsureStackWorkspace(folderForCopyDir, stackDir);
        if (!File.Exists(stackStateFile))
            await File.WriteAllTextAsync(stackStateFile, "{}");
        await StackStateStore.SetOperationAsync(stackStateFile, tag, operationType: "start", operationStatus: "in_progress");
        await DockerCompose.EnsureVersionedResourcesAsync(
            tag,
            stackEnvFile,
            imageEnvKey,
            postgresPasswordEnvKey);

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
        var waitResult = await DockerCompose.WaitForServicesReadyAsync(
            stackDir,
            timeout: TimeSpan.FromSeconds(600),
            pollInterval: TimeSpan.FromSeconds(2),
            ct);
        if (!waitResult.IsReady)
        {
            var startFailedMessage = waitResult.HasFailure
                ? $"Не удалось успешно запустить стек: {waitResult.Reason}"
                : "Не удалось успешно запустить стек: сервисы не достигли состояния running/exited за отведенное время";
            await StackStateStore.SetOperationAsync(stackStateFile, tag, operationType: "start", operationStatus: "deleting", error: startFailedMessage);
            await DockerComposeFullCleanup.CleanupStackAsync(stackDir);
            return Results.Json(new { error = startFailedMessage }, statusCode: 500);
        }

        var servicesState = waitResult.ServicesState;
        var startStackName = tag;
        await StackStateStore.SaveStackServicesStateAsync(stackStateFile, startStackName, servicesState);
        await StackStateStore.SetOperationAsync(stackStateFile, tag, operationType: "start", operationStatus: "success");
        var running = true;
        var serviceLinks = DeployEnvLinks.TryRead(stackEnvFile, serviceLinkEnvKeys);
        return Results.Json(new { ok = true, running, serviceLinks });
    }
    catch (Exception e)
    {
        await StackStateStore.SetOperationAsync(stackStateFile, tag, operationType: "start", operationStatus: "deleting", error: e.Message);
        await DockerComposeFullCleanup.CleanupStackAsync(stackDir);
        return Results.Json(new { error = e.Message }, statusCode: 500);
    }
});

app.MapPost("/api/stop", async () =>
{
    try
    {
        var stackDirs = Directory.GetDirectories(deployProjectsDir)
            .Where(d => File.Exists(Path.Combine(d, stateFileName)))
            .ToList();

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
