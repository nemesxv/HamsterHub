using HamsterHub.Client;
using HamsterHub.Contracts;

namespace HamsterHub.Mobile;

internal sealed record DevicePhoto(string Name, string ContentType, string? StorageKey = null, long SizeBytes = 0, byte[]? Bytes = null);
internal sealed record DeviceReport(Guid Id, int TaskId, string TaskName, string Frequency,
    DateTimeOffset CreatedAt, IReadOnlyList<DevicePhoto> Photos, string? Error = null);
internal sealed record DashboardCache(SessionDto Session, MemberDto Member, DashboardDto Dashboard,
    HouseholdHubDto Household, DateTimeOffset SavedAt);
internal sealed record InputDraft(Dictionary<string, string> Values);

public sealed partial class MainPage
{
    private List<DeviceReport> pendingSubmissions = [];
    private readonly SemaphoreSlim reportGate = new(1, 1);
    private string? loadedReportScope;
    private string? currentDraftKey;
    private Func<Task>? flushDraft;
    private bool offlineDashboard;
    private string AccountCacheKey => $"cache|{server?.AbsoluteUri}|{Preferences.Default.Get("active-saved-account", "")}";
    private string ReportScope => $"reports|{server?.AbsoluteUri}|{member?.Id}";

    private async Task LoadReportsAsync()
    {
        var scope = ReportScope;
        if (loadedReportScope == scope) return;
        var stored = await PrivateDeviceFiles.ReadAsync<List<DeviceReport>>(scope) ?? [];
        if (scope != ReportScope) return;
        var migrated = false;
        for (var i = 0; i < stored.Count; i++)
        {
            var report = stored[i]; var photos = new List<DevicePhoto>();
            foreach (var photo in report.Photos)
            {
                if (photo.StorageKey is null && photo.Bytes is { } bytes)
                {
                    var key = $"report-photo|{scope}|{report.Id}|{photos.Count}";
                    await PrivateDeviceFiles.SaveBytesAsync(key, bytes);
                    photos.Add(photo with { StorageKey = key, SizeBytes = bytes.Length, Bytes = null }); migrated = true;
                }
                else photos.Add(photo);
            }
            stored[i] = report with { Photos = photos };
        }
        if (migrated) await PrivateDeviceFiles.SaveAsync(scope, stored);
        if (scope != ReportScope) return;
        pendingSubmissions = stored; loadedReportScope = scope;
    }

    private async Task CacheDashboardAsync()
    {
        if (member?.Role != "Child" || session is null || dashboard is null || household is null ||
            string.IsNullOrEmpty(Preferences.Default.Get("active-saved-account", ""))) return;
        await PrivateDeviceFiles.SaveAsync(AccountCacheKey, new DashboardCache(session, member, dashboard, household, DateTimeOffset.UtcNow));
    }

    private async Task<bool> RestoreCachedChildAsync()
    {
        var activeId = Preferences.Default.Get("active-saved-account", "");
        if (!SavedAccounts.Items.Any(item => item.Id == activeId && item.Server == server!.AbsoluteUri && item.Tokens is not null && item.Role == "Child")) return false;
        var cached = await PrivateDeviceFiles.ReadAsync<DashboardCache>(AccountCacheKey);
        if (cached is null || cached.Member.Role != "Child" || cached.Session.Memberships.Any(item => item.Role == "Parent") ||
            DateTimeOffset.UtcNow - cached.SavedAt > TimeSpan.FromDays(7)) return false;
        session = cached.Session; member = cached.Member; dashboard = cached.Dashboard; household = cached.Household;
        lastUpdatedAt = cached.SavedAt; offlineDashboard = true;
        await LoadReportsAsync(); ShowDashboard(); return true;
    }

    private async Task<bool> QueueCompletionAsync(TaskDto task)
    {
        await LoadReportsAsync();
        if (pendingSubmissions.Any(item => item.TaskId == task.Id)) return true;
        if (pendingSubmissions.Count >= 20) throw new MobileApiException("OutboxFull");
        var reportId = Guid.NewGuid();
        var photos = new List<DevicePhoto>();
        var storedPhotoKeys = new List<string>();
        var committed = false;
        try
        {
        foreach (var file in selectedPhotos)
        {
            using var stream = await file.OpenReadAsync();
            using var buffer = new MemoryStream();
            var chunk = new byte[81920]; int count;
            while ((count = await stream.ReadAsync(chunk)) > 0)
            {
                if (buffer.Length + count > 5 * 1024 * 1024) throw new MobileApiException("TaskPhotoTooLarge");
                await buffer.WriteAsync(chunk.AsMemory(0, count));
            }
            var key = $"report-photo|{ReportScope}|{reportId}|{photos.Count}";
            if (pendingSubmissions.Sum(item => item.Photos.Sum(photo => photo.SizeBytes)) + photos.Sum(photo => photo.SizeBytes) + buffer.Length > 80 * 1024 * 1024)
                throw new MobileApiException("OutboxFull");
            await PrivateDeviceFiles.SaveBytesAsync(key, buffer.ToArray()); storedPhotoKeys.Add(key);
            photos.Add(new(file.FileName, MimeType(file), key, buffer.Length));
        }
        var report = new DeviceReport(reportId, task.Id, task.Name, task.Frequency, DateTimeOffset.UtcNow, photos);
        await reportGate.WaitAsync();
        try
        {
            var next = pendingSubmissions.Append(report).ToList();
            await PrivateDeviceFiles.SaveAsync(ReportScope, next);
            pendingSubmissions = next; committed = true;
        }
        finally { reportGate.Release(); }
        selectedPhotos.Clear();
        TaskNotifications.SuppressTask(task.Id, task.Frequency);
        await SendPendingReportsAsync();
        return pendingSubmissions.Any(item => item.Id == report.Id);
        }
        finally { if (!committed) foreach (var key in storedPhotoKeys) PrivateDeviceFiles.Remove(key); }
    }

    private async Task SendPendingReportsAsync()
    {
        await LoadReportsAsync();
        if (api is null || member is null) return;
        var client = api; var membership = member; var scope = ReportScope;
        await reportGate.WaitAsync();
        try
        {
            foreach (var report in pendingSubmissions.Where(item => item.Error is null).ToArray())
            {
                if (api != client || member != membership) return;
                try
                {
                    await client.CompleteAsync(membership.Id, report.TaskId, report.Photos.Select(photo =>
                        new UploadPhoto(photo.Name, photo.ContentType, async () =>
                        {
                            var bytes = photo.StorageKey is not null ? await PrivateDeviceFiles.ReadBytesAsync(photo.StorageKey) : null;
                            bytes = bytes
                                ?? throw new MobileApiException("SavedPhotoUnavailable");
                            return new MemoryStream(bytes, false);
                        })).ToList(), report.Id);
                    if (api != client || member != membership || loadedReportScope != scope) return;
                    var next = pendingSubmissions.Where(item => item.Id != report.Id).ToList();
                    await PrivateDeviceFiles.SaveAsync(scope, next);
                    if (api != client || member != membership || loadedReportScope != scope) return;
                    pendingSubmissions = next;
                    foreach (var photo in report.Photos) if (photo.StorageKey is not null) PrivateDeviceFiles.Remove(photo.StorageKey);
                }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { return; }
                catch (MobileApiException ex) when (ex.Code is "MobileServerError" or "MobileTryLater") { return; }
                catch (MobileApiException ex) when (ex.Code != "SessionExpired")
                {
                    if (api != client || member != membership || loadedReportScope != scope) return;
                    var next = pendingSubmissions.Select(item => item.Id == report.Id ? item with { Error = ex.Code } : item).ToList();
                    await PrivateDeviceFiles.SaveAsync(scope, next);
                    if (api != client || member != membership || loadedReportScope != scope) return;
                    pendingSubmissions = next;
                }
            }
        }
        finally { reportGate.Release(); }
    }

    private void AddOutboxStatus()
    {
        if (offlineDashboard) body.Add(PermissionWarning("OfflineTasksHint"));
        if (pendingSubmissions.Count == 0) return;
        var content = new VerticalStackLayout { Spacing = 10 };
        content.Add(Text("☁ " + Format("WaitingToSend", pendingSubmissions.Count), 20));
        foreach (var report in pendingSubmissions)
        {
            content.Add(Text(report.TaskName, 18));
            if (report.Error is not null) content.Add(Text(L(report.Error), 15));
            content.Add(SecondaryButton("RemoveQueuedReport", async () =>
            {
                if (!await DisplayAlertAsync(L("RemoveQueuedReport"), report.TaskName, L("Delete"), L("Cancel"))) return;
                await reportGate.WaitAsync();
                try
                {
                    var next = pendingSubmissions.Where(item => item.Id != report.Id).ToList();
                    await PrivateDeviceFiles.SaveAsync(ReportScope, next); pendingSubmissions = next;
                    foreach (var photo in report.Photos) if (photo.StorageKey is not null) PrivateDeviceFiles.Remove(photo.StorageKey);
                }
                finally { reportGate.Release(); }
                ShowDashboard();
            }));
        }
        content.Add(SecondaryButton("SendWaitingReports", async () => { await SendPendingReportsAsync(); await RefreshAsync(); }));
        body.Add(Card(content, Peach, Color.FromArgb("563C32")));
    }

    // Restore text and choices explicitly; never persist password, email, or authentication codes.
    private void AttachFormDraft(VerticalStackLayout form, string identity)
    {
        if (member is null) return;
        var key = $"draft|{server?.AbsoluteUri}|{member.Id}|{identity}";
        currentDraftKey = key;
        var inputs = formInputs.Where(item => item.Key.IsPassword == false &&
            item.Value.Key is not ("Password" or "NewPassword" or "TemporaryPassword" or "Email" or "MobileTwoFactor"))
            .ToDictionary(item => item.Value.Key, item => item.Key);
        var pickers = formPickers.Keys.ToList();
        static string ValueOf(object? value) => value switch
        {
            Choice c => c.Value, PetItemDto p => p.Id.ToString(), HouseholdMemberItemDto m => m.Id.ToString(),
            CategoryItemDto c => c.Id.ToString(), _ => ""
        };
        IEnumerable<IVisualTreeElement> Descendants(IVisualTreeElement root)
        {
            foreach (var child in root.GetVisualChildren())
            { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
        }
        var extra = Descendants(form).Where(item => item is Editor or Switch or DatePicker or TimePicker or CheckBox ||
            item is Entry { AutomationId: "task-visual-key" }).ToList();
        string ExtraValue(IVisualTreeElement item) => item switch
        {
            Editor e => e.Text ?? "", Entry e => e.Text ?? "", Switch s => s.IsToggled.ToString(),
            CheckBox c => c.IsChecked.ToString(), DatePicker d => d.Date?.ToString("O") ?? "",
            TimePicker t => t.Time?.ToString() ?? "", _ => ""
        };
        var restoring = false;
        async Task Save()
        {
            if (restoring || currentDraftKey != key) return;
            var values = inputs.ToDictionary(item => item.Key, item => item.Value.Text ?? "");
            for (var i = 0; i < pickers.Count; i++) values["picker-" + i] = ValueOf(pickers[i].SelectedItem);
            for (var i = 0; i < extra.Count; i++) values["extra-" + i] = ExtraValue(extra[i]);
            await PrivateDeviceFiles.SaveAsync(key, new InputDraft(values));
        }
        flushDraft = Save;
        void Changed()
        {
            if (restoring) return;
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(350), async () =>
            { try { await Save(); } catch { message.Text = L("DraftSaveFailed"); message.IsVisible = true; } });
        }
        foreach (var entry in inputs.Values) entry.TextChanged += (_, _) => Changed();
        foreach (var picker in pickers) picker.SelectedIndexChanged += (_, _) => Changed();
        foreach (var control in extra)
        {
            if (control is Editor editor) editor.TextChanged += (_, _) => Changed();
            if (control is Entry entry) entry.TextChanged += (_, _) => Changed();
            if (control is Switch toggle) toggle.Toggled += (_, _) => Changed();
            if (control is CheckBox box) box.CheckedChanged += (_, _) => Changed();
            if (control is DatePicker date) date.DateSelected += (_, _) => Changed();
            if (control is TimePicker time) time.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(TimePicker.Time)) Changed(); };
        }
        var actions = new VerticalStackLayout { Spacing = 8, IsVisible = false };
        actions.Add(SecondaryButton("RestoreDraft", async () =>
        {
            var draft = await PrivateDeviceFiles.ReadAsync<InputDraft>(key);
            if (draft is null) { message.Text = L("NoSavedDraft"); return; }
            restoring = true;
            try
            {
                foreach (var (name, entry) in inputs)
                    if (draft.Values.TryGetValue(name, out var text)) entry.Text = text;
                for (var i = 0; i < pickers.Count; i++)
                    if (draft.Values.TryGetValue("picker-" + i, out var value))
                        pickers[i].SelectedItem = pickers[i].ItemsSource.Cast<object>().FirstOrDefault(item => ValueOf(item) == value);
                for (var i = 0; i < extra.Count; i++)
                {
                    if (!draft.Values.TryGetValue("extra-" + i, out var value)) continue;
                    switch (extra[i])
                    {
                        case Editor e: e.Text = value; break;
                        case Entry e: e.Text = value; break;
                        case Switch s when bool.TryParse(value, out var enabled): s.IsToggled = enabled; break;
                        case CheckBox c when bool.TryParse(value, out var selected): c.IsChecked = selected; break;
                        case DatePicker d when DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var date): d.Date = date; break;
                        case TimePicker t when TimeSpan.TryParse(value, out var time): t.Time = time; break;
                    }
                }
            }
            finally { restoring = false; }
        }));
        actions.Add(SecondaryButton("DiscardDraft", () =>
        { PrivateDeviceFiles.Remove(key); actions.IsVisible = false; currentDraftKey = null; flushDraft = null; return Task.CompletedTask; }));
        form.Add(actions);
        Dispatcher.Dispatch(async () =>
        {
            try { if (await PrivateDeviceFiles.ReadAsync<InputDraft>(key) is not null && currentDraftKey == key) actions.IsVisible = true; }
            catch { }
        });
    }

}
