using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.SignalR.Client;

namespace AutoUpRelease.Agent;

/// <summary>Сообщения проверки пароля с сервера (<c>verify_password</c> → <c>PasswordVerified</c>).</summary>
internal static class AgentSignalRPasswordMessages
{
    const string ClientMethodVerifyPassword = "verify_password";
    const string ServerMethodPasswordVerified = "PasswordVerified";

    internal static void Register(HubConnection connection)
    {
        connection.On<VerifyPasswordRequest>(
            ClientMethodVerifyPassword,
            async request =>
            {
                if (request is null || string.IsNullOrWhiteSpace(request.Id))
                    return;

                var submitted = request.Password ?? "";
                var stored = AgentPasswordBootstrap.TryReadPassword();
                var ok = stored is not null && Utf8FixedTimeEquals(stored, submitted);
                await connection.InvokeAsync(ServerMethodPasswordVerified, request.Id, ok);
            });
    }

    sealed class VerifyPasswordRequest
    {
        public string? Id { get; set; }
        public string? Password { get; set; }
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
