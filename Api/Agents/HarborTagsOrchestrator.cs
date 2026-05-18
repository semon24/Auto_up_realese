using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using AutoUpRelease.Api;
using AutoUpRelease.Api.Agents.Hubs;

namespace AutoUpRelease.Api.Agents;

public sealed record TagsResult(bool Ok, TagItem[] Items, string? Error);

public sealed class HarborTagsOrchestrator(IHubContext<AgentHub> agentHub)
{
    readonly ConcurrentDictionary<string, TaskCompletionSource<TagsResult>> _pending = new();

    public void Complete(string id, bool ok, TagItem[]? tags, string? error)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;
        if (!_pending.TryRemove(id, out var tcs))
            return;
        if (ok)
            tcs.TrySetResult(new TagsResult(true, tags ?? Array.Empty<TagItem>(), null));
        else
            tcs.TrySetResult(new TagsResult(false, Array.Empty<TagItem>(), error ?? "Harbor error"));
    }

    public async Task<TagsResult> RequestTagsFromAgentAsync(string agentConnectionId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<TagsResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        try
        {
            await agentHub.Clients.Client(agentConnectionId).SendAsync("update_tags", new { Id = id }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _pending.TryRemove(id, out _);
            return new TagsResult(false, Array.Empty<TagItem>(), $"Не удалось отправить update_tags агенту: {ex.Message}");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            return await tcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _pending.TryRemove(id, out _);
            if (!tcs.Task.IsCompleted)
                tcs.TrySetResult(new TagsResult(false, Array.Empty<TagItem>(), "HarborTags timeout"));
            return await tcs.Task;
        }
    }
}
