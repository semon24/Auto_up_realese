using AutoUpRelease.Agent;
using AutoUpRelease.Agent.Services.DeleteStackService;
using AutoUpRelease.Agent.Services.RestartStackService;
using AutoUpRelease.Agent.Services.SingleProjectControlService;
using AutoUpRelease.Agent.Services.SingleProjectStateSyncService;
using AutoUpRelease.Agent.Services.StartStackService;
using AutoUpRelease.Agent.Services.StaleStartRecoveryService;
using AutoUpRelease.Agent.Services.StopStackService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<StartStackService>();
builder.Services.AddSingleton<DeleteStackService>();
builder.Services.AddSingleton<RestartStackService>();
builder.Services.AddSingleton<StopStackService>();
builder.Services.AddSingleton<StaleStartRecoveryService>();
builder.Services.AddSingleton<SingleProjectControlService>();
builder.Services.AddSingleton<SingleProjectStateSyncService>();
builder.Services.AddHostedService<AgentWorker>();
builder.Services.AddOptions<AppOptions>()
    .Bind(builder.Configuration)
    .PostConfigure(o => o.AgentHostName = o.AgentHostName?.Trim() ?? string.Empty)
    .PostConfigure(o => o.Type = o.Type?.Trim() ?? string.Empty)
    .PostConfigure(o => o.Mode = o.Mode?.Trim() ?? string.Empty)
    .Validate(o => !string.IsNullOrWhiteSpace(o.ServerBackendUrl), "SERVER_BACKEND_URL is required")
    .Validate(o => !string.IsNullOrWhiteSpace(o.AgentHostName), "AGENT_HOST_NAME is required")
    .Validate(o => !string.IsNullOrWhiteSpace(o.AgentReconnectSeconds), "AGENT_RECONNECT_SECONDS is required")
    .Validate(o => !string.IsNullOrWhiteSpace(o.AgentPasswordJsonPath), "AGENT_PASSWORD_JSON_PATH is required")
    .Validate(o => !string.IsNullOrWhiteSpace(o.AgentsJsonFilePath), "AGENTS_JSON_FILE_PATH is required")
    .Validate(o => !string.IsNullOrWhiteSpace(o.ProjectDeploymentPath), "PROJECT_DEPLOYMENT_PATH is required")
    .Validate(
        o => !string.IsNullOrWhiteSpace(o.ProjectDeploymentPath?.Trim()) &&
            Path.IsPathRooted(o.ProjectDeploymentPath.Trim()),
        "PROJECT_DEPLOYMENT_PATH must be absolute")
    .Validate(
        o => !string.IsNullOrWhiteSpace(o.ProjectDeploymentPath?.Trim()) &&
            Directory.Exists(o.ProjectDeploymentPath.Trim()),
        "PROJECT_DEPLOYMENT_PATH directory does not exist")
    .Validate(
        o => o.IsSingleProjectMode || !string.IsNullOrWhiteSpace(o.CopyFolderForDeployPath),
        "COPY_FOLDER_FOR_DEPLOY_PATH is required in multi-project mode")
    .Validate(
        o => string.IsNullOrWhiteSpace(o.CopyFolderForDeployPath?.Trim()) ||
            Path.IsPathRooted(o.CopyFolderForDeployPath.Trim()),
        "COPY_FOLDER_FOR_DEPLOY_PATH must be absolute")
    .Validate(
        o => string.IsNullOrWhiteSpace(o.CopyFolderForDeployPath?.Trim()) ||
            Directory.Exists(o.CopyFolderForDeployPath.Trim()),
        "COPY_FOLDER_FOR_DEPLOY_PATH directory does not exist")
    .Validate(
        o => string.IsNullOrWhiteSpace(o.StackSingleName) || IsValidStackSingleName(o.StackSingleName),
        "STACK_SINGLE_NAME contains invalid characters")
    .Validate(o => !string.IsNullOrWhiteSpace(o.StateProjectFileName), "STATE_PROJECT_FILE_NAME is required")
    .Validate(o => !string.IsNullOrWhiteSpace(o.ImageEnvKey), "IMAGE_ENV_KEY is required")
    .Validate(o => !string.IsNullOrWhiteSpace(o.HarborRepository), "HARBOR_REPOSITORY is required")
    .Validate(o => !string.IsNullOrWhiteSpace(o.RegistryUrl), "REGISTRY_URL is required")
    .Validate(o => !string.IsNullOrWhiteSpace(o.RegistryUser), "REGISTRY_USER is required")
    .Validate(o => !string.IsNullOrWhiteSpace(o.RegistryPassword), "REGISTRY_PASSWORD is required")
    .Validate(
        o =>
            !string.IsNullOrWhiteSpace(o.AgentPasswordJsonPath?.Trim())
            && Path.IsPathRooted(o.AgentPasswordJsonPath.Trim()),
        "AGENT_PASSWORD_JSON_PATH must be absolute")
    .Validate(
        o =>
            !string.IsNullOrWhiteSpace(o.AgentsJsonFilePath?.Trim())
            && Path.IsPathRooted(o.AgentsJsonFilePath.Trim()),
        "AGENTS_JSON_FILE_PATH must be absolute")
    .ValidateOnStart();


var app = builder.Build();
var appOptions = app.Services.GetRequiredService<IOptions<AppOptions>>().Value;
AgentPasswordBootstrap.WritePassword(appOptions.AgentPasswordJsonPath);

var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    Console.WriteLine(
        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] ApplicationStopping (завершение хоста инициировано)");
});

await app.RunAsync();

static bool IsValidStackSingleName(string? value)
{
    var normalized = value?.Trim();
    if (string.IsNullOrWhiteSpace(normalized))
        return true;

    return normalized.All(char.IsLetterOrDigit);
}
