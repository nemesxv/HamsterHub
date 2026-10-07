# HamsterHub roadmap

Use the following states: **Next**, **Later**, and **Done**. Move an item when
its implementation and proportionate verification are complete.

## Next

- Android reminder device acceptance: app background/process termination, reboot,
  Doze, permission grant/denial, offline sync, and parent edits on another device.
- Consider cloud push if immediate remote schedule updates are needed; current
  reminders use local alarms with periodic background synchronization.
- Verify mobile dashboard destinations, restored drafts/scroll, and background-refresh
  interaction on a real phone and tablet.
- Guided family setup and child-device pairing; complete password/account
  recovery and parent account-security handoff from Android.
- Reward editing/archival, a combined point ledger, and mobile history-sharing
  controls.
- Define household-local task schedules and show exact next availability.
- HTTPS endpoint, accurate privacy notice, release-signing and photo-access
  review before broader family use.
- Verify the initial MAUI Android app on phone and tablet devices/emulators,
  including camera, keyboard/TalkBack, both languages, and both themes.
- Parent-managed password reset for family accounts.

## Later

- Care streaks and history without unhealthy competitive pressure.
- Email delivery and mandatory email confirmation.
- Accessibility review with children and parents.
- Production hosting, database, secrets, monitoring, and backups.

## Done

- Optional parent-configured task reminders in website/app forms, shared schedule
  validation, daily/weekly/one-time behavior, unlimited-task exclusion, EF migration,
  child-scoped API, and native Android alarm/background-sync implementation.
- Corrected the GitHub failing historical-pet test and nullable fixture warnings.
- Shared task recurrence status on website/API, actionable child task count,
  pending/done states, readable two-line task titles, and child-dashboard APK
  banner removal.
- Parent approval queues moved ahead of management content; Android combines
  care and reward request counts.
- Android pull/resume/manual refresh, last-updated cue, honest pending/rejected
  point labels, insufficient-reward explanation, Back routing for forms/settings,
  and first-parent registration handoff.
- Android date-picker birth dates, numeric point validation, and duplicate-safe
  retries when a newly created record's photo upload fails.
- Initial MAUI Android app and `/api/v1` backend for login, care tasks, completion
  photos, parent approvals, care history, and points; device acceptance remains Next.
- Mobile household management for creating, editing, and archiving family members,
  pets, and care tasks; optional validated pictures; reward catalogs, requests,
  reviews, and direct parent purchases.
- Shared MVC/API care workflow and image validation; bearer authentication with
  active-user, security-stamp, role, and household checks.

- SQL Server LocalDB connection and Entity Framework migrations.
- Identity-based users with Parent and Child roles.
- Household, membership, pet, care-task, and care-log schema.
- Initial landing screen with functional parent registration and login.
- Russian default localization with an English option.
- Persistent light and dark themes.
- Initial responsive layout for phones, tablets, and desktops.
- Durable cross-chat project guidance and architecture documentation.
- Automatic household onboarding for a new parent.
- Parent dashboard for adding children, other parents, pets, and care tasks.
- Guided, attention-first parent dashboard with large common-action cards, a
  visual household summary, and a localized parent-editable family name.
- Child dashboard for recording tasks and viewing points and history.
- Parent approval queue for approving or rejecting care logs.
- Parent-only account management; child account controls are restricted.
- Optional pet birth dates and validated JPG, PNG, or WebP photo uploads.
- Pet-linked care tasks with reusable built-in and custom Unicode categories.
- Parent editing and safe archival for members, pets, and care tasks.
- Required family-member task assignment with session-scoped selection memory.
- Responsive, localized parent account-management area for profile, password,
  and authenticator-based two-step security.
- Parent family-member history with report and approval timestamps.
- Durable point totals captured at approval time.
- Parent-managed, private-by-default child history sharing.
- Pet-forward child task and history presentation.
- Picture-first creative child dashboard for the 3–6 age range.
- Default illustrated care categories and parent-managed task pictures.
- Optional multi-photo child task reports shown during approval and in history.
- QR-based authenticator setup with a manual-key fallback.
- Password symbols made optional while retaining length, case, and digit rules.
- Child pet gallery, live camera capture, multi-photo previews, and in-page
  photo viewing.
- Accessible dark-theme point contrast on the child dashboard.
- Optional parent-managed family-member profile photos.
- Auto-approved parent completion of tasks assigned to that parent.
- Privacy-filtered child family profiles with approved history and photo viewing.
- Automated tests for household authorization, recurrence rules, relational
  constraints, and point-history invariants.
- Parent-managed rewards with per-child visibility, optional validated images, point
  costs, child requests, approval/rejection, and direct parent purchases.
- Immutable reward-redemption debits and derived current point balances.
- Privacy-permitted family point balances on the child dashboard without
  rankings or a competitive scoreboard.
- Automatic pending-migration application for the local development database.

## General family tasks (2026-09-30)

- Website and app: independent task titles, optional pet/category, inline custom categories.
- Mobile Settings groups language, theme, and logout.
- Camera/gallery source choices, multi-photo reports, removable previews.
- Verification scope: builds and migration review; no unit tests or emulator per request.

## Self-hosted in-app updater (2026-10-01)

- Implemented: startup/resume release checks, Update card, manual Settings check,
  download progress, private verified APK cache, signing/version checks and Android
  installer handoff with one-time source-permission continuation.
- Implemented: anonymous release metadata API and deployment generation/publication
  with backup, verification and rollback of the previous APK/metadata pair.
- Acceptance pending: actual Android installation from an older version, source
  permission denial/approval, interrupted download and preserved login/settings.

- Update placement refinement: Settings-only update controls and persisted automatic
  checking option; compact website download links in both dashboard welcome areas.
  Local tests/builds skipped by request; publication uses the existing autodeploy flow.

- Dashboard refresh refinement: removed the Refresh button, added foreground data
  refresh every minute, retained forced pull-down refresh, and paused the timer in
  forms/background. Local tests/builds skipped by the continuing user preference.

- Mobile creation feedback: accept blank optional categories, explicitly label optional
  photos, show model/Identity validation causes and distinguish transport/server errors.
  Local tests/builds skipped under the continuing user instruction; autodeploy builds.

- Form/Settings refinement: required-field stars, simultaneous per-field validation,
  four-character password minimum, optional-photo inline errors, conditional permission
  warnings/actions and two-by-two parent add-action tiles. Local checks remain skipped
  by request; real-phone acceptance is still pending.

- Implemented mobile visual creation tiles, tappable management cards with deletion in
  editing, full member account/privacy fields, reward editing/archival, child member/pet
  profiles, privacy-scoped history/media, full-screen photo zoom and encrypted saved
  account/password sign-in choices. Added readable field labels, password visibility,
  top Back actions, available-task ordering and shorter completion copy. Local tests/
  builds skipped by instruction; autodeploy compilation and phone acceptance pending.

- Implemented mobile single-Back navigation, nested draft/scroll restoration, fixed
  dashboard destinations, compact summaries, incremental history rendering, wrapping
  labels, tappable selection rows and nonblocking guarded automatic refresh. Publication
  uses autodeploy; real-device interaction acceptance remains pending.


## Visual-first family release (2026-10-07)

Implemented: eight task templates and shared illustrations/instructions; opt-in spoken
instructions; parent-controlled reading presentation; explicit task states; simple
completion/help; supportive review feedback; visual reward goals; parent setup/preview;
notification test; protected saved parent access; encrypted management drafts; cached
child tasks and idempotent durable mobile report submission. Website shares task templates,
help, feedback, reading mode and goal progress. Recorded parent audio is a future optional
extension; this release uses existing device/browser speech engines.

Acceptance: deployment builds and health checks; local tests/builds skipped by request.
Still requires actual-phone verification of Android credential return/cancellation,
offline queued photos/reconnect/account switching, voice availability, and usability
observation with children of different reading levels and less technical parents.
