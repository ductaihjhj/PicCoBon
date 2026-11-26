# SPIKE OS — GEMINI AI ASSISTANT CONFIGURATION GUIDE

## 1. Configuration Options (`GeminiOptions`)

All assistant settings are bound to the `"Gemini"` configuration section (`Configuration/GeminiOptions.cs`) and configured in `appsettings.json` (with empty `"ApiKey": ""`).

```json
{
  "Gemini": {
    "Enabled": true,
    "ApiKey": "",
    "Model": "gemini-3.5-flash-lite",
    "FallbackModelName": "gemini-3.1-flash-lite",
    "BaseUrl": "https://generativelanguage.googleapis.com/v1beta",
    "TimeoutSeconds": 18,
    "MaxInputChars": 500,
    "MaxOutputTokens": 600,
    "MaxConversationTurns": 8,
    "PermitLimitPerMinute": 15
  }
}
```

| Setting | Default | Description |
| :--- | :--- | :--- |
| `Gemini:Enabled` | `true` | Master kill-switch. When `false`, `/api/assistant/status` returns `ready: false` and `/api/assistant/chat` returns `503 ASSISTANT_DISABLED`. |
| `Gemini:ApiKey` | `""` | Google Gemini API key. Loaded from `.NET User Secrets` or `GEMINI_API_KEY` environment variable. Never commit a real key to `appsettings.json`. |
| `Gemini:Model` | `"gemini-3.5-flash-lite"` | Primary Gemini model used for multi-turn chat and `functionDeclarations` tool calling. |
| `Gemini:FallbackModelName` | `"gemini-3.1-flash-lite"` | Secondary Gemini model automatically used if the primary model returns HTTP `404`, `408`, `429`, or `5xx`. |
| `Gemini:BaseUrl` | `"https://generativelanguage.googleapis.com/v1beta"` | HTTPS Google Generative Language REST API endpoint. Non-HTTPS URLs are rejected by `GeminiOptions.Validate()`. |
| `Gemini:TimeoutSeconds` | `18` | Per-request timeout (clamped to `5..45` seconds). |
| `Gemini:MaxInputChars` | `500` | Maximum user message length in characters (clamped to `100..1200`). |
| `Gemini:MaxOutputTokens` | `600` | Maximum tokens generated per Gemini response (clamped to `128..1024`). |
| `Gemini:MaxConversationTurns` | `8` | Maximum prior user/assistant turns retained in `IMemoryCache` per conversation (30-minute sliding expiration, 2-hour absolute expiration). |
| `Gemini:PermitLimitPerMinute` | `15` | Per-IP rate limit enforced by the `"AssistantLimiter"` fixed-window policy. |

---

## 2. Setting the API Key Safely

### Option A: .NET User Secrets (Recommended for Local Development)

The project `PickleballClub.csproj` includes `UserSecretsId` (`f3577ece-adbf-45e5-8ad3-e32c14fcc3a2`). Configure or rotate your key using:

```powershell
dotnet user-secrets set "Gemini:ApiKey" "<YOUR_GEMINI_API_KEY>" --project e:\pickleball\PickleballClub.csproj
```

Verify that the secret is configured (without printing the key in logs):

```powershell
dotnet user-secrets list --project e:\pickleball\PickleballClub.csproj
```

### Option B: Environment Variable (Recommended for Production / Containers / CI)

Set either `GEMINI_API_KEY` or `Gemini__ApiKey`:

```powershell
# PowerShell
$env:GEMINI_API_KEY = "<YOUR_GEMINI_API_KEY>"
```

```bash
# Linux / Docker
export GEMINI_API_KEY="<YOUR_GEMINI_API_KEY>"
```

---

## 3. Verifying Assistant Health

Once the application is running, check the non-sensitive status endpoint:

```http
GET /api/assistant/status
```

Expected response when properly configured:

```json
{
  "ok": true,
  "enabled": true,
  "ready": true,
  "statusCode": "READY",
  "welcomeMessage": "Xin chào! 🏓 Mình là Spike Assistant. Mình có thể giúp bạn tìm sân, xem giá, tìm sản phẩm và hướng dẫn sử dụng Spike OS.",
  "privacyNotice": "Tin nhắn được xử lý qua Google Gemini AI dựa trên dữ liệu xác thực của Spike OS. Không nhập mật khẩu hoặc thông tin nhạy cảm.",
  "suggestedQuestions": [
    "Tối nay 19:00 còn sân trống không?",
    "Giá thuê sân 1 giờ tiêu chuẩn và giờ vàng?",
    "Pro Shop đang bán những sản phẩm gì?",
    "Hướng dẫn đặt sân và sử dụng vé QR"
  ]
}
```
