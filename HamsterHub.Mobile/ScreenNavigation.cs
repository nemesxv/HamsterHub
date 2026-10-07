using HamsterHub.Client;

namespace HamsterHub.Mobile;

public sealed partial class MainPage
{
    private readonly Stack<Action> previousScreens = new();
    private int screenRevision;
    private bool backgroundRefreshing;
    private readonly Dictionary<string, Func<Task>> pendingPhotoChanges = [];

    private async Task ApplyPendingPhotoChangesAsync()
    {
        foreach (var (key, save) in pendingPhotoChanges.ToArray())
        {
            await save();
            pendingPhotoChanges.Remove(key);
        }
    }

    // Retain actual controls and validation state, including unsaved edits, when opening a child screen.
    private void RememberScreen()
    {
        if (body.Children.Count == 0) return;
        speech?.Cancel();
        var views = body.Children.Cast<View>().ToArray();
        var position = scroll.ScrollY;
        var inputs = formInputs.ToArray(); var pickers = formPickers.ToArray();
        var groups = formGroups.ToArray(); var photoErrors = formPhotoErrors.ToArray();
        var photos = selectedPhotos.ToArray();
        var photoChanges = pendingPhotoChanges.ToArray();
        var savedDraftKey = currentDraftKey; var savedFlush = flushDraft;
        var wasForm = showingForm; var wasSettings = showingSettings; var wasCompletion = showingCompletion;
        previousScreens.Push(() =>
        {
            screenRevision++; currentDraftKey = savedDraftKey; flushDraft = savedFlush;
            body.Clear(); foreach (var view in views) body.Add(view);
            formInputs.Clear(); foreach (var pair in inputs) formInputs.Add(pair.Key, pair.Value);
            formPickers.Clear(); foreach (var pair in pickers) formPickers.Add(pair.Key, pair.Value);
            formGroups.Clear(); formGroups.AddRange(groups);
            formPhotoErrors.Clear(); foreach (var pair in photoErrors) formPhotoErrors.Add(pair.Key, pair.Value);
            selectedPhotos.Clear(); selectedPhotos.AddRange(photos);
            pendingPhotoChanges.Clear(); foreach (var pair in photoChanges) pendingPhotoChanges.Add(pair.Key, pair.Value);
            showingForm = wasForm; showingSettings = wasSettings; showingCompletion = wasCompletion;
            dashboardNavigation.IsVisible = session is not null && member is not null && !wasForm && !wasCompletion;
            message.Text = ""; message.IsVisible = false;
            RestoreScroll(position);
        });
        dashboardNavigation.IsVisible = false;
        message.Text = ""; message.IsVisible = false;
    }

    private Task GoBackAsync()
    {
        speech?.Cancel(); currentDraftKey = null; flushDraft = null;
        if (previousScreens.TryPop(out var restore)) restore();
        else if (session is null) ShowLogin();
        else ShowDashboard();
        return Task.CompletedTask;
    }

    private Button BackButton()
    {
        var back = SecondaryButton("MobileBack", GoBackAsync);
        back.HorizontalOptions = LayoutOptions.Start;
        return back;
    }

    private void RestoreScroll(double position)
    {
        var revision = screenRevision;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(60), () =>
        {
            if (revision == screenRevision) _ = scroll.ScrollToAsync(0, position, false);
        });
    }

    private async Task RefreshInBackgroundAsync()
    {
        if (backgroundRefreshing || busy || !foreground || session is null || member is null ||
            showingForm || showingCompletion || photoViewerOpen) return;
        backgroundRefreshing = true;
        var client = api;
        var revision = screenRevision;
        try { await RefreshAsync(preserveScroll: true, background: true); }
        catch (MobileApiException exception) when (exception.Code == "SessionExpired")
        {
            if (api == client && revision == screenRevision)
                await RunAsync(() => Task.FromException(exception));
        }
        catch (Exception)
        {
            if (api == client && revision == screenRevision)
            {
                message.Text = L("MobileBackgroundRefreshFailed"); message.IsVisible = true;
            }
        }
        finally { backgroundRefreshing = false; }
    }
}
