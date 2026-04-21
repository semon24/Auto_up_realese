using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AutoUpRelease.Agent;

/// <summary>Сообщения проверки пароля с сервера (<c>verify_password</c> → <c>password_verified</c>).</summary>
internal static class AgentWebSocketPasswordMessages
{
    const string TypeVerifyPassword = "verify_password";
    const string TypePasswordVerified = "password_verified";

    internal static async Task<bool> TryHandleAsync(
        ClientWebSocket ws,
        JsonElement root,
        CancellationToken ct)
    {
        if (!root.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
            return false;
        if (!string.Equals(typeEl.GetString(), TypeVerifyPassword, StringComparison.Ordinal))
            return false;
        if (!root.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
            return false;
        var id = idEl.GetString();
        if (string.IsNullOrEmpty(id))
            return false;
        if (!root.TryGetProperty("password", out var pwdEl) || pwdEl.ValueKind != JsonValueKind.String)
            return false;
        var submitted = pwdEl.GetString() ?? "";

        var stored = AgentPasswordBootstrap.TryReadPassword();
        var ok = stored is not null && Utf8FixedTimeEquals(stored, submitted);

        var response = JsonSerializer.Serialize(new
        {
            type = TypePasswordVerified,
            id,
            ok
        });
        var bytes = Encoding.UTF8.GetBytes(response);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        return true;
    }

    static bool Utf8FixedTimeEquals(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        if (ba.Length != bb.Length)
            return false;
        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}
