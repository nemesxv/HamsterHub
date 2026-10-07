using HamsterHub.Client;
using HamsterHub.Contracts;

namespace HamsterHub.Mobile;

public sealed partial class MainPage
{
    private CancellationTokenSource? speech;
    private bool parentNeedsUnlock;
    private string FamilyPreference(string suffix) => $"{server?.AbsoluteUri}|{member?.Id}|{suffix}";
    private bool PictureMode => member?.PictureMode != false;
    private static string TaskWords(TaskDto task) => string.Join(". ", new[] { task.Name, task.Instructions }
        .Where(value => !string.IsNullOrWhiteSpace(value)));
    private static string TaskSteps(string? instructions, string? visualKey) => instructions ??
        (TaskTemplates.Find(visualKey) is { } template ? L(template.StepsKey) : "");

    private Button ListenButton(string words)
    {
        var button = SecondaryButton("ListenTask", () => Task.CompletedTask);
        button.Text = "🔊 " + L("ListenTask");
        // Speech does not lock every control while the device is speaking.
        button.Clicked += async (_, _) =>
        {
            speech?.Cancel(); speech?.Dispose(); speech = new();
            var speechToken = speech.Token;
            try
            {
                var locales = await TextToSpeech.Default.GetLocalesAsync();
                speechToken.ThrowIfCancellationRequested();
                var locale = locales.FirstOrDefault(item => item.Language.StartsWith(Strings.Culture, StringComparison.OrdinalIgnoreCase));
                if (locale is null) { message.Text = L("VoiceUnavailable"); message.IsVisible = true; return; }
                await TextToSpeech.Default.SpeakAsync(words, new SpeechOptions { Locale = locale, Pitch = 1, Volume = 1 }, speechToken);
            }
            catch (OperationCanceledException) { }
            catch { message.Text = L("VoiceUnavailable"); message.IsVisible = true; }
        };
        return button;
    }

    private string TaskState(TaskDto task) => pendingSubmissions.Any(item => item.TaskId == task.Id) ? "Queued" :
        task.CanComplete ? "Ready" : task.CurrentStatus == "Approved" ? "Done" : "Waiting";

    private View ChildTaskCard(TaskDto task)
    {
        var state = TaskState(task);
        var content = new VerticalStackLayout { Spacing = 10 };
        var visual = new Image { Source = PhotoSource(task.ImagePath), HeightRequest = PictureMode ? 220 : 170, Aspect = Aspect.AspectFit };
        SemanticProperties.SetDescription(visual, task.Name);
        content.Add(visual);
        var title = Text(task.Name, PictureMode ? 27 : 23); title.FontAttributes = FontAttributes.Bold; content.Add(title);
        if (!string.IsNullOrEmpty(task.PetName) && task.PetName != L("GeneralTask") && task.PetName != task.Name) content.Add(Text(task.PetName, 16));
        content.Add(Text((state switch { "Ready" => "▶ ", "Done" => "✓ ", "Queued" => "☁ ", _ => "⌛ " }) + L("TaskState" + state), 18));
        content.Add(ListenButton(TaskWords(task with { Instructions = TaskSteps(task.Instructions, task.VisualKey) })));
        if (state == "Ready")
        {
            var open = Button("OpenMyTask", () => { ShowCompletion(task); return Task.CompletedTask; });
            open.Text = "▶ " + L("OpenMyTask"); content.Add(open);
        }
        if (!PictureMode && task.Points > 0) content.Add(Text(Format("MobilePotentialPoints", task.Points), 14));
        return Card(content, state == "Ready" ? Mint : Paper, Color.FromArgb("25443E"), 28, 1);
    }

    private View RewardGoal()
    {
        var selected = Preferences.Default.Get(FamilyPreference("reward-goal"), 0);
        var reward = household?.Rewards.FirstOrDefault(item => item.Id == selected);
        var box = new VerticalStackLayout { Spacing = 10 };
        if (reward is null)
        {
            box.Add(Text("🎁 " + L("ChooseGoalHint"), 18));
            box.Add(SecondaryButton("ChooseGoal", () =>
            { dashboardSection = DashboardSection.Rewards; ShowDashboard(); return Task.CompletedTask; }));
        }
        else
        {
            box.Add(Text("🎁 " + L("MyGoal") + ": " + reward.Name, 22));
            if (reward.ImagePath is not null) box.Add(new Image { Source = PhotoSource(reward.ImagePath), HeightRequest = 100 });
            var progress = Math.Clamp((double)dashboard!.Balance / reward.PointCost, 0, 1);
            box.Add(new ProgressBar { Progress = progress, ProgressColor = MintDeep, HeightRequest = 18 });
            var stars = Text(string.Concat(Enumerable.Range(1, 5).Select(i => progress >= i / 5d ? "★ " : "☆ ")), 30);
            stars.HorizontalTextAlignment = TextAlignment.Center; box.Add(stars);
            box.Add(Text(Format("GoalProgress", dashboard.Balance, reward.PointCost), 17));
            box.Add(SecondaryButton("Rewards", () =>
            { dashboardSection = DashboardSection.Rewards; ShowDashboard(); return Task.CompletedTask; }));
        }
        return Card(box, Peach, Color.FromArgb("563C32"), 24);
    }

    private void AddChildSteps(VerticalStackLayout container, TaskDto task)
    {
        var steps = TaskSteps(task.Instructions, task.VisualKey).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var index = 1;
        foreach (var step in steps)
        {
            var row = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
            row.Add(Text((index++).ToString() + " ●", 24)); row.Add(Text(step, PictureMode ? 21 : 18), 1);
            container.Add(row);
        }
    }

    private void ShowCompletionThanks(bool queued)
    {
        ShowForm("CompletionThanks", form =>
        {
            var art = Text(queued ? "☁" : "🌟", 80); art.HorizontalTextAlignment = TextAlignment.Center; form.Add(art);
            form.Add(Text(L(queued ? "CompletionQueued" : member!.Role == "Parent" ? "ParentTaskCompleted" : "CompletionWaiting"), 23));
            form.Add(ListenButton(L(queued ? "CompletionQueued" : "CompletionWaiting")));
            if (Preferences.Default.Get("gentle-celebrations", true))
                Dispatcher.Dispatch(async () => { await art.ScaleToAsync(1.12, 200); await art.ScaleToAsync(1, 200); });
        });
    }

    private void AddParentHelpRequests()
    {
        foreach (var task in household!.Tasks.Where(item => item.HelpRequested))
        {
            var content = new VerticalStackLayout { Spacing = 10 };
            content.Add(Text("🙋 " + Format("ChildNeedsHelp", task.AssignedMemberName), 22));
            content.Add(Text(task.Name));
            content.Add(SecondaryButton("HelpResolved", async () =>
            { await api!.ResolveHelpAsync(member!.Id, task.Id); await RefreshAsync(); }));
            body.Add(Card(content, Peach, Color.FromArgb("563C32")));
        }
    }

    private View FamilySetupGuide()
    {
        var children = household!.Members.Where(item => item.Role == "Child").ToList();
        var hasTasks = household.Tasks.Any(item => children.Any(child => child.Id == item.AssignedMemberId));
        var box = new VerticalStackLayout { Spacing = 12 };
        box.Add(Text("🌱 " + L("FamilyGuide"), 23));
        box.Add(Text(L("FamilyGuideHint"), 15));
        box.Add(SecondaryButton("SetupChild", () => { ShowAddMember(); return Task.CompletedTask; }));
        box.Add(Text((children.Count > 0 ? "✓ " : "○ ") + L("SetupChildStatus"), 15));
        var tasks = SecondaryButton("SetupTask", () => { ShowAddTask(); return Task.CompletedTask; });
        tasks.IsEnabled = children.Count > 0; box.Add(tasks);
        box.Add(Text((hasTasks ? "✓ " : "○ ") + L("SetupTaskStatus"), 15));
        if (hasTasks)
        {
            box.Add(Text("🔔 " + L("SetupReminderHint"), 15));
            box.Add(SecondaryButton("PreviewChild", () => { ShowChildPreview(); return Task.CompletedTask; }));
        }
        return Card(box, Mint, Color.FromArgb("25443E"));
    }

    private void ShowChildPreview() => ShowForm("PreviewChild", form =>
    {
        form.Add(Text(L("PreviewChildHint"), 15));
        var children = household!.Members.Where(item => item.Role == "Child").ToList();
        var select = PickerFor(L("ChooseChild"), children, nameof(HouseholdMemberItemDto.DisplayName));
        AddPicker(form, select);
        var cards = new VerticalStackLayout { Spacing = 14 }; form.Add(cards);
        void Render()
        {
            cards.Clear();
            if (select.SelectedItem is not HouseholdMemberItemDto child) return;
            foreach (var task in household.Tasks.Where(item => item.AssignedMemberId == child.Id))
            {
                var box = new VerticalStackLayout { Spacing = 12 };
                box.Add(new Image { Source = PhotoSource(task.ImagePath), HeightRequest = child.PictureMode ? 220 : 170 });
                box.Add(Text(task.Name, child.PictureMode ? 27 : 23));
                box.Add(ListenButton(task.Name + ". " + TaskSteps(task.Instructions, task.VisualKey)));
                var previewTask = new TaskDto(task.Id, task.PetName, task.Name, task.ImagePath, task.Points, task.Frequency, true,
                    VisualKey: task.VisualKey, Instructions: task.Instructions);
                AddChildSteps(box, previewTask); cards.Add(Card(box, Mint, Color.FromArgb("25443E")));
            }
            if (cards.Count == 0) cards.Add(Text(L("MobileNoTasks")));
        }
        select.SelectedIndexChanged += (_, _) => Render(); Render();
    });

    private (Func<string?> VisualKey, Editor Instructions) AddTaskVisuals(VerticalStackLayout form, Entry name,
        Picker frequency, Entry points, string? initialKey = null, string? initialInstructions = null, bool templates = false)
    {
        var visualChoice = new Entry { Text = initialKey, IsVisible = false, AutomationId = "task-visual-key" };
        form.Add(visualChoice);
        form.Add(Text(L(templates ? "ChooseTaskTemplate" : "ChooseTaskPicture"), 20));
        var choices = new Grid { ColumnSpacing = 10, RowSpacing = 10,
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) } };
        var instructions = new Editor { Text = initialInstructions, MaxLength = 600, AutoSize = EditorAutoSizeOption.TextChanges,
            MinimumHeightRequest = 110, Placeholder = L("TaskStepsHint"), AutomationId = "task-steps" };
        instructions.SetAppThemeColor(Editor.TextColorProperty, Ink, Color.FromArgb("E8F5F1"));
        var selected = Text("", 15);
        void Describe() => selected.Text = L("SelectedPicture") + ": " +
            (TaskTemplates.Find(visualChoice.Text) is { } item ? L(item.TitleKey) : L("DefaultPicture"));
        for (var index = 0; index < TaskTemplates.All.Count; index++)
        {
            if (index % 2 == 0) choices.RowDefinitions.Add(new(GridLength.Auto));
            var item = TaskTemplates.All[index];
            var content = new VerticalStackLayout { Spacing = 5 };
            content.Add(new Image { Source = item.Image.Replace(".svg", ".png"), HeightRequest = 85 });
            content.Add(Text(L(item.TitleKey), 15));
            choices.Add(TappableCard(content, L(item.TitleKey), () =>
            {
                visualChoice.Text = item.Id;
                if (templates)
                {
                    name.Text = L(item.TitleKey); instructions.Text = L(item.StepsKey);
                    frequency.SelectedItem = frequency.ItemsSource.Cast<Choice>().First(value => value.Value == item.Frequency);
                    points.Text = item.Points.ToString();
                }
                Describe(); choices.IsVisible = false; return Task.CompletedTask;
            }, Mint, Color.FromArgb("25443E")), index % 2, index / 2);
        }
        choices.IsVisible = templates;
        form.Add(choices); Describe(); form.Add(selected);
        form.Add(SecondaryButton("ChooseTaskPicture", () => { choices.IsVisible = !choices.IsVisible; return Task.CompletedTask; }));
        form.Add(SecondaryButton("DefaultPicture", () => { visualChoice.Text = null; Describe(); return Task.CompletedTask; }));
        form.Add(Text(L("TaskSteps"), 16)); form.Add(instructions);
        visualChoice.TextChanged += (_, _) => Describe();
        return (() => visualChoice.Text, instructions);
    }

    private async Task<bool> AllowParentSessionAsync(bool passwordVerified)
    {
        if (!session!.Memberships.Any(item => item.Role == "Parent") || passwordVerified) return true;
        if (await ParentAccess.ConfirmAsync()) return true;
        var saved = SavedAccounts.Items.FirstOrDefault(item => item.Id == Preferences.Default.Get("active-saved-account", ""));
        await api!.LogoutAsync(); TaskNotifications.Clear(); ShowLogin(saved);
        message.Text = L("ParentPasswordRequired"); message.IsVisible = true;
        return false;
    }
}
