using System.Net.Http.Headers;
using System.Text.Json;

namespace AutoUpRelease.Agent.Integrations.Harbor;

public static class HarborTags
{
    public static async Task<List<TagItem>> FetchAllAsync(
        HttpClient client,
        string registryUrl,
        string username,
        string password,
        string repository)
    {
        if (string.IsNullOrWhiteSpace(registryUrl))
            throw new InvalidOperationException("Задайте REGISTRY_URL (например https://registry.ft-soft.ru).");
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Задайте REGISTRY_USER и REGISTRY_PASSWORD для Harbor.");
        if (string.IsNullOrWhiteSpace(repository))
            throw new InvalidOperationException("Задайте HARBOR_REPOSITORY (например vneocheredi/admin).");

        var baseUrl = registryUrl.Trim().TrimEnd('/');
        if (!baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = "https://" + baseUrl;
        }

        var repo = repository.Trim().Trim('/');
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AutoUpRelease/1.0");

        var tokenUrl =
            $"{baseUrl}/service/token?service=harbor-registry&scope={Uri.EscapeDataString($"repository:{repo}:pull")}";
        using var tokenReq = new HttpRequestMessage(HttpMethod.Get, tokenUrl);
        var basic = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{username}:{password}"));
        tokenReq.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        using var tokenRes = await client.SendAsync(tokenReq);
        var tokenBody = await tokenRes.Content.ReadAsStringAsync();
        if (!tokenRes.IsSuccessStatusCode)
            throw new InvalidOperationException($"Harbor token {(int)tokenRes.StatusCode}: {tokenBody}");

        var tokenObj = JsonSerializer.Deserialize<HarborTokenResponse>(tokenBody, JsonOpt());
        var token = tokenObj?.Token?.Trim();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Harbor token не получен.");

        var tagsUrl = $"{baseUrl}/v2/{repo}/tags/list";
        using var tagsReq = new HttpRequestMessage(HttpMethod.Get, tagsUrl);
        tagsReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var tagsRes = await client.SendAsync(tagsReq);
        var tagsBody = await tagsRes.Content.ReadAsStringAsync();
        if (!tagsRes.IsSuccessStatusCode)
            throw new InvalidOperationException($"Harbor tags {(int)tagsRes.StatusCode}: {tagsBody}");

        var list = JsonSerializer.Deserialize<HarborTagsListResponse>(tagsBody, JsonOpt());
        var tags = list?.Tags ?? new List<string>();

        var result = tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.Ordinal)
            .Select(t => new TagItem(t))
            .OrderByDescending(x => x.Tag, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return result;
    }

    static JsonSerializerOptions JsonOpt() =>
        new() { PropertyNameCaseInsensitive = true };
}
