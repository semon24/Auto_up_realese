using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.SignalR.Client;

namespace AutoUpRelease.Agent;

/// <summary>Сообщения проверки пароля с сервера (<c>verify_password</c> → <c>PasswordVerified</c>).</summary>
internal static class AgentSignalRPasswordMessages
{
    const string ClientMethodVerifyPassword = "verify_password";
    const string ServerMethodPasswordVerified = "PasswordVerified";

    internal static void Register(HubConnection connection, AppOptions appOptions)
    {
        connection.On<VerifyPasswordRequest>(
            ClientMethodVerifyPassword,
            async request =>
            {
                if (request is null || string.IsNullOrWhiteSpace(request.Id))
                {
                    Console.WriteLine(
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] verify_password: пустой request или id");
                    return;
                }

                Console.WriteLine(
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] получена команда verify_password: id={request.Id}");

                var submitted = request.Password ?? "";
                var stored = AgentPasswordBootstrap.TryReadPassword(appOptions.AgentPasswordJsonPath);
                Console.WriteLine(
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] verify_password: сохраненный пароль {(stored is null ? "не найден" : "найден")}");
                var ok = stored is not null && Utf8FixedTimeEquals(stored, submitted);
                Console.WriteLine(
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [agent] verify_password завершен: id={request.Id}, ok={ok}");
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
