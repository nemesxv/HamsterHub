# HamsterHub roadmap

Use the following states: **Next**, **Later**, and **Done**. Move an item when
its implementation and proportionate verification are complete.

## Next

- Verify the initial MAUI Android app on phone and tablet devices/emulators,
  including camera, keyboard/TalkBack, both languages, and both themes.
- Parent-managed password reset for family accounts.

## Later

- Care streaks and history without unhealthy competitive pressure.
- Email delivery and mandatory email confirmation.
- Accessibility review with children and parents.
- Production hosting, database, secrets, monitoring, and backups.

## Done

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
