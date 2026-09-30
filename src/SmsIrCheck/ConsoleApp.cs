using System.Runtime.InteropServices;
using System.Text;

namespace SmsIrCheck;

internal static class ConsoleApp
{
    public static bool WantsConsole(string[] args)
    {
        if (args.Length == 0)
            return false;

        var first = args[0];
        return first.Equals("--probe", StringComparison.OrdinalIgnoreCase)
            || first.Equals("--send", StringComparison.OrdinalIgnoreCase)
            || first.Equals("--help", StringComparison.OrdinalIgnoreCase)
            || first.Equals("-h", StringComparison.OrdinalIgnoreCase)
            || first.Equals("/?", StringComparison.OrdinalIgnoreCase);
    }

    public static int Run(string[] args)
    {
        EnsureConsole();
        var options = Parse(args);
        if (options.ContainsKey("help") || args[0] is "-h" or "/?")
        {
            PrintHelp();
            return 0;
        }

        if (options.ContainsKey("probe"))
            return ProbeAsync().GetAwaiter().GetResult();

        if (options.ContainsKey("send"))
            return SendAsync(options).GetAwaiter().GetResult();

        PrintHelp();
        return 1;
    }

    private static async Task<int> ProbeAsync()
    {
        var state = AppStateStore.Load();
        var key = AppStateStore.KeyFromEnvironment() ?? state.ApiKey;
        using var client = new SmsIrClient();
        var report = await ConnectivityCheck.RunAsync(client, key, CancellationToken.None).ConfigureAwait(false);
        foreach (var line in report.Lines)
            Console.WriteLine(line.Text);
        return report.ExitCode;
    }

    private static async Task<int> SendAsync(Dictionary<string, string> options)
    {
        var state = AppStateStore.Load();
        var key = AppStateStore.KeyFromEnvironment() ?? state.ApiKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            Console.WriteLine("No API key. Set SMSIR_API_KEY or save a key from the window.");
            return 3;
        }

        if (!options.TryGetValue("mobile", out var mobile) || string.IsNullOrWhiteSpace(mobile))
        {
            Console.WriteLine("Missing --mobile.");
            PrintHelp();
            return 1;
        }

        mobile = PhoneNumber.Compact(mobile);
        var verify = options.ContainsKey("template");
        var bulk = options.ContainsKey("line");
        if (verify == bulk)
        {
            Console.WriteLine("Use either --line and --text, or --template and --param.");
            return 1;
        }

        if (!options.ContainsKey("yes"))
        {
            Console.WriteLine("Refusing to send without --yes.");
            Console.WriteLine(verify
                ? $"Would send a verify SMS to {mobile} with template {options["template"]}."
                : $"Would send a bulk SMS to {mobile} from line {options["line"]}.");
            return 2;
        }

        using var client = new SmsIrClient();
        ApiResult result;
        long? messageId;
        if (verify)
        {
            if (!int.TryParse(options["template"], out var templateId))
            {
                Console.WriteLine("--template must be a number.");
                return 1;
            }

            var parameters = ReadParameters(options);
            if (parameters.Count == 0)
            {
                Console.WriteLine("Verify send needs --param NAME=VALUE.");
                return 1;
            }

            Console.WriteLine(SmsIrClient.DescribeRequest("POST", "/v1/send/verify", SmsIrClient.VerifyBody(mobile, templateId, parameters)));
            result = await client.SendVerifyAsync(key, mobile, templateId, parameters, CancellationToken.None).ConfigureAwait(false);
            messageId = SmsJson.FirstMessageId(result.DataJson, bulkArray: false);
        }
        else
        {
            if (!long.TryParse(options["line"], out var lineNumber))
            {
                Console.WriteLine("--line must be a number.");
                return 1;
            }

            if (!options.TryGetValue("text", out var text) || string.IsNullOrWhiteSpace(text))
            {
                Console.WriteLine("Bulk send needs --text.");
                return 1;
            }

            Console.WriteLine(SmsIrClient.DescribeRequest("POST", "/v1/send/bulk", SmsIrClient.BulkBody(lineNumber, text, mobile)));
            result = await client.SendBulkAsync(key, lineNumber, text, mobile, CancellationToken.None).ConfigureAwait(false);
            messageId = SmsJson.FirstMessageId(result.DataJson, bulkArray: true);
        }

        var report = new CheckReport();
        ConnectivityCheck.AppendCall(report, result);
        foreach (var line in report.Lines)
            Console.WriteLine(line.Text);

        if (messageId is > 0)
        {
            Console.WriteLine($"Message id {messageId}");
            var delivery = await client.GetMessageAsync(key, messageId.Value, CancellationToken.None).ConfigureAwait(false);
            var deliveryReport = new CheckReport();
            ConnectivityCheck.AppendCall(deliveryReport, delivery);
            foreach (var line in deliveryReport.Lines)
                Console.WriteLine(line.Text);
            var deliveryState = SmsJson.IntProperty(delivery.DataJson, "deliveryState");
            Console.WriteLine(StatusText.DescribeDelivery(deliveryState));
        }
        else if (messageId == 0)
        {
            Console.WriteLine("message id 0: sms.ir accepted the call and marked this number as blacklisted.");
        }

        if (!result.TransportSucceeded)
            return 2;
        if (result.HttpStatus == 401 || result.Status is 10 or 11 or 12)
            return 3;
        if (result.Status == 1)
            return 0;
        return 4;
    }

    private static List<(string Name, string Value)> ReadParameters(Dictionary<string, string> options)
    {
        var parameters = new List<(string Name, string Value)>();
        foreach (var key in new[] { "param", "param2" })
        {
            if (!options.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
                continue;
            var split = raw.Split('=', 2);
            if (split.Length == 2 && split[0].Trim().Length > 0)
                parameters.Add((split[0].Trim(), split[1]));
        }

        return parameters;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            SmsIrCheck — prove this PC can call the sms.ir API.

            SmsIrCheck.exe
                Open the window.

            SmsIrCheck.exe --probe
                DNS, TCP, TLS, credit, and sender lines. Does not send an SMS.
                Exit 0: key accepted.  2: network failed.  3: key missing or rejected.  4: API rejected the account call.

            SmsIrCheck.exe --send --yes --mobile 0912xxxxxxx --line 3000xxxx --text "hello"
            SmsIrCheck.exe --send --yes --mobile 0912xxxxxxx --template 123456 --param CODE=12345

            The API key is read from SMSIR_API_KEY, otherwise from the key saved in the window.
            """);
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
                continue;
            var key = args[i][2..];
            var value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++i]
                : "true";
            options[key] = value;
        }

        return options;
    }

    private static void EnsureConsole()
    {
        SetConsoleOutputCP(65001);
        var writer = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
        };
        Console.SetOut(writer);
        Console.SetError(writer);
    }

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleOutputCP(uint codePage);
}
