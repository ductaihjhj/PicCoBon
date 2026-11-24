# SPIKE OS — GEMINI AI MASCOT ASSISTANT ARCHITECTURE

## 1. High-Level System Architecture

The **Spike AI Mascot Assistant** transforms the floating 6-pose animated mascot (`#spikeFloatingMascot`) into a live, production-oriented AI concierge backed by **Google Gemini** (`gemini-3.5-flash-lite` primary with automatic fallback to `gemini-3.1-flash-lite`) and authorized, read-only server-side tools querying the live Spike OS database and business services.

```mermaid
flowchart LR
    subgraph Browser["Browser Client (Public Razor Layout)"]
        Mascot["#spikeFloatingMascot (6-Pose Dual-Layer Engine)"]
        Panel["#spikeAssistantPanel (Soft Green Chat UI)"]
        Mascot <-->|"State Sync: idle / wave / thinking / chat / idea / happy"| Panel
    end

    subgraph AspNetCore["ASP.NET Core Backend (Spike OS)"]
        Ctrl["AssistantController (/api/assistant/*)"]
        RL["RateLimiter (AssistantLimiter) + Anti-Forgery CSRF"]
        Svc["GeminiAssistantService (IAiAssistantService)"]
        Cache["IMemoryCache (Session-Partitioned Multi-Turn Memory)"]
        Tools["SpikeAssistantToolService (6 Allowlisted Read-Only Tools)"]
        Client["GeminiClient (IGeminiClient)"]
    end

    subgraph Domain["Spike OS Core Services & SQL Server"]
        ClubSvc["ClubSettingsService"]
        PriceSvc["PricingService.CalculateQuote"]
        BookSvc["BookingService + QrTokenService"]
        DB[("PickleballContext (SQL Server)")]
    end

    subgraph Google["Google Gemini REST API"]
        Gemini["POST /v1beta/models/{model}:generateContent"]
    end

    Panel -->|"POST /api/assistant/chat (JSON + RequestVerificationToken)"| RL
    RL --> Ctrl
    Ctrl --> Svc
    Svc <--> Cache
    Svc --> Client
    Client -->|"x-goog-api-key (Server-Side Only)"| Gemini
    Gemini -->|"functionCall (Tool Request)"| Client
    Svc -->|"ExecuteToolAsync (Strict Allowlist)"| Tools
    Tools --> ClubSvc
    Tools --> PriceSvc
    Tools --> BookSvc
    Tools --> DB
    Svc -->|"functionResponse -> Final Grounded Reply + Pose"| Ctrl
    Ctrl --> Panel
```

---

## 2. Core Components & Responsibilities

| Layer | File | Responsibility |
| :--- | :--- | :--- |
| **Configuration** | `Configuration/GeminiOptions.cs` | Strongly-typed options bound to `"Gemini"` config section and `GEMINI_API_KEY` environment fallback. Validates HTTPS base URL, model identifier, timeouts, token caps, and key presence without logging secrets. |
| **API Controller** | `Controllers/AssistantController.cs` | Exposes `GET /api/assistant/status`, `POST /api/assistant/chat`, and `POST /api/assistant/reset`. Enforces `[ValidateAntiForgeryToken]`, `[EnableRateLimiting("AssistantLimiter")]`, and isolates conversation memory by authenticated `AccountId` or HttpOnly cookie `spike_ai_sid`. |
| **Orchestration & Memory** | `Services/GeminiAssistantService.cs` | Manages multi-turn conversation history (`IMemoryCache`), system instruction assembly with Vietnam time (`UTC+7`), multi-round tool-calling loop (up to 3 rounds), concurrency gating (`SemaphoreSlim`), duplicate in-flight request deduplication, verified route extraction, and semantic mascot pose selection. |
| **Gemini HTTP Client** | `Services/GeminiAssistantService.cs` (`GeminiClient`) | Sends `generateContent` requests to `https://generativelanguage.googleapis.com/v1beta` using `x-goog-api-key` request headers, parses `functionCall` and `text` candidate parts, enforces request timeouts, and performs single-hop automatic failover from `gemini-3.5-flash-lite` to `gemini-3.1-flash-lite` on transient upstream errors (`404`/`408`/`429`/`5xx`). |
| **Read-Only Tool Executor** | `Services/SpikeAssistantToolService.cs` | Executes strictly allowlisted read-only tools (`get_club_info`, `check_court_availability`, `get_pricing_quote`, `search_proshop_products`, `get_website_guide`, `get_my_bookings`) against `PickleballContext`, `ClubSettingsService`, `PricingService`, `BookingService`, and `QrTokenService`. |
| **Frontend UI & Pose Engine** | `wwwroot/js/mascot-assistant.js` & `wwwroot/css/components/mascot-assistant.css` | Renders the floating `#spikeAssistantPanel` above `#spikeFloatingMascot`, handles CSRF token retrieval, conversation rendering, suggested question chips, verified internal route links, retry controls, and dual-layer PNG crossfade pose transitions (`01_idle.png`–`06_jump.png`). |

---

## 3. Multi-Turn Tool-Calling Sequence

```mermaid
sequenceDiagram
    participant U as User (Browser)
    participant UI as mascot-assistant.js
    participant C as AssistantController
    participant S as GeminiAssistantService
    participant G as Google Gemini API
    participant T as SpikeAssistantToolService
    participant DB as SQL Server / Services

    U->>UI: Clicks mascot or submits question
    UI->>UI: SetMascotPose("thinking")
    UI->>C: POST /api/assistant/chat (Message + CSRF)
    C->>S: ChatAsync(request, actor, sessionPartitionKey)
    S->>S: Load prior turns from IMemoryCache
    S->>G: generateContent (SystemInstruction + History + Tools)
    G-->>S: Candidate with functionCall(s) (e.g., check_court_availability)
    S->>T: ExecuteToolAsync(toolName, args, actor)
    T->>DB: Read-only EF Core / PricingService query
    DB-->>T: Grounded structured result
    T-->>S: Sanitized JSON tool response
    S->>G: generateContent (Append functionCall + functionResponse)
    G-->>S: Final grounded Vietnamese/English answer
    S->>S: Save turn to IMemoryCache + Resolve Pose ("happy"/"idea"/"chat")
    S-->>C: AssistantChatResponse
    C-->>UI: HTTP 200 JSON (reply, pose, links, suggestedQuestions)
    UI->>UI: Render message + SetMascotPose(response.pose) -> return to "idle"
```

---

## 4. Mascot State & Pose Synchronization

The assistant preserves the 6-pose PNG crossfade architecture (`180–240ms` opacity/scale/translate crossfade across two stacked `<img>` layers) and maps conversational events to visual states:

- **`idle` (`01_idle.png`)**: Default resting state when the panel is closed or after a response finishes displaying.
- **`wave` (`02_wave.png`)**: Triggered when opening the chat panel, greeting the user, or resetting the conversation.
- **`thinking` (`03_thinking.png`)**: Active while waiting for `POST /api/assistant/chat` or when displaying a recoverable warning/error state.
- **`chat` (`04_chat.png`)**: Standard conversational reply pose.
- **`idea` (`05_point.png`)**: Triggered when explaining pricing breakdowns (`get_pricing_quote`) or website usage guides (`get_website_guide`).
- **`happy` (`06_jump.png`)**: Triggered when court availability checks (`check_court_availability`) confirm open slots for the customer.
