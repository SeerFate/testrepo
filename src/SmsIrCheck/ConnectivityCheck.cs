using System.Globalization;
using System.Text;

namespace SmsIrCheck;

internal enum CheckTone
{
    Info,
    Ok,
    Bad,
    Warn,
    Body,
}

internal sealed record CheckLine(string Text, CheckTone Tone);

internal sealed class CheckReport
{
    public List<CheckLine> Lines { get; } = [];
    public List<long> SenderLines { get; } = [];
    public double? Credit { get; set; }
    public bool NetworkOk { get; set; }
    public bool AuthOk { get; set; }
    public int ExitCode { get; set; } = 1;
    public string Verdict { get; set; } = "";

    public void Add(CheckTone tone, string text) => Lines.Add(new CheckLine(text, tone));
}

internal static class ConnectivityCheck
{
    public static async Task<CheckReport> RunAsync(SmsIrClient client, string? apiKey, CancellationToken cancellationToken)
    {
        var report = new CheckReport();
        report.Add(CheckTone.Info, $"{Environment.MachineName}  {Environment.UserName}  {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        report.Add(CheckTone.Info, "Target  https://api.sms.ir");

        NetworkProbe probe;
        try
        {
            probe = await client.ProbeNetworkAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or System.Net.Sockets.SocketException)
        {
            report.NetworkOk = false;
            report.ExitCode = 2;
            report.Verdict = "FAIL: this PC could not start a connection to api.sms.ir.";
            report.Add(CheckTone.Bad, report.Verdict);
            report.Add(CheckTone.Body, ex.Message);
            return report;
        }

        if (!probe.DnsOk)
        {
            report.NetworkOk = false;
            report.ExitCode = 2;
            report.Verdict = "FAIL: DNS for api.sms.ir did not resolve. This PC cannot find the API host.";
            report.Add(CheckTone.Bad, $"DNS  failed in {probe.DnsMs} ms");
            report.Add(CheckTone.Body, probe.DnsError ?? "Unknown DNS error");
            report.Add(CheckTone.Bad, report.Verdict);
            return report;
        }

        report.Add(CheckTone.Ok, $"DNS  {string.Join(", ", probe.Addresses)}  ({probe.DnsMs} ms)");
        foreach (var address in probe.Addresses.Where(FakeIp.IsBenchmarkRange))
        {
            report.Add(
                CheckTone.Warn,
                $"{address} is inside 198.18.0.0/15. Local proxies use that range for fake-ip DNS. It is not one of sms.ir's published servers (185.211.56.44 and 78.158.166.99). The TLS certificate is what shows whether traffic still reaches sms.ir.");
        }

        if (!probe.TcpOk)
        {
            report.NetworkOk = false;
            report.ExitCode = 2;
            report.Verdict = "FAIL: TCP port 443 to api.sms.ir is blocked. Allow outbound HTTPS, including 185.211.56.44 and 78.158.166.99 if you filter by IP.";
            report.Add(CheckTone.Bad, $"TCP 443  failed in {probe.TcpMs} ms");
            report.Add(CheckTone.Body, probe.TcpError ?? "Unknown TCP error");
            report.Add(CheckTone.Bad, report.Verdict);
            return report;
        }

        report.Add(CheckTone.Ok, $"TCP 443  open  ({probe.TcpMs} ms)");

        if (!probe.TlsOk)
        {
            report.NetworkOk = false;
            report.ExitCode = 2;
            report.Verdict = "FAIL: TLS to api.sms.ir failed. A firewall or intercepting proxy is breaking HTTPS.";
            report.Add(CheckTone.Bad, $"TLS  failed in {probe.TlsMs} ms");
            report.Add(CheckTone.Body, probe.TlsError ?? "Unknown TLS error");
            report.Add(CheckTone.Bad, report.Verdict);
            return report;
        }

        var certTone = probe.CertificateMatchesHost ? CheckTone.Ok : CheckTone.Warn;
        report.Add(certTone, $"TLS  {probe.TlsProtocol}  {probe.CertSubject}  issuer {probe.CertIssuer}");
        if (probe.CertNotAfter is DateTimeOffset expires)
            report.Add(CheckTone.Info, $"Certificate expires {expires:yyyy-MM-dd}");
        if (!probe.CertificateMatchesHost)
            report.Add(CheckTone.Warn, "The certificate subject does not contain sms.ir. Something is intercepting TLS.");

        report.NetworkOk = true;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            report.ExitCode = 3;
            report.Verdict = "NETWORK PASS: DNS, TCP, and TLS to api.sms.ir work. Paste an API key and check again to prove authentication.";
            report.Add(CheckTone.Warn, "API key is empty, so credit and line calls were skipped.");
            report.Add(CheckTone.Ok, report.Verdict);
            return report;
        }

        report.Add(CheckTone.Info, "GET https://api.sms.ir/v1/credit");
        var credit = await client.GetCreditAsync(apiKey, cancellationToken).ConfigureAwait(false);
        AppendCall(report, credit);

        if (!credit.TransportSucceeded)
        {
            report.ExitCode = 2;
            report.Verdict = "FAIL: the HTTPS probe worked, but the credit request did not complete.";
            report.Add(CheckTone.Bad, report.Verdict);
            return report;
        }

        if (credit.Status == 1
            && double.TryParse(credit.DataJson, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
        {
            report.Credit = amount;
            report.AuthOk = true;
            report.Add(CheckTone.Ok, $"Credit  {amount.ToString(CultureInfo.InvariantCulture)}");
        }

        if (credit.HttpStatus == 401 || credit.Status is 10 or 11 or 12 or 13 or 14)
        {
            report.ExitCode = 3;
            report.Verdict = credit.Status == 12
                ? "REACHED: this PC can talk to sms.ir, but the API key is locked to other IP addresses. Add this server's public IP in the developer panel."
                : $"REACHED: this PC can talk to sms.ir, but the key was rejected ({credit.Status}: {StatusText.Describe(credit.Status ?? 0)}).";
            report.Add(credit.Status == 12 ? CheckTone.Warn : CheckTone.Bad, report.Verdict);
            return report;
        }

        report.Add(CheckTone.Info, "GET https://api.sms.ir/v1/line");
        var lines = await client.GetLinesAsync(apiKey, cancellationToken).ConfigureAwait(false);
        AppendCall(report, lines);
        if (lines.TransportSucceeded && lines.Status == 1)
        {
            report.SenderLines.AddRange(SmsJson.ReadLines(lines.DataJson));
            report.Add(
                report.SenderLines.Count > 0 ? CheckTone.Ok : CheckTone.Warn,
                report.SenderLines.Count > 0
                    ? $"Sender lines  {string.Join(", ", report.SenderLines)}"
                    : "The account returned no sender lines. Bulk send needs a line. Verify send uses a template instead.");
        }

        if (report.AuthOk)
        {
            report.ExitCode = 0;
            report.Verdict = "PASS: this PC resolved api.sms.ir, completed TLS, and the API key was accepted. Send an SMS to prove a real message is accepted.";
            report.Add(CheckTone.Ok, report.Verdict);
            return report;
        }

        report.ExitCode = 4;
        report.Verdict = credit.Status is int status
            ? $"REACHED: sms.ir answered status {status} ({StatusText.Describe(status)}). The network path is open."
            : $"REACHED: sms.ir answered HTTP {credit.HttpStatus}. The network path is open.";
        report.Add(CheckTone.Warn, report.Verdict);
        return report;
    }

    public static void AppendCall(CheckReport report, ApiResult result)
    {
        if (!result.TransportSucceeded)
        {
            report.Add(CheckTone.Bad, $"Transport failed in {result.ElapsedMs} ms");
            report.Add(CheckTone.Body, result.TransportError ?? "Unknown transport error");
            return;
        }

        var builder = new StringBuilder();
        builder.Append("HTTP ").Append(result.HttpStatus);
        if (!string.IsNullOrWhiteSpace(result.ReasonPhrase))
            builder.Append(' ').Append(result.ReasonPhrase);
        builder.Append("  ").Append(result.ElapsedMs).Append(" ms");
        if (result.Status is int status)
            builder.Append("  status ").Append(status).Append(" (").Append(StatusText.Describe(status)).Append(')');
        if (!string.IsNullOrWhiteSpace(result.Message))
            builder.Append("  ").Append(result.Message);
        report.Add(result.Status == 1 ? CheckTone.Ok : CheckTone.Warn, builder.ToString());
        report.Add(CheckTone.Info, StatusText.DescribeHttp(result.HttpStatus));

        if (result.RateLimitRemaining != null || result.RateLimitLimit != null || result.RateLimitReset != null)
        {
            report.Add(
                CheckTone.Info,
                $"Rate limit  remaining {result.RateLimitRemaining ?? "?"}  window {result.RateLimitLimit ?? "?"}  resets {result.RateLimitReset ?? "?"}");
        }

        if (!string.IsNullOrWhiteSpace(result.PrettyBody))
            report.Add(CheckTone.Body, result.PrettyBody);
    }
}
