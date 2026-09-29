# MAUI Android and the shared backend

The app and MVC website share one ASP.NET Core backend, database, and Identity
user store. `HamsterHub.Contracts` contains transport records, `HamsterHub.Client`
contains a platform-independent HTTP client, and `HamsterHub.Mobile` contains
native Android screens and secure token storage. Business rules remain server-side.

Open `HamsterHub.Mobile.slnx` for the full solution. `HamsterHub.slnx` remains
backend-only so its CI does not require Android workloads.

## Current scope

- Existing-account login and authenticator codes when 2FA is enabled.
- Household selection, assigned care tasks, and current point balance.
- Completion with zero to eight photos (camera or file selection).
- Parent approval/rejection of care reports with their photos.
- Latest 50 personal care reports and their review status.
- Parent create/edit/archive flows for family members, pets, and care tasks,
  including validated pictures.
- Parent and child reward catalogs, requests, reviews, direct purchases, and history.
- Privacy-filtered household member and pet summaries for children.
- Russian/English, persistent light/dark choice, accessible names, and a
  two-column layout at 720 logical units for tablets.

Initial parent registration, parent account security, detailed family point-history
pages, and child history-sharing controls still use the website. Accounts without a
household finish onboarding there. Offline writes and push notifications are future work.

## Build and run

Use the **x64 .NET 10 SDK**, MAUI/Android tooling, Android SDK 36, and JDK 21.
On this PC the x64 CLI is `C:\Program Files\dotnet\dotnet.exe`; the default PATH
may instead resolve the older x86 CLI.

```powershell
dotnet restore .\HamsterHub.Mobile.slnx
dotnet test .\HamsterHub.slnx --no-restore
dotnet build .\HamsterHub\HamsterHub.csproj --no-restore
dotnet build .\HamsterHub.Mobile\HamsterHub.Mobile.csproj --no-restore
```

If SDK detection needs help, append local SDK paths to the build:

```powershell
-p:AndroidSdkDirectory="C:\Program Files (x86)\Android\android-sdk" `
-p:JavaSdkDirectory="C:\Program Files\Android\openjdk\jdk-21.0.8"
```

The debug APK is
`HamsterHub.Mobile/bin/Debug/net10.0-android/com.nemesxv.hamsterhub-Signed.apk`.
Assemblies are embedded for installation without Visual Studio fast deployment.
The initial ID is `com.nemesxv.hamsterhub`; confirm it and configure release signing
before store publication. No store upload or production hosting is configured.

Run the backend under the Windows account that owns the development LocalDB:

```powershell
dotnet run --project .\HamsterHub\HamsterHub.csproj --no-launch-profile `
  --environment Development --urls http://localhost:5080
```

Development startup applies committed migrations. In the emulator, enter
`http://10.0.2.2:5080` on the login screen. For a USB phone with debugging enabled:

```powershell
adb reverse tcp:5080 tcp:5080
adb install -r .\HamsterHub.Mobile\bin\Debug\net10.0-android\com.nemesxv.hamsterhub-Signed.apk
```

Enter `http://localhost:5080` on the phone. The current self-hosted preview accepts
both HTTP and HTTPS server addresses, including a direct public IP. HTTP carries
passwords, bearer tokens, and uploaded photos without transport encryption. Server
URLs with a subpath are not supported. Emulator installation requires the Android
SDK license to be accepted separately if it has not already been accepted.

New installs default to `http://95.165.103.141:5080/`. The server field is hidden
under Advanced settings during normal login. The authenticator-code field is also
hidden until a successful password check reports that two-factor authentication is
required; the same login form then presents a dedicated code-verification step.
The default address remains active unless a different address is explicitly saved
from Advanced settings. After login, parent and child memberships open separate
dashboards modeled on their website experiences: parent approvals appear first,
while the child view emphasizes pictures, available tasks, points, and recent care.
Parents can also create, edit, and archive family members, pets, and care tasks;
attach validated pictures; create and directly award rewards; and review child
reward requests. Children can browse household members and pets, request visible
rewards, and see their reward history. These operations use the same household,
role, image, and point rules as the website.

## API

In addition to authentication, dashboard, completion, and care-review routes, the
app uses `GET /api/v1/memberships/{id}/household` plus household-scoped member,
pet, task, media, reward, reward-request, and direct-purchase routes. Parent-only
mutations reject child and foreign-household memberships.

| Method | Route | Purpose |
|---|---|---|
| POST | `/api/v1/auth/login` | Email, password, optional `twoFactorCode` |
| POST | `/api/v1/auth/refresh` | `refreshToken` |
| GET | `/api/v1/me` | User display name and active memberships |
| GET | `/api/v1/memberships/{id}/dashboard` | Tasks, balance, history, approvals |
| GET | `/api/v1/memberships/{id}/household` | Role-filtered members, pets, tasks, rewards, and reward history |
| POST/PUT/DELETE | `/api/v1/memberships/{id}/members/...` | Parent member management |
| POST/PUT/DELETE | `/api/v1/memberships/{id}/pets/...` | Parent pet management |
| POST/PUT/DELETE | `/api/v1/memberships/{id}/tasks/...` | Parent task management |
| PUT | `/api/v1/memberships/{id}/media/{kind}/{itemId}` | Parent-managed member, pet, task, or reward picture |
| POST | `/api/v1/memberships/{id}/rewards/...` | Create, request, review, or directly award a reward |
| POST | `/api/v1/memberships/{id}/tasks/{taskId}/complete` | Multipart form; repeated optional `photos` files |
| POST | `/api/v1/memberships/{id}/care-logs/{logId}/review` | JSON `{ "approve": true/false }` |
| GET | `/api/v1/memberships/{id}/media?path=...` | Authorized care/task/pet/member/reward photos |

Website cookies and antiforgery stay intact. Mobile endpoints accept only Identity
bearer tokens: protected opaque tokens, not JWTs. No public Identity registration
or account-management API is exposed. Password failures use Identity lockout;
login/refresh also have a per-IP fixed-window limiter.

Access tokens last 15 minutes and refresh tokens 14 days. API access checks
active users, lockout, security stamps, current roles, and household membership.
MAUI SecureStorage stores tokens per server origin; passwords are never saved.
Logout clears the device session. It does not revoke a separately copied refresh
token; device-specific server revocation remains future work. Security-stamp
changes, including password changes, invalidate both token types.

`CareWorkflowService` owns completion and review transactions for both clients.
It preserves point snapshots and checks recurrence inside a serializable
transaction. `UploadedImageService` checks extension, matching MIME type, signature,
size, and count, and removes newly saved files on failure. Mobile media retrieval
checks ownership before returning a file. Existing website upload serving/storage
is unchanged; production storage and website photo-access hardening remain
deployment work.

The client refreshes an expired token and retries a rejected request once. It does
not retry an uncertain network write: refresh the dashboard before resending to
avoid duplicating an as-needed task. SQLite tests verify behavior and ownership,
not SQL Server-specific locking or concurrent-transaction behavior.

## Device acceptance

Before release, test phone and tablet layouts, Russian/English, light/dark,
login/2FA, task images, photo selection/capture, completion, parent approval,
updated balances, rotation, keyboard navigation/TalkBack, session restore,
logout, and connection loss. An APK build and HTTP tests do not replace these checks.

## Verification snapshot (2026-09-30)

- 67 backend/client integration tests passed, including cookie/bearer separation,
  lockout/2FA requirements, security-stamp invalidation, cross-household denial,
  household management, protected media, task completion/approval, reward requests,
  point snapshots, refresh retries, and logout/refresh races.
- Backend and standalone Android 0.1.13 Release builds passed with zero warnings/errors;
  the signed APK was installed and launched on the emulator.
- The parent household-management flow was checked on phone and tablet emulator
  layouts in Russian/light and English/dark modes. The native add-member form and
  tablet management dashboard fit without horizontal clipping.
- File upload authorization and image signatures are covered by integration tests.
  Camera capture, keyboard/TalkBack navigation, rotation, and connection-loss
  recovery still require a full physical-device acceptance pass before store release.

## Website APK download

The shared website layout offers a localized Android preview download on phones,
tablets, and desktops whenever Downloads/HamsterHub.apk exists in the web project.
GET /download/android returns the actual APK as an attachment with the Android
package MIME type and resumable range support. No sign-in is required. The APK is
outside wwwroot and is included in web build/publish output. Without the artifact,
the banner is hidden and the endpoint returns 404.

The currently staged artifact is a Release-configuration preview signed with the
Android debug signing key, not a store-ready production release. Replace it with
an APK signed with a durable release key before store publication. Keep the same
signing key for subsequent app updates. APK binaries
are ignored by Git: copy the signed APK to HamsterHub/Downloads/HamsterHub.apk
before publishing the website. Android requires users to open the downloaded file
and approve installation from their browser. This Android APK does not run on iOS.

The Windows homelab deployment task builds the MAUI Release APK from the same Git
commit as the website. It derives Android version code and display version from the
commit count, backs up the previous APK with the SQL/uploads/key backup, deploys and
health-checks the website, then atomically replaces and verifies the live download.

### Website download verification (2026-09-28)

- All 65 backend/client tests passed; website build and publish succeeded.
- The live Docker site serves the 29,574,042-byte Android 0.1.1 APK over HTTP.
  Its downloaded SHA-256 matches the staged artifact:
  4905AB3936042FAE56EFE23B4CC7480027935E4E73DF013C286C4E112649567C.
- Attachment filename/MIME type, HEAD, and 206 byte-range responses verified.
- Published output includes the identical APK; absent artifact returns 404 and
  hides the banner.
- Browser checks passed at 320px phone and 800px tablet widths, Russian/English,
  light/dark themes, and keyboard focus/Enter-triggered download.
- The Release APK was installed on the emulator and reached the live Docker API
  at `http://192.168.1.69:5080`; a disposable invalid login returned the expected
  authentication response, proving cleartext HTTP works end to end.

## General-task update (0.1.14)

Task creation/editing now sends `Name`, nullable `PetId`, and nullable `CategoryId`.
The existing pet list and household-scoped custom categories remain available.
Update the Android client before managing general tasks: older clients assumed
non-null pet/category IDs. Settings contains language/theme/logout. Photo buttons
open a camera/gallery source sheet; gallery supports selecting several report
photos in one operation (eight total). New single-image selections show previews.

Verification for this update excludes unit tests and emulator runs at the user's
request. Prior version's test/device results above do not verify this increment.

The migration applied successfully to local SQL Server. A disposable local HTTP
check verified a task without a pet/category, creation and removal of its category,
a two-photo report, authorized image retrieval, and the resulting points balance.
The website and Android 0.1.14 Release builds completed with zero warnings/errors;
JavaScript syntax and parity of all 480 Russian/English resource keys were checked.
