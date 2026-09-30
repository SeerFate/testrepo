using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SmsIrCheck;

internal sealed class Recipient
{
    public string Name { get; set; } = "";
    public string Mobile { get; set; } = "";
}

internal sealed class AppState
{
    public string ApiKey { get; set; } = "";
    public string LineNumber { get; set; } = "";
    public string SendMethod { get; set; } = "bulk";
    public string MessageText { get; set; } = "";
    public string TemplateId { get; set; } = "";
    public string ParameterName { get; set; } = "CODE";
    public string ParameterValue { get; set; } = "12345";
    public string ParameterName2 { get; set; } = "";
    public string ParameterValue2 { get; set; } = "";
    public List<Recipient> Recipients { get; set; } = [];
    public bool KeyWasUnreadable { get; set; }
    public string? UndecryptedKey { get; set; }
}

internal static class AppStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SmsIrCheck",
        "config.json");

    public static AppState Load()
    {
        var state = new AppState();
        if (!File.Exists(FilePath))
            return state;

        StoredFile stored;
        try
        {
            stored = JsonSerializer.Deserialize<StoredFile>(File.ReadAllText(FilePath), JsonOptions) ?? new StoredFile();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            state.KeyWasUnreadable = true;
            state.UndecryptedKey = null;
            return state;
        }

        state.LineNumber = stored.LineNumber ?? "";
        state.SendMethod = string.Equals(stored.SendMethod, "verify", StringComparison.OrdinalIgnoreCase) ? "verify" : "bulk";
        state.MessageText = stored.MessageText ?? "";
        state.TemplateId = stored.TemplateId ?? "";
        state.ParameterName = string.IsNullOrWhiteSpace(stored.ParameterName) ? "CODE" : stored.ParameterName;
        state.ParameterValue = stored.ParameterValue ?? "12345";
        state.ParameterName2 = stored.ParameterName2 ?? "";
        state.ParameterValue2 = stored.ParameterValue2 ?? "";
        state.Recipients = stored.Recipients ?? [];

        if (string.IsNullOrWhiteSpace(stored.ProtectedApiKey))
            return state;

        try
        {
            var protectedBytes = Convert.FromBase64String(stored.ProtectedApiKey);
            var plain = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            state.ApiKey = Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or DecoderFallbackException)
        {
            state.KeyWasUnreadable = true;
            state.UndecryptedKey = stored.ProtectedApiKey;
        }

        return state;
    }

    public static void Save(AppState state)
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);

        string? protectedKey;
        if (state.ApiKey.Length == 0 && state.KeyWasUnreadable && !string.IsNullOrEmpty(state.UndecryptedKey))
            protectedKey = state.UndecryptedKey;
        else if (state.ApiKey.Length == 0)
            protectedKey = null;
        else
        {
            var protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(state.ApiKey),
                null,
                DataProtectionScope.CurrentUser);
            protectedKey = Convert.ToBase64String(protectedBytes);
            state.KeyWasUnreadable = false;
            state.UndecryptedKey = null;
        }

        var stored = new StoredFile
        {
            ProtectedApiKey = protectedKey,
            LineNumber = state.LineNumber,
            SendMethod = state.SendMethod,
            MessageText = state.MessageText,
            TemplateId = state.TemplateId,
            ParameterName = state.ParameterName,
            ParameterValue = state.ParameterValue,
            ParameterName2 = state.ParameterName2,
            ParameterValue2 = state.ParameterValue2,
            Recipients = state.Recipients,
        };

        File.WriteAllText(FilePath, JsonSerializer.Serialize(stored, JsonOptions));
    }

    public static string? KeyFromEnvironment()
    {
        var value = Environment.GetEnvironmentVariable("SMSIR_API_KEY");
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed class StoredFile
    {
        public string? ProtectedApiKey { get; set; }
        public string? LineNumber { get; set; }
        public string? SendMethod { get; set; }
        public string? MessageText { get; set; }
        public string? TemplateId { get; set; }
        public string? ParameterName { get; set; }
        public string? ParameterValue { get; set; }
        public string? ParameterName2 { get; set; }
        public string? ParameterValue2 { get; set; }
        public List<Recipient>? Recipients { get; set; }
    }
}
