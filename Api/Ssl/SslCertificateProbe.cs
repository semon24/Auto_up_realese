using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace AutoUpRelease.Api.Ssl;

public sealed class SslCertificateProbe
{
    public async Task<SslCertificateInfo> ProbeAsync(string domain, CancellationToken cancellationToken = default)
    {
        var normalizedDomain = NormalizeDomain(domain);
        if (string.IsNullOrWhiteSpace(normalizedDomain))
        {
            return new SslCertificateInfo(
                domain,
                null,
                null,
                null,
                false,
                "Домен не указан");
        }

        try
        {
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(normalizedDomain, 443, cancellationToken);

            using var sslStream = new SslStream(
                tcpClient.GetStream(),
                leaveInnerStreamOpen: false,
                userCertificateValidationCallback: static (_, _, _, _) => true);

            await sslStream.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions
                {
                    TargetHost = normalizedDomain,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                },
                cancellationToken);

            if (sslStream.RemoteCertificate is null)
            {
                return new SslCertificateInfo(
                    normalizedDomain,
                    null,
                    null,
                    null,
                    false,
                    "Удаленный сертификат не найден");
            }

            var certificate = new X509Certificate2(sslStream.RemoteCertificate);
            var notBeforeUtc = new DateTimeOffset(certificate.NotBefore.ToUniversalTime());
            var notAfterUtc = new DateTimeOffset(certificate.NotAfter.ToUniversalTime());
            var daysLeft = (int)Math.Floor((notAfterUtc - DateTimeOffset.UtcNow).TotalDays);

            return new SslCertificateInfo(
                normalizedDomain,
                notBeforeUtc,
                notAfterUtc,
                daysLeft,
                daysLeft >= 0,
                null);
        }
        catch (Exception ex)
        {
            return new SslCertificateInfo(
                normalizedDomain,
                null,
                null,
                null,
                false,
                ex.Message);
        }
    }

    static string NormalizeDomain(string? domain)
    {
        var value = domain?.Trim() ?? string.Empty;
        if (value.Length == 0)
            return string.Empty;

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            value = uri.Host;

        return value.Trim().TrimEnd('/').ToLowerInvariant();
    }
}
