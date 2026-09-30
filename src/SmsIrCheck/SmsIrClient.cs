using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SmsIrCheck;

internal sealed class ApiResult
{
    public bool TransportSucceeded { get; init; }
    public int? HttpStatus { get; init; }
    public string? ReasonPhrase { get; init; }
    public string PrettyBody { get; init; } = "";
    public long ElapsedMs { get; init; }
    public string? TransportError { get; init; }
    public int? Status { get; init; }
    public string? Message { get; init; }
    public string? DataJson { get; init; }
    public string? RateLimitLimit { get; init; }
    public string? RateLimitRemaining { get; init; }
    public string? RateLimitReset { get; init; }
}

internal sealed class NetworkProbe
{
    public bool DnsOk { get; init; }
    public string[] Addresses { get; init; } = [];
    public long DnsMs { get; init; }
    public string? DnsError { get; init; }
    public bool TcpOk { get; init; }
    public long TcpMs { get; init; }
    public string? TcpError { get; init; }
    public bool TlsOk { get; init; }
    public long TlsMs { get; init; }
    public string? TlsProtocol { get; init; }
    public string? CertSubject { get; init; }
    public string? CertIssuer { get; init; }
    public DateTimeOffset? CertNotAfter { get; init; }
    public string? TlsError { get; init; }
    public bool CertificateMatchesHost { get; init; }
}

internal sealed class SmsIrClient : IDisposable
{
    public const string Host = "api.sms.ir";
    public const string BaseUrl = "https://api.sms.ir";

    private static readonly JsonSerializerOptions RequestJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly HttpClient _http;

    public SmsIrClient()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(20),
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(40),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SmsIrCheck/1.0");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<NetworkProbe> ProbeNetworkAsync(CancellationToken cancellationToken)
    {
        var dnsWatch = Stopwatch.StartNew();
        string[] addresses;
        try
        {
            var resolved = await Dns.GetHostAddressesAsync(Host, cancellationToken).ConfigureAwait(false);
            dnsWatch.Stop();
            addresses = resolved.Select(address => address.ToString()).Distinct().ToArray();
            if (addresses.Length == 0)
            {
                return new NetworkProbe
                {
                    DnsMs = dnsWatch.ElapsedMilliseconds,
                    DnsError = "DNS returned no addresses.",
                };
            }
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            dnsWatch.Stop();
            return new NetworkProbe
            {
                DnsMs = dnsWatch.ElapsedMilliseconds,
                DnsError = ex.Message,
            };
        }

        var tcpWatch = Stopwatch.StartNew();
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(Host, 443, cancellationToken).ConfigureAwait(false);
            tcpWatch.Stop();
        }
        catch (Exception ex) when (ex is SocketException or IOException or TimeoutException)
        {
            tcpWatch.Stop();
            return new NetworkProbe
            {
                DnsOk = true,
                Addresses = addresses,
                DnsMs = dnsWatch.ElapsedMilliseconds,
                TcpMs = tcpWatch.ElapsedMilliseconds,
                TcpError = ex.Message,
            };
        }

        var tlsWatch = Stopwatch.StartNew();
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(Host, 443, cancellationToken).ConfigureAwait(false);
            using var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = Host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            }, cancellationToken).ConfigureAwait(false);
            tlsWatch.Stop();

            var cert = ssl.RemoteCertificate;
            var subject = cert?.Subject;
            var issuer = cert?.Issuer;
            DateTimeOffset? notAfter = null;
            if (cert != null)
            {
                var parsed = new System.Security.Cryptography.X509Certificates.X509Certificate2(cert);
                notAfter = parsed.NotAfter;
                subject ??= parsed.Subject;
                issuer ??= parsed.Issuer;
            }

            return new NetworkProbe
            {
                DnsOk = true,
                Addresses = addresses,
                DnsMs = dnsWatch.ElapsedMilliseconds,
                TcpOk = true,
                TcpMs = tcpWatch.ElapsedMilliseconds,
                TlsOk = true,
                TlsMs = tlsWatch.ElapsedMilliseconds,
                TlsProtocol = ssl.SslProtocol.ToString(),
                CertSubject = subject,
                CertIssuer = issuer,
                CertNotAfter = notAfter,
                CertificateMatchesHost = subject?.Contains("sms.ir", StringComparison.OrdinalIgnoreCase) == true,
            };
        }
        catch (Exception ex) when (ex is AuthenticationException or IOException or SocketException or TimeoutException)
        {
            tlsWatch.Stop();
            return new NetworkProbe
            {
                DnsOk = true,
                Addresses = addresses,
                DnsMs = dnsWatch.ElapsedMilliseconds,
                TcpOk = true,
                TcpMs = tcpWatch.ElapsedMilliseconds,
                TlsMs = tlsWatch.ElapsedMilliseconds,
                TlsError = ex.Message,
            };
        }
    }

    public Task<ApiResult> GetCreditAsync(string apiKey, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, "/v1/credit", apiKey, null, cancellationToken);

    public Task<ApiResult> GetLinesAsync(string apiKey, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, "/v1/line", apiKey, null, cancellationToken);

    public Task<ApiResult> GetMessageAsync(string apiKey, long messageId, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, $"/v1/send/{messageId}", apiKey, null, cancellationToken);

    public static string BulkBody(long lineNumber, string messageText, string mobile) =>
        JsonSerializer.Serialize(new
        {
            lineNumber,
            messageText,
            mobiles = new[] { mobile },
        }, RequestJson);

    public static string VerifyBody(string mobile, int templateId, IReadOnlyList<(string Name, string Value)> parameters) =>
        JsonSerializer.Serialize(new
        {
            mobile,
            templateId,
            parameters = parameters.Select(parameter => new { name = parameter.Name, value = parameter.Value }).ToArray(),
        }, RequestJson);

    public Task<ApiResult> SendBulkAsync(
        string apiKey,
        long lineNumber,
        string messageText,
        string mobile,
        CancellationToken cancellationToken)
    {
        var json = BulkBody(lineNumber, messageText, mobile);
        return SendAsync(HttpMethod.Post, "/v1/send/bulk", apiKey, JsonContent(json), cancellationToken);
    }

    public Task<ApiResult> SendVerifyAsync(
        string apiKey,
        string mobile,
        int templateId,
        IReadOnlyList<(string Name, string Value)> parameters,
        CancellationToken cancellationToken)
    {
        var json = VerifyBody(mobile, templateId, parameters);
        return SendAsync(HttpMethod.Post, "/v1/send/verify", apiKey, JsonContent(json), cancellationToken);
    }

    public static string DescribeRequest(string method, string path, string jsonBody)
    {
        var builder = new StringBuilder();
        builder.Append(method).Append(' ').Append(BaseUrl).Append(path);
        builder.AppendLine();
        builder.AppendLine("Accept: application/json");
        builder.AppendLine("x-api-key: (not shown)");
        if (jsonBody.Length > 0)
        {
            builder.AppendLine("Content-Type: application/json; charset=utf-8");
            builder.AppendLine(Pretty(jsonBody));
        }

        return builder.ToString().TrimEnd();
    }

    private async Task<ApiResult> SendAsync(
        HttpMethod method,
        string path,
        string apiKey,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey.Trim());

        var watch = Stopwatch.StartNew();
        try
        {
            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            watch.Stop();
            return Parse(response, body, watch.ElapsedMilliseconds, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            watch.Stop();
            return Failed(watch.ElapsedMilliseconds, "Timed out after 30 seconds. Outbound HTTPS to api.sms.ir did not finish.");
        }
        catch (HttpRequestException ex)
        {
            watch.Stop();
            return Failed(watch.ElapsedMilliseconds, ex.InnerException?.Message ?? ex.Message);
        }
    }

    private static ApiResult Parse(HttpResponseMessage response, string body, long elapsedMs, string? transportError)
    {
        int? status = null;
        string? message = null;
        string? dataJson = null;
        var pretty = string.IsNullOrWhiteSpace(body) ? "(empty body)" : body;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "null" : body);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (document.RootElement.TryGetProperty("status", out var statusElement) && statusElement.TryGetInt32(out var code))
                    status = code;
                if (document.RootElement.TryGetProperty("message", out var messageElement))
                    message = messageElement.GetString();
                if (document.RootElement.TryGetProperty("data", out var dataElement) && dataElement.ValueKind != JsonValueKind.Null)
                    dataJson = dataElement.GetRawText();
                pretty = JsonSerializer.Serialize(document.RootElement, PrettyJson);
            }
        }
        catch (JsonException)
        {
            pretty = body;
        }

        return new ApiResult
        {
            TransportSucceeded = true,
            HttpStatus = (int)response.StatusCode,
            ReasonPhrase = response.ReasonPhrase,
            PrettyBody = pretty,
            ElapsedMs = elapsedMs,
            TransportError = transportError,
            Status = status,
            Message = message,
            DataJson = dataJson,
            RateLimitLimit = Header(response, "X-Rate-Limit-Limit"),
            RateLimitRemaining = Header(response, "X-Rate-Limit-Remaining"),
            RateLimitReset = Header(response, "X-Rate-Limit-Reset"),
        };
    }

    private static ApiResult Failed(long elapsedMs, string error) => new()
    {
        TransportSucceeded = false,
        ElapsedMs = elapsedMs,
        TransportError = error,
        PrettyBody = "",
    };

    private static StringContent JsonContent(string json)
    {
        var content = new StringContent(json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        return content;
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(", ", values) : null;

    private static string Pretty(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement, PrettyJson);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    public void Dispose() => _http.Dispose();
}

internal static class SmsJson
{
    public static long? FirstMessageId(string? dataJson, bool bulkArray)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
            return null;

        try
        {
            using var document = JsonDocument.Parse(dataJson);
            var root = document.RootElement;
            if (bulkArray
                && root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("messageIds", out var ids)
                && ids.ValueKind == JsonValueKind.Array)
            {
                foreach (var id in ids.EnumerateArray())
                {
                    if (id.ValueKind == JsonValueKind.Null)
                        return null;
                    if (id.TryGetInt64(out var number))
                        return number;
                }

                return null;
            }

            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("messageId", out var single)
                && single.ValueKind != JsonValueKind.Null
                && single.TryGetInt64(out var messageId))
                return messageId;
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    public static string? TextProperty(string? dataJson, string name)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
            return null;

        try
        {
            using var document = JsonDocument.Parse(dataJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(name, out var property))
                return null;

            return property.ValueKind == JsonValueKind.String ? property.GetString() : property.GetRawText();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static IReadOnlyList<long> ReadLines(string? dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
            return [];

        try
        {
            using var document = JsonDocument.Parse(dataJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            var lines = new List<long>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.TryGetInt64(out var line))
                    lines.Add(line);
            }

            return lines;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static int? IntProperty(string? dataJson, string name)
    {
        var text = TextProperty(dataJson, name);
        return int.TryParse(text, out var value) ? value : null;
    }

    public static long? LongProperty(string? dataJson, string name)
    {
        var text = TextProperty(dataJson, name);
        return long.TryParse(text, out var value) ? value : null;
    }
}

internal static class FakeIp
{
    public static bool IsBenchmarkRange(string address)
    {
        if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return false;

        var bytes = ip.GetAddressBytes();
        return bytes[0] == 198 && bytes[1] is 18 or 19;
    }
}
