using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AutoUpRelease.Api;

public static class GitHubBranches
{
    public static async Task<List<BranchItem>> FetchAllAsync(
        IHttpClientFactory httpFactory,
        string owner,
        string repo,
        string token,
        string branchPrefix)
    {
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            throw new InvalidOperationException("Задайте GITHUB_OWNER и GITHUB_REPO (appsettings или переменные окружения).");

        var client = httpFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AutoUpRelease/1.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrWhiteSpace(token))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());

        var list = new List<BranchItem>();
        var url = $"https://api.github.com/repos/{owner}/{repo}/branches?per_page=100";
        var hasMorePages = true;

        while (hasMorePages)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var res = await client.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException($"GitHub {(int)res.StatusCode}: {body}");

            var page = JsonSerializer.Deserialize<GitHubBranch[]>(body, JsonOpt()) ?? Array.Empty<GitHubBranch>();
            foreach (var b in page)
            {
                if (b.Name is { } name && name.StartsWith(branchPrefix, StringComparison.Ordinal))
                {
                    var tag = BranchToTag(name);
                    if (tag != null)
                        list.Add(new BranchItem(name, tag));
                }
            }

            hasMorePages = false;
            if (res.Headers.TryGetValues("Link", out var links))
            {
                var linkHeader = links.FirstOrDefault();
                var m = Regex.Match(linkHeader, @"<([^>]+)>;\s*rel=""next""");
                if (m.Success)
                {
                    url = m.Groups[1].Value;
                    hasMorePages = true;
                }
            }
        }

        list.Sort((a, b) => string.Compare(a.Tag, b.Tag, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    static string? BranchToTag(string branchName)
    {
        const string prefix = "release/";
        if (!branchName.StartsWith(prefix, StringComparison.Ordinal))
            return null;
        var after = branchName[prefix.Length..];
        return NormalizeTag(after);
    }

    static string NormalizeTag(string suffix)
    {
        var t = suffix.Trim();
        if (t.Length == 0) return t;
        var parts = t.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
            return $"{parts[0]}.{parts[1]}.0";
        return t;
    }

    static JsonSerializerOptions JsonOpt() =>
        new() { PropertyNameCaseInsensitive = true };
}
