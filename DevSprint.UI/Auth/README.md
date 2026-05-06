# Auth

This folder contains everything related to **authenticating against external
platforms** (GitHub today, Atlassian/Jira later). It deliberately sits apart
from `Services/` so that:

- `Services/*.cs` stay pure API clients — they request an `HttpClient` and
  trust the pipeline to add the right `Authorization` header on every call.
- All OAuth/Device-Flow plumbing, token persistence, and refresh logic lives
  in one place that is easy to find, read, and replace.

## Layout

```
Auth/
├── README.md                       ← you are here
├── AuthTokens.cs                   ← record: AccessToken, RefreshToken, ExpiresAtUtc, Scopes, CloudId
├── ITokenProvider.cs               ← what services depend on (GetAccessTokenAsync)
├── BearerTokenHandler.cs           ← DelegatingHandler — injects "Authorization: Bearer <token>"
│                                     plus GitHubBearerTokenHandler / JiraBearerTokenHandler subclasses
├── EncryptedTokenStore.cs          ← DPAPI-encrypted persistence at %AppData%\TeamHub\tokens.dat
├── PkceCodes.cs                    ← shared: verifier+challenge generator, state generator
├── LoopbackHttpListener.cs         ← shared: one-shot HttpListener for OAuth redirects
├── GitHub/
│   ├── GitHubAuthOptions.cs        ← bound from "GitHub:OAuth": ClientId, Scopes, PollInterval
│   ├── IGitHubAuthService.cs
│   ├── GitHubAuthService.cs        ← cache → silent refresh → device-flow dialog
│   └── GitHubDeviceFlowClient.cs   ← raw HTTP for /login/device/code, /login/oauth/access_token
└── Jira/
    ├── JiraAuthOptions.cs          ← bound from "Jira:OAuth": ClientId, ClientSecret, CallbackPort, Scopes
    ├── IJiraAuthService.cs         ← + GetCloudIdAsync (cloudid is part of the auth state)
    ├── JiraAuthService.cs          ← cache → silent refresh → loopback OAuth flow
    ├── JiraOAuthClient.cs          ← raw HTTP for /authorize URL, /oauth/token, accessible-resources
    └── JiraApiBaseUriHandler.cs    ← rewrites /rest/... → /ex/jira/{cloudId}/rest/...
```

## How GitHub auth works (Device Flow)

1. App starts. `App.OnStartup` calls `IGitHubAuthService.EnsureSignedInAsync()`
   before showing `MainWindow`.
2. The service reads `EncryptedTokenStore` for the GitHub entry.
   - If a non-expired access token is present → done, return.
   - If the access token is expired but a refresh token exists → call
     `/login/oauth/access_token` with `grant_type=refresh_token`. On success,
     persist the new pair (GitHub rotates refresh tokens). On failure, fall
     through to step 3.
   - If nothing is cached → step 3.
3. The service shows `Views/DeviceCodeDialog`:
   - Calls `POST https://github.com/login/device/code` with `client_id` + scopes.
   - Displays the `user_code` + `verification_uri` and a "Copy & open browser"
     button.
   - Polls `POST https://github.com/login/oauth/access_token` every `interval`
     seconds with `grant_type=urn:ietf:params:oauth:grant-type:device_code`
     until GitHub returns `access_token` (or the user cancels / times out).
4. The new tokens are DPAPI-encrypted and saved to disk. Subsequent app
   launches skip the dialog as long as the refresh token still works.

## How GitHub requests pick up the token

`Services/GitHubService` is registered with `IHttpClientFactory` and that
client has `BearerTokenHandler` installed as a `DelegatingHandler`. On every
outgoing request the handler calls `IGitHubAuthService.GetAccessTokenAsync()`
(which silently refreshes if needed) and stamps
`Authorization: Bearer <access_token>` on the request. The service itself
never sees a token.

## How Jira auth works (OAuth 2.0 / 3LO with PKCE + loopback redirect)

1. App starts. `App.OnStartup` calls `IJiraAuthService.EnsureSignedInAsync()`
   after the GitHub one.
2. The service reads `EncryptedTokenStore` for the Jira entry.
   - If a non-expired access token is present and `CloudId` is populated → done.
   - If expired but a refresh token exists → call `https://auth.atlassian.com/oauth/token`
     with `grant_type=refresh_token` (Atlassian rotates refresh tokens — we persist
     the new pair). Cloudid is preserved across refreshes.
   - Otherwise → step 3.
3. The service shows `Views/JiraSignInDialog`:
   - Generates a PKCE verifier + challenge and a random `state` value.
   - Starts `LoopbackHttpListener` on the configured `CallbackPort`.
   - Opens the system browser to `https://auth.atlassian.com/authorize?...&code_challenge=...&prompt=consent`.
   - Awaits the redirect to `http://127.0.0.1:<port>/callback?code=...&state=...`.
   - Validates `state` matches; exchanges `code` for tokens at
     `https://auth.atlassian.com/oauth/token` (with `client_secret` and
     `code_verifier`).
   - Calls `https://api.atlassian.com/oauth/token/accessible-resources` to look
     up the cloudid for the configured Atlassian site (`Jira:BaseUrl`).
4. The new tokens (with cloudid) are DPAPI-encrypted and saved.

## How Jira requests pick up the token AND the cloudid

`Services/JiraService` has TWO `DelegatingHandler`s installed by the factory:

- `JiraBearerTokenHandler` — same pattern as GitHub: stamps `Authorization: Bearer ...`.
- `JiraApiBaseUriHandler` — rewrites the outgoing URI from
  `https://api.atlassian.com/rest/...` to `https://api.atlassian.com/ex/jira/{cloudid}/rest/...`.

This lets `JiraService` keep using its existing relative URIs (`rest/api/3/search/jql`,
`rest/agile/1.0/board/...`) without knowing about cloudid routing.

## Token storage

`%AppData%\TeamHub\tokens.dat` — a single file holding a `Dictionary<string, AuthTokens>`
keyed by platform (`"github"`, `"jira"`). The file is encrypted with
`ProtectedData.Protect(..., DataProtectionScope.CurrentUser)` so only the
current Windows user on this machine can decrypt it. There is no key
management — it's tied to the OS account.

## OAuth App registration

The GitHub `client_id` lives in `appsettings.json` under `GitHub:OAuth:ClientId`.
That value is **not a secret** — it identifies the registered app, doesn't
authorize anything by itself. The OAuth App must have **Device Flow enabled**
in its GitHub settings.

## Security notes

- No client secrets ship in the binary.
- No long-lived PATs are persisted anywhere.
- Access tokens are short-lived (default 8h with token expiration enabled);
  refresh tokens are 6-month rotating.
- Sign-out wipes the relevant entry from `tokens.dat`. The next API call will
  trigger a fresh device-flow sign-in.
