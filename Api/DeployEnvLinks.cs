using System.Text.RegularExpressions;

namespace AutoUpRelease.Api;

public static class DeployEnvLinks
{
    static readonly Regex Placeholder = new(@"\$\{([^}]+)\}", RegexOptions.Compiled);

    /// <param name="deployEnvPath">Путь к deploy/.env</param>
    /// <param name="keys">Имена переменных внутри deploy/.env для каждого сервиса (задаются в appsettings или Api/.env через ServiceLinkEnvKeys__*)</param>
    public static DeployServiceLinks? TryRead(string deployEnvPath, ServiceLinkEnvKeys keys)
    {
        if (!File.Exists(deployEnvPath)) return null;

        static string? Expand(string? template, string path)
        {
            if (string.IsNullOrEmpty(template)) return template;
            return Placeholder.Replace(template, m =>
            {
                var key = m.Groups[1].Value.Trim();
                return EnvFile.ReadTag(path, key) ?? m.Value;
            });
        }

        static string? ReadExpand(string path, string? keyName)
        {
            if (string.IsNullOrWhiteSpace(keyName)) return null;
            return Expand(EnvFile.ReadTag(path, keyName.Trim()), path);
        }

        var admin = ReadExpand(deployEnvPath, keys.Admin);
        var server = ReadExpand(deployEnvPath, keys.Server);
        var portal = ReadExpand(deployEnvPath, keys.Portal);
        var call = ReadExpand(deployEnvPath, keys.Call);

        if (admin == null && server == null && portal == null && call == null)
            return null;

        return new DeployServiceLinks(admin, server, portal, call);
    }
}

public record DeployServiceLinks(
    string? Admin,
    string? Server,
    string? Portal,
    string? Call);
