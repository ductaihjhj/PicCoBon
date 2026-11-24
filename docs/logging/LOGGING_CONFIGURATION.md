# SPIKE OS — Logging Configuration Guide

## 1. Environment Configuration Overview

| Environment / Profile | Config File(s) | Console Formatter | EF `Database.Command` | `HttpClient` | Notes |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Base Defaults** | `appsettings.json` | Default (inherited) | `Warning` | `Warning` | Suppresses noisy routine SQL `SELECT`s and HTTP lifecycle noise globally. |
| **Development** | `appsettings.Development.json` | `spike-clean` | `Warning` | `Warning` | Single-line colorized console with timestamps, short categories, and `[cid:...]`. |
| **SQL Debug (Opt-in)** | `appsettings.SqlDebug.json` | `spike-clean` | `Information` | `Warning` | Shows executed SQL queries (`Executed DbCommand`) on demand. Blocked in `Production`. |
| **Production** | `appsettings.Production.json` | `json` | `Warning` | `Warning` | Structured single-line UTC JSON with `IncludeScopes: true` for log collectors. |

---

## 2. Switching Profiles & Flags

### Default Development Mode (Clean Console, No SQL Spam)
```powershell
dotnet run --project e:\pickleball\PickleballClub.csproj
```
`Database.AutoMigrateInDevelopment` remains `false` safely.

### Opt-In SQL Query Debugging (Non-Production Only)
Use **any** of these opt-in triggers (ignored automatically when `ASPNETCORE_ENVIRONMENT=Production`):

1. **CLI flag:**
   ```powershell
   dotnet run --project e:\pickleball\PickleballClub.csproj -- --sql-debug
   ```
2. **Environment variable:**
   ```powershell
   $env:SPIKE_SQL_DEBUG = "true"
   dotnet run --project e:\pickleball\PickleballClub.csproj
   ```
3. **Named environment profile:**
   ```powershell
   $env:ASPNETCORE_ENVIRONMENT = "SqlDebug"
   dotnet run --project e:\pickleball\PickleballClub.csproj
   ```

### Disabling ANSI Colors (Plain Console / CI Pipes)
`CleanSpikeConsoleFormatter` honors `NO_COLOR`, redirected stdout (`Console.IsOutputRedirected`), and explicit `ColorBehavior`:
```powershell
$env:NO_COLOR = "1"
```
Or override in configuration:
`Logging:Console:FormatterOptions:ColorBehavior = "Disabled"`

### Production Profile (Structured JSON)
```powershell
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:Security__QrSigningKey = "<minimum-32-character-non-placeholder-secret>"
dotnet PickleballClub.dll
```
Notes:
- `Production` uses `json` formatter (`TimestampFormat: "yyyy-MM-ddTHH:mm:ss.fffZ"`, `UseUtcTimestamp: true`, `IncludeScopes: true`).
- EF Core `Database.Command` remains at `Warning` in Production; `--sql-debug` and `SPIKE_SQL_DEBUG` are blocked in Production.

---

## 3. Correlation Header Propagation

Clients or reverse proxies can pass `X-Correlation-Id` matching `^[A-Za-z0-9_-]{8,48}$` (e.g., `trace-paypal-01`).
- Valid headers are echoed back on the response `X-Correlation-Id` header and attached to all logs (`[cid:...]` in `spike-clean`, `CorrelationId` scope in `json`).
- Invalid or injected values (including newline/control characters or unsafe symbols) are silently rejected and replaced with a generated `spk-<12-hex>` ID.
- Background worker runs (`BookingLifecycleWorker`, `PayPalReconciliationWorker`) automatically emit a per-cycle `[cid:wrk-<12-hex>]`.
