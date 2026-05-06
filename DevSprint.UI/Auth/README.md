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
├── README.md                  ← you are here
├── AuthTokens.cs              ← record: AccessToken, RefreshToken, ExpiresAtUtc, Scopes
├── ITokenProvider.cs          ← what services depend on (GetAccessTokenAsync)
├── BearerTokenHandler.cs      ← DelegatingHandler — injects "Authorization: Bearer <token>"
├── EncryptedTokenStore.cs     ← DPAPI-encrypted persistence at %AppData%\TeamHub\tokens.dat
└── GitHub/
    ├── GitHubAuthOptions.cs   ← bound from configuration: ClientId, Scopes, PollInterval
    ├── IGitHubAuthService.cs  ← SignInAsync / GetAccessTokenAsync / SignOutAsync / IsSignedIn
    ├── GitHubAuthService.cs   ← orchestrates: cache → refresh → device-flow
    └── GitHubDeviceFlowClient.cs   ← raw HTTP to /login/device/code & /login/oauth/access_token
```

When Jira gets its turn, a sibling `Auth/Jira/` folder will appear with the same
shape (`IJiraAuthService`, `JiraOAuthClient`, etc.) and reuse `EncryptedTokenStore`
and `BearerTokenHandler`.

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

## How requests pick up the token

`Services/GitHubService` is registered with `IHttpClientFactory` and that
client has `BearerTokenHandler` installed as a `DelegatingHandler`. On every
outgoing request the handler calls `IGitHubAuthService.GetAccessTokenAsync()`
(which silently refreshes if needed) and stamps
`Authorization: Bearer <access_token>` on the request. The service itself
never sees a token.

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
