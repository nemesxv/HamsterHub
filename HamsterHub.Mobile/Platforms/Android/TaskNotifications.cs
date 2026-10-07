using System.Text.Json;
using Android.App;
using Android.App.Job;
using Android.Content;
using Android.OS;
using HamsterHub.Client;
using HamsterHub.Contracts;
using Application = Android.App.Application;

namespace HamsterHub.Mobile;

internal sealed record ReminderDeviceState(string Server, int MemberId, string Revision,
    IReadOnlyList<ScheduledTaskReminderDto> Reminders);

internal static class TaskNotifications
{
    internal const string ChannelId = "family-task-reminders";
    private const int SyncJobId = 7401;
    private static readonly SemaphoreSlim SyncGate = new(1, 1);
    private static readonly object StateLock = new();
    private static Context Context => Application.Context;
    private static Android.Content.ISharedPreferences Storage =>
        Context.GetSharedPreferences("task-reminders", FileCreationMode.Private)!;

    internal static ReminderDeviceState? Read()
    {
        try { return JsonSerializer.Deserialize<ReminderDeviceState>(Storage.GetString("state", "null")!); }
        catch (JsonException) { return null; }
    }

    private static void Save(ReminderDeviceState state) =>
        Storage.Edit()!.PutString("state", JsonSerializer.Serialize(state))!.Commit();

    public static void SendTest()
    {
        EnsureChannel();
        if (!NotificationsAllowed) return;
        var launch = new Intent(Context, typeof(MainActivity)).AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        var tap = PendingIntent.GetActivity(Context, 7410, launch, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        var builder = new AndroidX.Core.App.NotificationCompat.Builder(Context, ChannelId);
        builder.SetSmallIcon(Resource.Drawable.ic_task_reminder);
        builder.SetContentTitle(Strings.Get("TestReminder"));
        builder.SetContentText(Strings.Get("TestReminderBody"));
        builder.SetVisibility(AndroidX.Core.App.NotificationCompat.VisibilityPrivate);
        builder.SetAutoCancel(true); builder.SetContentIntent(tap);
        ((NotificationManager)Context.GetSystemService(Context.NotificationService)!).Notify(7410, builder.Build());
    }

    public static void Clear()
    {
        lock (StateLock)
        {
        var state = Read();
        Storage.Edit()!.Remove("state")!.Commit();
        if (state is not null) Cancel(state);
        ((JobScheduler)Context.GetSystemService(Context.JobSchedulerService)!).Cancel(SyncJobId);
        }
    }

    private static void Cancel(ReminderDeviceState state, bool dismiss = true)
    {
        var alarms = (AlarmManager)Context.GetSystemService(Context.AlarmService)!;
        var notifications = (NotificationManager)Context.GetSystemService(Context.NotificationService)!;
        foreach (var reminder in state.Reminders)
        {
            var pending = AlarmIntent(reminder.TaskId, state.Revision, 0);
            alarms.Cancel(pending);
            pending.Cancel();
            if (dismiss) notifications.Cancel(reminder.TaskId);
        }
    }

    public static async Task ConfigureAsync(Uri server, MemberDto member, HamsterHubClient api)
    {
        if (member.Role != "Child") { Clear(); return; }
        await SyncGate.WaitAsync();
        try
        {
            var current = Read();
            if (current?.Server != server.AbsoluteUri || current.MemberId != member.Id)
            {
                Clear();
                Save(new(server.AbsoluteUri, member.Id, Guid.NewGuid().ToString("N"), []));
            }
            StartSyncJob();
            var reminders = await api.GetRemindersAsync(member.Id);
            if (!Matches(server.AbsoluteUri, member.Id)) return;
            Replace(reminders, server.AbsoluteUri, member.Id);
            if (reminders.Count > 0 && !Preferences.Default.Get("notification-permission-requested", false))
            {
                Preferences.Default.Set("notification-permission-requested", true);
                await RequestPermissionCoreAsync(false);
            }
        }
        finally { SyncGate.Release(); }
    }

    private static bool Matches(string server, int memberId) =>
        Read() is { } state && state.Server == server && state.MemberId == memberId;

    private static void Replace(IReadOnlyList<ScheduledTaskReminderDto> reminders, string server, int memberId)
    {
        lock (StateLock)
        {
        if (Read() is not { } state || state.Server != server || state.MemberId != memberId) return;
        if (state.Reminders.OrderBy(item => item.TaskId).SequenceEqual(reminders.OrderBy(item => item.TaskId))) return;
        Cancel(state, dismiss: false);
        var notifications = (NotificationManager)Context.GetSystemService(Context.NotificationService)!;
        foreach (var previous in state.Reminders)
            if (!reminders.Any(item => item.TaskId == previous.TaskId &&
                (item.SuppressUntil is null || item.SuppressUntil <= DateTimeOffset.UtcNow)))
                notifications.Cancel(previous.TaskId);
        state = state with { Revision = Guid.NewGuid().ToString("N"), Reminders = reminders };
        Save(state);
        Rearm();
        }
    }

    public static void SuppressTask(int taskId, string frequency)
    {
        lock (StateLock)
        {
            if (Read() is not { } state) return;
            var now = DateTimeOffset.UtcNow;
            var until = frequency switch
            {
                "Once" => DateTimeOffset.MaxValue,
                "Weekly" => now.AddDays(7),
                _ => new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero)
            };
            Replace(state.Reminders.Select(item => item.TaskId == taskId
                ? item with { SuppressUntil = until } : item).ToList(), state.Server, state.MemberId);
        }
    }

    internal static void Rearm()
    {
        lock (StateLock)
        {
        if (Read() is not { } state) return;
        EnsureChannel();
        foreach (var reminder in state.Reminders) Schedule(state, reminder);
        }
    }

    private static PendingIntent AlarmIntent(int taskId, string revision, long due) =>
        PendingIntent.GetBroadcast(Context, taskId,
            new Intent(Context, typeof(TaskReminderReceiver)).SetAction("com.nemesxv.hamsterhub.TASK_REMINDER")
                .PutExtra("task", taskId).PutExtra("revision", revision).PutExtra("due", due),
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;

    private static void Schedule(ReminderDeviceState state, ScheduledTaskReminderDto reminder)
    {
        var next = ReminderSchedule.Next(reminder.Frequency, reminder.Schedule,
            DateTimeOffset.UtcNow, reminder.SuppressUntil);
        if (next is null) return;
        var due = next.Value.ToUnixTimeMilliseconds();
        var alarm = (AlarmManager)Context.GetSystemService(Context.AlarmService)!;
        var pending = AlarmIntent(reminder.TaskId, state.Revision, due);
        try
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(31) || alarm.CanScheduleExactAlarms())
                alarm.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, due, pending);
            else
                alarm.SetAndAllowWhileIdle(AlarmType.RtcWakeup, due, pending);
        }
        catch (Java.Lang.SecurityException)
        {
            // Exact alarm access can be revoked between checking it and scheduling.
            alarm.SetAndAllowWhileIdle(AlarmType.RtcWakeup, due, pending);
        }
    }

    internal static void Deliver(Intent intent)
    {
        lock (StateLock)
        {
        var state = Read();
        if (state is null || state.Revision != intent.GetStringExtra("revision")) return;
        var reminder = state.Reminders.FirstOrDefault(item => item.TaskId == intent.GetIntExtra("task", 0));
        if (reminder is null) return;
        var due = DateTimeOffset.FromUnixTimeMilliseconds(intent.GetLongExtra("due", 0));
        if (reminder.SuppressUntil is { } blocked && blocked > due) { Schedule(state, reminder); return; }
        EnsureChannel();
        var manager = (NotificationManager)Context.GetSystemService(Context.NotificationService)!;
        if (!OperatingSystem.IsAndroidVersionAtLeast(33) ||
            Context.CheckSelfPermission(Android.Manifest.Permission.PostNotifications) == Android.Content.PM.Permission.Granted)
        {
            var launch = new Intent(Context, typeof(MainActivity)).AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
            var tap = PendingIntent.GetActivity(Context, reminder.TaskId, launch,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
            var builder = new AndroidX.Core.App.NotificationCompat.Builder(Context, ChannelId);
            var text = string.Format(System.Globalization.CultureInfo.GetCultureInfo(Strings.Culture),
                Strings.Get("TaskReminderBody"), reminder.TaskName);
            builder.SetSmallIcon(Resource.Drawable.ic_task_reminder);
            builder.SetContentTitle(Strings.Get("TaskReminderTitle"));
            builder.SetContentText(text);
            var style = new AndroidX.Core.App.NotificationCompat.BigTextStyle();
            style.BigText(text);
            builder.SetStyle(style);
            builder.SetVisibility(AndroidX.Core.App.NotificationCompat.VisibilityPrivate);
            builder.SetAutoCancel(true);
            builder.SetContentIntent(tap);
            manager.Notify(reminder.TaskId, builder.Build());
        }
        // Schedule the next local calendar occurrence; no foreground page timer is involved.
        Schedule(state, reminder);
        }
    }

    private static void EnsureChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;
        var manager = (NotificationManager)Context.GetSystemService(Context.NotificationService)!;
        manager.CreateNotificationChannel(new NotificationChannel(ChannelId,
            Strings.Get("TaskNotification"), NotificationImportance.Default));
    }

    public static Task RequestPermissionAsync() => RequestPermissionCoreAsync(true);

    public static bool NotificationsAllowed =>
        AndroidX.Core.App.NotificationManagerCompat.From(Context)?.AreNotificationsEnabled() == true &&
        (!OperatingSystem.IsAndroidVersionAtLeast(26) ||
         ((NotificationManager)Context.GetSystemService(Context.NotificationService)!)
             .GetNotificationChannel(ChannelId)?.Importance != NotificationImportance.None);

    public static bool ExactAlarmsAllowed => !OperatingSystem.IsAndroidVersionAtLeast(31) ||
        ((AlarmManager)Context.GetSystemService(Context.AlarmService)!).CanScheduleExactAlarms();

    private static async Task RequestPermissionCoreAsync(bool openSettingsOnDenied)
    {
        EnsureChannel();
        if (OperatingSystem.IsAndroidVersionAtLeast(33) &&
            await Permissions.RequestAsync<NotificationPermission>() != PermissionStatus.Granted && openSettingsOnDenied)
            AppInfo.Current.ShowSettingsUI();
        else if (openSettingsOnDenied && !NotificationsAllowed)
            AppInfo.Current.ShowSettingsUI();
        Rearm();
    }

    public static Task OpenExactAlarmSettingsAsync()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
            Context.StartActivity(new Intent(Android.Provider.Settings.ActionRequestScheduleExactAlarm,
                Android.Net.Uri.Parse("package:" + Context.PackageName)).AddFlags(ActivityFlags.NewTask));
        return Task.CompletedTask;
    }

    private static void StartSyncJob()
    {
        var scheduler = (JobScheduler)Context.GetSystemService(Context.JobSchedulerService)!;
        if (scheduler.GetPendingJob(SyncJobId) is not null) return;
        var job = new JobInfo.Builder(SyncJobId, new ComponentName(Context, Java.Lang.Class.FromType(typeof(TaskReminderSyncJob))));
        job.SetRequiredNetworkType(NetworkType.Any);
        job.SetPeriodic(15 * 60 * 1000);
        job.SetPersisted(true);
        scheduler.Schedule(job.Build()!);
    }

    internal static async Task SyncAsync(CancellationToken cancellationToken)
    {
        await SyncGate.WaitAsync(cancellationToken);
        try
        {
            if (Read() is not { } state) return;
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
                { BaseAddress = new Uri(state.Server), Timeout = TimeSpan.FromSeconds(45) };
            var api = new HamsterHubClient(http, new SecureSessionStore(http.BaseAddress)) { Culture = Strings.Culture };
            if (!await api.RestoreAsync()) { if (Matches(state.Server, state.MemberId)) Clear(); return; }
            var session = await api.GetSessionAsync();
            if (!session.Memberships.Any(member => member.Id == state.MemberId && member.Role == "Child"))
            { if (Matches(state.Server, state.MemberId)) Clear(); return; }
            var reminders = await api.GetRemindersAsync(state.MemberId);
            if (!cancellationToken.IsCancellationRequested) Replace(reminders, state.Server, state.MemberId);
        }
        catch (MobileApiException exception) when (exception.Code is "SessionExpired" or "MobileAccessDenied") { Clear(); }
        finally { SyncGate.Release(); }
    }
}

internal sealed class NotificationPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        [("android.permission.POST_NOTIFICATIONS", true)];
}

[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class TaskReminderReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    { if (intent is not null) TaskNotifications.Deliver(intent); }
}

[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter([Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced, Intent.ActionTimeChanged,
    "android.app.action.SCHEDULE_EXACT_ALARM_PERMISSION_STATE_CHANGED"])]
public sealed class TaskReminderRestoreReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent) => TaskNotifications.Rearm();
}

[Service(Permission = "android.permission.BIND_JOB_SERVICE", Exported = true)]
public sealed class TaskReminderSyncJob : JobService
{
    private CancellationTokenSource? cancellation;
    public override bool OnStartJob(JobParameters? parameters)
    {
        cancellation = new();
        var token = cancellation.Token;
        _ = Task.Run(async () =>
        {
            try { await TaskNotifications.SyncAsync(token); }
            catch (Exception exception) when (exception is HttpRequestException or MobileApiException or System.OperationCanceledException)
            { /* Keep the last known local schedule; Android will retry the periodic sync. */ }
            finally { if (!token.IsCancellationRequested) JobFinished(parameters, false); }
        });
        return true;
    }
    public override bool OnStopJob(JobParameters? parameters) { cancellation?.Cancel(); return true; }
}
