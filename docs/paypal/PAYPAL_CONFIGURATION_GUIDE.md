# PayPal Sandbox Configuration Guide — Spike OS

## Overview

Spike OS integrates **PayPal REST Orders API v2** strictly in **Sandbox test mode** (`https://api-m.sandbox.paypal.com`). Live production endpoints (`api-m.paypal.com`) are blocked at configuration validation time.

## Required Configuration Keys

Configuration binds cleanly to `PayPalOptions` (`Configuration/PayPalOptions.cs`):

| Key | Source | Purpose | Safe Default / Rule |
|---|---|---|---|
| `PayPal:Environment` | `appsettings.json` / User Secrets | Environment guard | Must equal `"Sandbox"` |
| `PayPal:BaseUrl` | `appsettings.json` / User Secrets | PayPal REST API host | Must equal `"https://api-m.sandbox.paypal.com"` |
| `PayPal:ClientId` | User Secrets / Env var | Sandbox REST app Client ID | Required; never commit to repo |
| `PayPal:ClientSecret` | User Secrets / Env var | Sandbox REST app Secret | Required; never commit or log |
| `PayPal:Currency` | `appsettings.json` | Settlement currency | `"USD"` |
| `PayPal:VndPerUsd` | User Secrets / Env var | Authoritative VND → USD rate | Must be `> 0` (`26000` for Sandbox test); `0` fails closed (`503`) |
| `PayPal:WebhookId` | User Secrets / Env var | Webhook signature verification ID | Required only for webhook verification (`/api/webhooks/paypal`) |
| `PayPal:BrandName` | `appsettings.json` | PayPal checkout display name | `"Spike Pickleball Club (Sandbox)"` |

## Setting Up Credentials Safely (.NET User Secrets)

Never place `ClientId` or `ClientSecret` inside `appsettings.json`, `.cshtml`, or client-side `.js` files. Configure secrets locally using `dotnet user-secrets` from `e:\pickleball`:

```powershell
dotnet user-secrets set "PayPal:Environment" "Sandbox"
dotnet user-secrets set "PayPal:BaseUrl" "https://api-m.sandbox.paypal.com"
dotnet user-secrets set "PayPal:ClientId" "<YOUR_SANDBOX_CLIENT_ID>"
dotnet user-secrets set "PayPal:ClientSecret" "<YOUR_SANDBOX_CLIENT_SECRET>"
dotnet user-secrets set "PayPal:VndPerUsd" "26000"
# Optional for webhook signature verification:
dotnet user-secrets set "PayPal:WebhookId" "<YOUR_SANDBOX_WEBHOOK_ID>"
```

To verify your secret keys without exposing secrets on shared logs:

```powershell
dotnet user-secrets list
```

For staging / environment deployments, inject via standard ASP.NET Core environment variables (`PayPal__ClientId`, `PayPal__ClientSecret`, `PayPal__VndPerUsd`, etc.).

## Database Safety Settings

Keep automated migrations disabled on boot during development and run explicit backups/migrations only on approved targets:

```json
{
  "Database": {
    "AutoMigrateInDevelopment": false
  }
}
```

Apply schema changes via an explicit EF CLI step when authorized:

```powershell
dotnet ef database update --connection "<TARGET_CONNECTION_STRING>"
```
