using Microsoft.AspNetCore.SignalR.Client;
using AutoUpRelease.Agent.Integrations.Harbor;

namespace AutoUpRelease.Agent.Commands;

internal static class UpdateHarborTagsSignalRMessages
{
    const string ClientMethodUpdateTags = "update_tags";
    const string ServerMethodTagsUpdated = "TagsUpdated";

    internal static void Register(HubConnection connection, AppOptions appOptions)
    {
        connection.On<UpdateTagsRequest>(
            ClientMethodUpdateTags,
            async request =>
            {
                if (request is null || string.IsNullOrWhiteSpace(request.Id))
                    return;
                
                try
                {
                    using var httpClient = new HttpClient();
                    var tags = await HarborTags.FetchAllAsync(
                        httpClient, 
                        appOptions.RegistryUrl, 
                        appOptions.RegistryUser, 
                        appOptions.RegistryPassword, 
                        appOptions.HarborRepository);

                    await connection.InvokeAsync(ServerMethodTagsUpdated, request.Id, true, tags, (string?)null);
                }
                catch (Exception ex)
                {
                    await connection.InvokeAsync(ServerMethodTagsUpdated, request.Id, false, null, ex.Message);
                }
            });
    }

    private sealed class UpdateTagsRequest
    {
        public string? Id { get; set; }
    }
}
