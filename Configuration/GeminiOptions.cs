using System;

namespace PickleballClub.Configuration;

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";
    public const string OfficialBaseUrl = "https://generativelanguage.googleapis.com/v1beta";
    public const string DefaultModel = "gemini-3.5-flash-lite";
    public const string FallbackModel = "gemini-flash-lite-latest";

    public bool Enabled { get; set; } = true;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = DefaultModel;
    public string FallbackModelName { get; set; } = FallbackModel;
    public string BaseUrl { get; set; } = OfficialBaseUrl;
    public int MaxOutputTokens { get; set; } = 512;
    public int TimeoutSeconds { get; set; } = 12;
    public int MaxInputChars { get; set; } = 500;
    public int MaxConversationTurns { get; set; } = 8;
    public int MaxConcurrentRequests { get; set; } = 6;

    public string ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            return ApiKey.Trim();
        }

        string? envKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        return string.IsNullOrWhiteSpace(envKey) ? string.Empty : envKey.Trim();
    }

    public string ResolveModel()
    {
        string raw = string.IsNullOrWhiteSpace(Model) ? DefaultModel : Model.Trim();
        if (raw.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw["models/".Length..];
        }
        return raw;
    }

    public string ResolveBaseUrl()
    {
        string raw = string.IsNullOrWhiteSpace(BaseUrl) ? OfficialBaseUrl : BaseUrl.Trim().TrimEnd('/');
        return raw;
    }

    public (bool IsValid, string ErrorCode, string UserMessage) Validate()
    {
        if (!Enabled)
        {
            return (false, "ASSISTANT_DISABLED",
                "Trợ lý Spike Assistant hiện đang tạm tắt theo cấu hình hệ thống.");
        }

        string baseUrl = ResolveBaseUrl();
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return (false, "GEMINI_INVALID_BASE_URL",
                "Cấu hình địa chỉ máy chủ Gemini API không hợp lệ (bắt buộc HTTPS).");
        }

        string key = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(key) ||
            key.Length < 16 ||
            key.Contains("YOUR_", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "GEMINI_API_KEY_MISSING",
                "Trợ lý AI chưa được cấu hình khóa kết nối Gemini API trên máy chủ.");
        }

        string model = ResolveModel();
        if (string.IsNullOrWhiteSpace(model))
        {
            return (false, "GEMINI_MODEL_MISSING",
                "Chưa cấu hình model Gemini hợp lệ.");
        }

        return (true, string.Empty, string.Empty);
    }

    public override string ToString() =>
        $"GeminiOptions(Enabled={Enabled}, Model={ResolveModel()}, BaseUrl={ResolveBaseUrl()}, HasKey={!string.IsNullOrWhiteSpace(ResolveApiKey())})";
}
