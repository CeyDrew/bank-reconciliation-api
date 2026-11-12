using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Reconciliation.Infrastructure.Webhooks;

/// <summary>
/// Signature header value: "sha256=" + hex(HMAC-SHA256(secret, "{timestamp}.{body}")).
/// Signing the timestamp with the body lets the receiver reject replays of old requests.
/// </summary>
public static class WebhookSignature
{
    public const string Prefix = "sha256=";

    public static string Create(string secret, long timestamp, string body) =>
        Prefix + Hmac(secret, string.Create(CultureInfo.InvariantCulture, $"{timestamp}.{body}"));

    public static string Hmac(string secret, string message) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(message)));
}
