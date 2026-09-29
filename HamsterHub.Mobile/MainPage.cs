using HamsterHub.Client;
using HamsterHub.Contracts;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;

namespace HamsterHub.Mobile;

public sealed class MainPage : ContentPage
{
    private sealed record Choice(string Value, string Label);

    private const string DefaultServerAddress = "http://95.165.103.141:5080/";
    private const string CustomServerPreference = "custom-server";
    private const string LegacyServerPreference = "server";
    private static readonly Color Ink = Color.FromArgb("173D3A");
    private static readonly Color InkSoft = Color.FromArgb("526D69");
    private static readonly Color Cream = Color.FromArgb("FFFAF0");
    private static readonly Color Paper = Colors.White;
    private static readonly Color Mint = Color.FromArgb("DFF4EA");
    private static readonly Color MintDeep = Color.FromArgb("2F8174");
    private static readonly Color Coral = Color.FromArgb("EF755D");
    private static readonly Color Peach = Color.FromArgb("FFD6B8");
    private HamsterHubClient? api;
    private HttpClient? http;
    private Uri? server;
    private SessionDto? session;
    private MemberDto? member;
    private DashboardDto? dashboard;
    private HouseholdHubDto? household;
    private readonly VerticalStackLayout body = new() { Spacing = 16 };
    private readonly Label message = new() { FontSize = 16, IsVisible = false };
    private readonly ActivityIndicator activity = new() { IsVisible = false, HeightRequest = 24 };
    private readonly Grid columns = new() { ColumnSpacing = 24, RowSpacing = 24 };
    private readonly ScrollView scroll = new();
    private readonly List<FileResult> selectedPhotos = [];
    private bool initialized;
    private bool busy;
    private bool wide;
    private bool showingCompletion;
    private static string L(string key) => Strings.Get(key);

    public MainPage()
    {
        Title = "HamsterHub";
        this.SetAppThemeColor(BackgroundColorProperty, Cream, Color.FromArgb("102522"));
        message.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("8B3636"), Color.FromArgb("FFB7A9"));
        SemanticProperties.SetDescription(message, L("MobileStatus"));
        var root = new VerticalStackLayout { Padding = new Thickness(20, 22, 20, 40), Spacing = 14,
            MaximumWidthRequest = 1200, HorizontalOptions = LayoutOptions.Fill };
        root.Add(BrandHeader());
        root.Add(activity);
        root.Add(message);
        root.Add(body);
        scroll.Content = root;
        Content = scroll;
        SizeChanged += (_, _) =>
        {
            var nextWide = Width >= 720;
            if (nextWide == wide) return;
            wide = nextWide;
            ArrangeColumns();
        };
        Loaded += async (_, _) =>
        {
            if (initialized) return;
            initialized = true;
            ShowLogin();
            await RunAsync(async () =>
            {
                Connect(ConfiguredServerAddress());
                if (await api!.RestoreAsync()) await LoadSessionAsync();
            });
        };
    }

    private View BrandHeader()
    {
        var mark = new Border
        {
            WidthRequest = 46, HeightRequest = 46, Padding = 0, StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 15 },
            BackgroundColor = Peach,
            Content = new Label { Text = "H", FontSize = 24, FontAttributes = FontAttributes.Bold,
                TextColor = Coral, HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center }
        };
        var name = new Label { Text = "HamsterHub", FontSize = 28, FontAttributes = FontAttributes.Bold,
            VerticalTextAlignment = TextAlignment.Center };
        name.SetAppThemeColor(Label.TextColorProperty, Ink, Color.FromArgb("E8F5F1"));
        var header = new HorizontalStackLayout { Spacing = 12, Children = { mark, name } };
        SemanticProperties.SetDescription(header, "HamsterHub");
        return header;
    }

    private static string ConfiguredServerAddress()
    {
        var custom = Preferences.Default.Get(CustomServerPreference, "");
        if (!string.IsNullOrWhiteSpace(custom)) return custom;
        var legacy = Preferences.Default.Get(LegacyServerPreference, "");
        return string.IsNullOrWhiteSpace(legacy) ? DefaultServerAddress : legacy;
    }

    private void ResetScroll() => Dispatcher.Dispatch(() => _ = scroll.ScrollToAsync(0, 0, false));

    private static void SaveServerOverride(string address)
    {
        Preferences.Default.Remove(LegacyServerPreference);
        if (string.Equals(address, DefaultServerAddress, StringComparison.OrdinalIgnoreCase))
            Preferences.Default.Remove(CustomServerPreference);
        else
            Preferences.Default.Set(CustomServerPreference, address);
    }

    private void Connect(string address)
    {
        server = ServerAddress.Parse(address);
        http?.Dispose();
        http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { BaseAddress = server, Timeout = TimeSpan.FromSeconds(45) };
        api = new HamsterHubClient(http, new SecureSessionStore(server)) { Culture = Strings.Culture };
    }

    private Button Button(string key, Func<Task> action)
    {
        var button = new Button { Text = L(key), MinimumHeightRequest = 52, CornerRadius = 16,
            Margin = new Thickness(0, 0, 4, 4), BackgroundColor = MintDeep, TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold, FontSize = 16, Padding = new Thickness(16, 10) };
        SemanticProperties.SetDescription(button, L(key));
        button.Clicked += async (_, _) => await RunAsync(action);
        return button;
    }

    private Button SecondaryButton(string key, Func<Task> action)
    {
        var button = Button(key, action);
        button.BackgroundColor = Mint;
        button.TextColor = Ink;
        return button;
    }

    private Label Text(string text, int size = 18)
    {
        var label = new Label { Text = text, FontSize = size };
        label.SetAppThemeColor(Label.TextColorProperty, Ink, Color.FromArgb("E8F5F1"));
        return label;
    }

    private Border Card(View content, Color? light = null, Color? dark = null, float radius = 22, int stroke = 0)
    {
        var border = new Border { Content = content, Padding = 18, StrokeThickness = stroke,
            Stroke = Color.FromArgb("D9E8E3"), StrokeShape = new RoundRectangle { CornerRadius = radius } };
        border.SetAppThemeColor(BackgroundColorProperty, light ?? Paper, dark ?? Color.FromArgb("17312D"));
        return border;
    }

    private void AddSettings() => body.Add(SecondaryButton("MobileSettings", () =>
    { ShowSettings(); return Task.CompletedTask; }));

    private void ShowSettings()
    {
        ResetScroll(); body.Clear();
        var settings = new VerticalStackLayout { Spacing = 14, MaximumWidthRequest = 620 };
        settings.Add(Text(L("MobileSettings"), 28));
        settings.Add(SecondaryButton("SwitchLanguage", async () =>
        {
            Strings.Culture = Strings.Culture == "ru" ? "en" : "ru";
            Preferences.Default.Set("language", Strings.Culture);
            if (api is not null) api.Culture = Strings.Culture;
            if (session is not null && member is not null)
            {
                dashboard = await api!.GetDashboardAsync(member.Id);
                household = await api.GetHouseholdAsync(member.Id);
            }
            ShowSettings();
        }));
        settings.Add(SecondaryButton("ToggleTheme", () =>
        {
            var app = Application.Current!;
            app.UserAppTheme = app.RequestedTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
            Preferences.Default.Set("theme", app.UserAppTheme == AppTheme.Dark ? "dark" : "light");
            return Task.CompletedTask;
        }));
        if (session is not null)
            settings.Add(SecondaryButton("Logout", async () => { await api!.LogoutAsync(); ShowLogin(); }));
        settings.Add(SecondaryButton("MobileBack", () =>
        { if (session is null) ShowLogin(); else ShowDashboard(); return Task.CompletedTask; }));
        body.Add(Card(settings));
    }

    private Entry Field(string key, bool password = false)
    {
        var entry = new Entry { Placeholder = L(key), IsPassword = password, MinimumHeightRequest = 52 };
        entry.SetAppThemeColor(Entry.TextColorProperty, Ink, Color.FromArgb("E8F5F1"));
        entry.SetAppThemeColor(Entry.PlaceholderColorProperty, InkSoft, Color.FromArgb("A9C2BC"));
        SemanticProperties.SetDescription(entry, L(key));
        return entry;
    }

    private void ShowLogin()
    {
        ResetScroll();
        body.Clear();
        session = null;
        member = null;
        dashboard = null;
        household = null;
        selectedPhotos.Clear();
        AddSettings();
        var form = new VerticalStackLayout { Spacing = 14, MaximumWidthRequest = 480 };
        form.Add(Text(L("LoginTitle"), 26));
        form.Add(Text(L("MobileLoginIntro")));
        var address = Field("MobileServer");
        address.Keyboard = Keyboard.Url;
        address.Text = ConfiguredServerAddress();
        address.IsVisible = false;
        Button? advanced = null;
        advanced = SecondaryButton("MobileAdvancedSettings", () =>
        {
            address.IsVisible = !address.IsVisible;
            advanced!.Text = L(address.IsVisible ? "MobileHideAdvancedSettings" : "MobileAdvancedSettings");
            SemanticProperties.SetDescription(advanced, advanced.Text);
            return Task.CompletedTask;
        });
        form.Add(advanced);
        form.Add(address);
        var email = Field("Email"); email.Keyboard = Keyboard.Email;
        var password = Field("Password", true);
        var code = Field("MobileTwoFactor"); code.Keyboard = Keyboard.Numeric;
        code.IsVisible = false;
        foreach (var field in new[] { email, password, code }) form.Add(field);
        Button? login = null;
        login = Button("LoginSubmit", async () =>
        {
            var selectedAddress = address.IsVisible ? address.Text ?? "" : ConfiguredServerAddress();
            var selectedServer = ServerAddress.Parse(selectedAddress);
            if (address.IsVisible) SaveServerOverride(selectedServer.AbsoluteUri);
            Connect(selectedServer.AbsoluteUri);
            try
            {
                await api!.LoginAsync(email.Text ?? "", password.Text ?? "", code.IsVisible ? code.Text : null);
            }
            catch (MobileApiException exception) when (exception.Code == "TwoFactorRequired")
            {
                code.IsVisible = true;
                login!.Text = L("MobileVerifyCode");
                SemanticProperties.SetDescription(login, login.Text);
                message.Text = L("TwoFactorRequired");
                code.Focus();
                return;
            }
            password.Text = "";
            await LoadSessionAsync();
        });
        form.Add(login);
        form.Add(Text(L("MobileAccountHelp"), 14));
        body.Add(Card(form));
    }

    private async Task LoadSessionAsync()
    {
        var previousMember = member?.Id;
        session = await api!.GetSessionAsync();
        member = session.Memberships.FirstOrDefault(m => m.Id == previousMember) ?? session.Memberships.FirstOrDefault();
        if (member is null) { ShowDashboard(); return; }
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var dashboardTask = api!.GetDashboardAsync(member!.Id);
        var householdTask = api.GetHouseholdAsync(member.Id);
        await Task.WhenAll(dashboardTask, householdTask);
        dashboard = await dashboardTask;
        household = await householdTask;
        ShowDashboard();
    }

    private void ShowDashboard()
    {
        ResetScroll();
        showingCompletion = false;
        body.Clear();
        selectedPhotos.Clear();
        AddSettings();
        if (member is null || dashboard is null)
        {
            var accountRow = new Grid { ColumnDefinitions = new ColumnDefinitionCollection
                { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 12 };
            accountRow.Add(Text(session!.DisplayName, 24));
            body.Add(accountRow);
            body.Add(Card(Text(L("MobileNoHousehold"))));
            return;
        }
        ShowRoleDashboard();
    }

    private void ShowRoleDashboard()
    {
        var accountRow = new Grid { ColumnDefinitions = new ColumnDefinitionCollection
            { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 12 };
        var identity = new VerticalStackLayout { Spacing = 2 };
        identity.Add(Text(session!.DisplayName, 24));
        var role = Text(L("Role_" + member!.Role), 13);
        role.SetAppThemeColor(Label.TextColorProperty,
            member.Role == "Parent" ? MintDeep : Coral,
            member.Role == "Parent" ? Color.FromArgb("72C8B8") : Color.FromArgb("FF9B85"));
        role.FontAttributes = FontAttributes.Bold;
        identity.Add(role);
        accountRow.Add(identity);
        body.Add(accountRow);

        if (session.Memberships.Count > 1)
        {
            var picker = new Picker { Title = L("MobileHousehold"), ItemsSource = session.Memberships.ToList(),
                ItemDisplayBinding = new Binding(nameof(MemberDto.HouseholdName)), SelectedItem = member };
            picker.SetAppThemeColor(Picker.TextColorProperty, Ink, Color.FromArgb("E8F5F1"));
            SemanticProperties.SetDescription(picker, L("MobileHousehold"));
            picker.SelectedIndexChanged += async (_, _) =>
            {
                if (picker.SelectedItem is MemberDto next && next.Id != member?.Id)
                    await RunAsync(async () => { member = next; await RefreshAsync(); });
            };
            body.Add(picker);
        }

        if (member.Role == "Parent") ShowParentRoleDashboard(); else ShowChildRoleDashboard();
    }

    private static string Format(string key, params object[] values) => string.Format(
        System.Globalization.CultureInfo.GetCultureInfo(Strings.Culture), L(key), values);

    private void ShowChildRoleDashboard()
    {
        body.Add(RoleHero(false));
        var waiting = dashboard!.History.Count(item => item.Status == "Pending");
        body.Add(RoleStats(
            ("🐾", dashboard.Tasks.Count(item => item.CanComplete).ToString(), L("AvailableTasks"), Color.FromArgb("CDEBFA")),
            ("★", dashboard.Balance.ToString(), L("ApprovedPoints"), Color.FromArgb("FFF0A8")),
            ("⌛", waiting.ToString(), L("WaitingApproval"), Color.FromArgb("DDF3E8"))));
        columns.Children.Clear();
        columns.Add(RoleTaskSection(true));
        columns.Add(RoleHistorySection("RecentCare", true));
        ArrangeColumns();
        body.Add(columns);
        if (household is not null) body.Add(ChildHouseholdSection());
    }

    private void ShowParentRoleDashboard()
    {
        body.Add(RoleHero(true));
        body.Add(RoleStats(
            ("✓", dashboard!.Tasks.Count(item => item.CanComplete).ToString(), L("CareTasks"), Mint),
            ("!", dashboard.PendingApprovals.Count.ToString(), L("AwaitingApproval"), Color.FromArgb("FFF0C8")),
            ("↻", dashboard.History.Count.ToString(), L("RecentCare"), Peach)));

        var attention = new VerticalStackLayout { Spacing = 14 };
        attention.Add(RoleSectionHeading(dashboard.PendingApprovals.Count == 0 ? "✓" : "!",
            L("MobileApprovals"), L(dashboard.PendingApprovals.Count == 0 ? "MobileNoApprovals" : "AttentionHint")));
        foreach (var log in dashboard.PendingApprovals) attention.Add(RoleLogCard(log, true, false));
        body.Add(Card(attention, dashboard.PendingApprovals.Count == 0 ? Mint : Color.FromArgb("FFF7D8"),
            dashboard.PendingApprovals.Count == 0 ? Color.FromArgb("25443E") : Color.FromArgb("3C3525"), 26, 1));

        columns.Children.Clear();
        columns.Add(RoleTaskSection(false));
        columns.Add(RoleHistorySection("MobileParentHistory", false));
        ArrangeColumns();
        body.Add(columns);
        if (household is not null)
        {
            body.Add(ParentRewardRequests());
            body.Add(ParentManagementSection());
        }
    }

    private Border RoleHero(bool parent)
    {
        var copy = new VerticalStackLayout { Spacing = 7, VerticalOptions = LayoutOptions.Center };
        var eyebrow = Text(L(parent ? "ParentSpace" : "KidSpace"), 13);
        eyebrow.FontAttributes = FontAttributes.Bold;
        eyebrow.SetAppThemeColor(Label.TextColorProperty, parent ? MintDeep : Coral,
            parent ? Color.FromArgb("72C8B8") : Color.FromArgb("FF9B85"));
        copy.Add(eyebrow);
        var title = Text(Format(parent ? "ParentWelcome" : "KidWelcome", session!.DisplayName), parent ? 30 : 34);
        title.FontAttributes = FontAttributes.Bold;
        copy.Add(title);
        copy.Add(Text(parent ? Format("HouseholdSubtitle", member!.HouseholdName) : L("ReadyToCare"), 16));

        var score = new VerticalStackLayout { Spacing = 0, HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center };
        var scoreValue = Text((parent ? dashboard!.PendingApprovals.Count : dashboard!.Balance).ToString(), 34);
        scoreValue.HorizontalTextAlignment = TextAlignment.Center;
        scoreValue.FontAttributes = FontAttributes.Bold;
        score.Add(scoreValue);
        var scoreLabel = Text(L(parent ? "AwaitingApproval" : "ApprovedPoints"), 12);
        scoreLabel.HorizontalTextAlignment = TextAlignment.Center;
        score.Add(scoreLabel);
        var scoreBadge = Card(score, parent ? Paper : Color.FromArgb("FFF0A8"),
            parent ? Color.FromArgb("17312D") : Color.FromArgb("5A4A23"), parent ? 22 : 42, 1);
        scoreBadge.WidthRequest = 122;
        scoreBadge.MinimumHeightRequest = 112;

        var hero = new Grid { ColumnDefinitions = new ColumnDefinitionCollection
            { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 16 };
        hero.Add(copy);
        hero.Add(scoreBadge, 1);
        return Card(hero, parent ? Mint : Color.FromArgb("F7D8E7"),
            parent ? Color.FromArgb("25443E") : Color.FromArgb("4B3340"), parent ? 28 : 36, parent ? 1 : 3);
    }

    private Grid RoleStats(params (string Icon, string Value, string Label, Color Background)[] items)
    {
        var grid = new Grid { ColumnSpacing = 9 };
        foreach (var _ in items) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            var stack = new VerticalStackLayout { Spacing = 2, HorizontalOptions = LayoutOptions.Center };
            var icon = new Label { Text = item.Icon, FontSize = 23, HorizontalTextAlignment = TextAlignment.Center };
            icon.SetAppThemeColor(Label.TextColorProperty, Ink, Color.FromArgb("E8F5F1"));
            stack.Add(icon);
            var value = Text(item.Value, 27);
            value.FontAttributes = FontAttributes.Bold;
            value.HorizontalTextAlignment = TextAlignment.Center;
            stack.Add(value);
            var label = Text(item.Label, 11);
            label.HorizontalTextAlignment = TextAlignment.Center;
            label.LineBreakMode = LineBreakMode.TailTruncation;
            stack.Add(label);
            var card = Card(stack, item.Background, Color.FromArgb("25443E"), 21, 1);
            card.MinimumHeightRequest = 116;
            grid.Add(card, index);
        }
        return grid;
    }

    private View RoleSectionHeading(string icon, string title, string? subtitle = null)
    {
        var copy = new VerticalStackLayout { Spacing = 2 };
        var heading = Text(title, 23);
        heading.FontAttributes = FontAttributes.Bold;
        copy.Add(heading);
        if (!string.IsNullOrWhiteSpace(subtitle)) copy.Add(Text(subtitle, 14));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitionCollection
            { new(GridLength.Auto), new(GridLength.Star) }, ColumnSpacing = 10 };
        var iconLabel = new Label { Text = icon, FontSize = 28,
            VerticalTextAlignment = TextAlignment.Center };
        iconLabel.SetAppThemeColor(Label.TextColorProperty, Ink, Color.FromArgb("E8F5F1"));
        grid.Add(iconLabel);
        grid.Add(copy, 1);
        return grid;
    }

    private VerticalStackLayout RoleTaskSection(bool child)
    {
        var tasks = new VerticalStackLayout { Spacing = 16 };
        tasks.Add(RoleSectionHeading(child ? "🐹" : "✓", L(child ? "WhatWillYouDo" : "MobileParentTasks"),
            child ? L("ReadyToCare") : null));
        if (dashboard!.Tasks.Count == 0)
            tasks.Add(Card(Text(L("MobileNoTasks")), Mint, Color.FromArgb("25443E"), 22, 1));
        for (var index = 0; index < dashboard.Tasks.Count; index++)
            tasks.Add(RoleTaskCard(dashboard.Tasks[index], child, index));
        return tasks;
    }

    private Border RoleTaskCard(TaskDto task, bool child, int index)
    {
        var content = new VerticalStackLayout { Spacing = 10 };
        var image = new Image { Source = PhotoSource(task.ImagePath),
            HeightRequest = child ? 190 : 130, Aspect = Aspect.AspectFit };
        SemanticProperties.SetDescription(image, task.Name + ", " + task.PetName);
        content.Add(image);
        var titleRow = new Grid { ColumnDefinitions = new ColumnDefinitionCollection
            { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 10 };
        var names = new VerticalStackLayout { Spacing = 1 };
        var pet = Text(task.PetName, 14);
        pet.SetAppThemeColor(Label.TextColorProperty, child ? Coral : MintDeep,
            child ? Color.FromArgb("FF9B85") : Color.FromArgb("72C8B8"));
        pet.FontAttributes = FontAttributes.Bold;
        names.Add(pet);
        var name = Text(task.Name, child ? 23 : 20);
        name.FontAttributes = FontAttributes.Bold;
        names.Add(name);
        names.Add(Text(L("Frequency_" + task.Frequency), 13));
        titleRow.Add(names);
        var points = Card(Text("★ +" + task.Points, 17), Color.FromArgb("FFF0A8"), Color.FromArgb("5A4A23"), 18);
        titleRow.Add(points, 1);
        content.Add(titleRow);
        var complete = Button(task.CanComplete ? (child ? "DoneButton" : "MarkComplete") : "MobileAlreadyRecorded",
            () => { ShowCompletion(task); return Task.CompletedTask; });
        complete.IsEnabled = task.CanComplete;
        if (child) complete.BackgroundColor = Coral;
        content.Add(complete);
        var childColors = new[] { Color.FromArgb("F7D8E7"), Color.FromArgb("CDEBFA"),
            Color.FromArgb("DDF3E8"), Color.FromArgb("FFF0C8") };
        return Card(content, child ? childColors[index % childColors.Length] : Paper,
            child ? Color.FromArgb("4B3340") : Color.FromArgb("17312D"), child ? 30 : 22, child ? 3 : 1);
    }

    private VerticalStackLayout RoleHistorySection(string titleKey, bool child)
    {
        var history = new VerticalStackLayout { Spacing = 16 };
        history.Add(RoleSectionHeading(child ? "📷" : "↻", L(titleKey)));
        if (dashboard!.History.Count == 0)
            history.Add(Card(Text(L("MobileNoHistory")), child ? Color.FromArgb("FFF0C8") : Mint,
                Color.FromArgb("25443E"), 22, 1));
        foreach (var log in dashboard.History) history.Add(RoleLogCard(log, false, child));
        return history;
    }

    private Border RoleLogCard(CareLogDto log, bool review, bool child)
    {
        var content = new VerticalStackLayout { Spacing = 10 };
        var title = Text($"{log.PetName} · {log.TaskName}", 20);
        title.FontAttributes = FontAttributes.Bold;
        content.Add(title);
        if (review)
        {
            var memberName = Text(log.MemberName, 15);
            memberName.SetAppThemeColor(Label.TextColorProperty, Coral, Color.FromArgb("FF9B85"));
            memberName.FontAttributes = FontAttributes.Bold;
            content.Add(memberName);
        }
        var statusRow = new Grid { ColumnDefinitions = new ColumnDefinitionCollection
            { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 10 };
        statusRow.Add(Text(L("MobileStatus" + log.Status), 14));
        var points = Text("★ +" + log.Points, 16);
        points.FontAttributes = FontAttributes.Bold;
        points.SetAppThemeColor(Label.TextColorProperty, MintDeep, Color.FromArgb("72C8B8"));
        statusRow.Add(points, 1);
        content.Add(statusRow);
        content.Add(Text(log.CompletedAt.ToLocalTime().ToString("g",
            System.Globalization.CultureInfo.GetCultureInfo(Strings.Culture)), 14));
        foreach (var photo in log.Photos)
        {
            var image = new Image { Source = PhotoSource(photo), HeightRequest = 160, Aspect = Aspect.AspectFit };
            SemanticProperties.SetDescription(image, L("MobileCarePhoto"));
            content.Add(image);
        }
        if (review)
        {
            var actions = new Grid { ColumnDefinitions = new ColumnDefinitionCollection
                { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 10 };
            actions.Add(Button("MobileApprove", async () =>
            { await api!.ReviewAsync(member!.Id, log.Id, true); await RefreshAsync(); }));
            var reject = SecondaryButton("MobileReject", async () =>
            { await api!.ReviewAsync(member!.Id, log.Id, false); await RefreshAsync(); });
            reject.BackgroundColor = Peach;
            reject.SetAppThemeColor(Microsoft.Maui.Controls.Button.TextColorProperty, Coral, Color.FromArgb("7D2F24"));
            actions.Add(reject, 1);
            content.Add(actions);
        }
        return Card(content, child ? Color.FromArgb("FFFDF7") : Paper,
            child ? Color.FromArgb("3C3525") : Color.FromArgb("17312D"), child ? 26 : 20, 1);
    }

    private View ParentRewardRequests()
    {
        var section = new VerticalStackLayout { Spacing = 14 };
        section.Add(RoleSectionHeading("★", L("RewardWaiting"), L("RewardWaitingHint")));
        if (household!.RewardRequests.Count == 0) section.Add(Text(L("MobileNoApprovals"), 15));
        foreach (var request in household.RewardRequests)
        {
            var card = new VerticalStackLayout { Spacing = 9 };
            var title = Text(request.RewardName, 20); title.FontAttributes = FontAttributes.Bold;
            card.Add(title);
            card.Add(Text($"{request.ChildName} · {Format("PointsCount", request.PointsCost)}", 15));
            card.Add(Text(request.RequestedAt.ToLocalTime().ToString("g",
                System.Globalization.CultureInfo.GetCultureInfo(Strings.Culture)), 13));
            var actions = new Grid { ColumnDefinitions = new ColumnDefinitionCollection
                { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 10 };
            actions.Add(Button("Approve", async () =>
            { await api!.ReviewRewardAsync(member!.Id, request.Id, true); await RefreshAsync(); }));
            actions.Add(SecondaryButton("Reject", async () =>
            { await api!.ReviewRewardAsync(member!.Id, request.Id, false); await RefreshAsync(); }), 1);
            card.Add(actions);
            section.Add(Card(card, Color.FromArgb("FFF7D8"), Color.FromArgb("3C3525"), 22, 1));
        }
        return Card(section, Paper, Color.FromArgb("17312D"), 26, 1);
    }

    private View ParentManagementSection()
    {
        var section = new VerticalStackLayout { Spacing = 16 };
        section.Add(RoleSectionHeading("⚙", L("FamilySetup"), L("FamilySetupHint")));
        var actions = new FlexLayout { Wrap = FlexWrap.Wrap, JustifyContent = FlexJustify.Start };
        actions.Add(Button("AddFamilyMember", () => { ShowAddMember(); return Task.CompletedTask; }));
        actions.Add(Button("AddPet", () => { ShowAddPet(); return Task.CompletedTask; }));
        actions.Add(Button("AddCareTask", () => { ShowAddTask(); return Task.CompletedTask; }));
        actions.Add(Button("AddReward", () => { ShowAddReward(); return Task.CompletedTask; }));
        section.Add(actions);

        section.Add(ManagementHeading("FamilyMembers"));
        foreach (var item in household!.Members)
        {
            var row = ManagementRow(item.DisplayName, $"{L("Role_" + item.Role)} · {item.Email}",
                () => { ShowEditMember(item); return Task.CompletedTask; },
                item.IsCurrentUser ? null : async () =>
                {
                    if (await ConfirmDelete(item.DisplayName))
                    { await api!.ArchiveMemberAsync(member!.Id, item.Id); await RefreshAsync(); }
                });
            section.Add(row);
        }

        section.Add(ManagementHeading("Pets"));
        foreach (var item in household.Pets)
            section.Add(ManagementRow(item.Name, item.Species,
                () => { ShowEditPet(item); return Task.CompletedTask; }, async () =>
            {
                if (await ConfirmDelete(item.Name))
                { await api!.ArchivePetAsync(member!.Id, item.Id); await RefreshAsync(); }
            }));

        section.Add(ManagementHeading("CareTasks"));
        foreach (var item in household.Tasks)
            section.Add(ManagementRow(item.Name, $"{item.PetName} · {item.AssignedMemberName} · ★ {item.Points}",
                () => { ShowEditTask(item); return Task.CompletedTask; }, async () =>
            {
                if (await ConfirmDelete(item.Name))
                { await api!.ArchiveTaskAsync(member!.Id, item.Id); await RefreshAsync(); }
            }));

        section.Add(ManagementHeading("Rewards"));
        foreach (var reward in household.Rewards)
        {
            var card = new VerticalStackLayout { Spacing = 8 };
            var title = Text(reward.Name, 19); title.FontAttributes = FontAttributes.Bold; card.Add(title);
            card.Add(Text(Format("RewardCostCount", reward.PointCost), 14));
            var children = household.Members.Where(item => item.Role == "Child").ToList();
            if (children.Count > 0)
            {
                var picker = PickerFor(L("ChooseChild"), children, nameof(HouseholdMemberItemDto.DisplayName));
                card.Add(picker);
                card.Add(SecondaryButton("MobileGiveReward", async () =>
                {
                    if (picker.SelectedItem is HouseholdMemberItemDto child)
                    { await api!.PurchaseRewardAsync(member!.Id, reward.Id, child.Id); await RefreshAsync(); }
                }));
            }
            section.Add(Card(card, Mint, Color.FromArgb("25443E"), 20, 1));
        }
        return Card(section, Paper, Color.FromArgb("17312D"), 28, 1);
    }

    private View ChildHouseholdSection()
    {
        var section = new VerticalStackLayout { Spacing = 16 };
        section.Add(RoleSectionHeading("🎁", L("Rewards"), L("RewardCatalogHint")));
        if (household!.Rewards.Count == 0) section.Add(Text(L("MobileNoRewards"), 15));
        foreach (var reward in household.Rewards)
        {
            var card = new VerticalStackLayout { Spacing = 8 };
            if (!string.IsNullOrWhiteSpace(reward.ImagePath))
                card.Add(new Image { Source = PhotoSource(reward.ImagePath), HeightRequest = 150,
                    Aspect = Aspect.AspectFit });
            var title = Text(reward.Name, 21); title.FontAttributes = FontAttributes.Bold; card.Add(title);
            card.Add(Text(Format("RewardCostCount", reward.PointCost), 15));
            var request = Button(reward.HasPendingRequest ? "RewardWaiting" : "RequestReward", async () =>
            { await api!.RequestRewardAsync(member!.Id, reward.Id); await RefreshAsync(); });
            request.IsEnabled = reward.CanAfford && !reward.HasPendingRequest;
            card.Add(request);
            section.Add(Card(card, Color.FromArgb("FFF0C8"), Color.FromArgb("3C3525"), 28, 2));
        }

        section.Add(RoleSectionHeading("👪", L("FamilyMembers")));
        foreach (var item in household.Members)
            section.Add(ManagementRow(item.DisplayName,
                $"{L("Role_" + item.Role)} · ★ {item.Balance}", null, null));
        section.Add(RoleSectionHeading("🐾", L("FamilyPets")));
        foreach (var item in household.Pets)
            section.Add(ManagementRow(item.Name, item.Species, null, null));
        if (household.RewardHistory.Count > 0)
        {
            section.Add(RoleSectionHeading("↻", L("PointHistory")));
            foreach (var item in household.RewardHistory)
                section.Add(ManagementRow(item.RewardName,
                    $"{L("RewardStatus_" + item.Status)} · −{item.PointsCost} ★", null, null));
        }
        return Card(section, Color.FromArgb("FFFDF7"), Color.FromArgb("17312D"), 30, 2);
    }

    private Label ManagementHeading(string key)
    {
        var heading = Text(L(key), 22); heading.FontAttributes = FontAttributes.Bold; return heading;
    }

    private View ManagementRow(string titleText, string detail, Func<Task>? edit, Func<Task>? delete)
    {
        var copy = new VerticalStackLayout { Spacing = 2 };
        var title = Text(titleText, 18); title.FontAttributes = FontAttributes.Bold; copy.Add(title);
        copy.Add(Text(detail, 14));
        if (edit is null && delete is null) return Card(copy, Mint, Color.FromArgb("25443E"), 18, 1);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitionCollection
            { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 10 };
        grid.Add(copy);
        var actions = new HorizontalStackLayout { Spacing = 8 };
        if (edit is not null) actions.Add(SecondaryButton("Edit", edit));
        if (delete is not null)
        {
            var remove = SecondaryButton("Delete", delete); remove.BackgroundColor = Peach; actions.Add(remove);
        }
        grid.Add(actions, 1);
        return Card(grid, Paper, Color.FromArgb("17312D"), 18, 1);
    }

    private async Task<bool> ConfirmDelete(string name) =>
        await DisplayAlertAsync(L("Delete"), name, L("Delete"), L("Cancel"));

    private Picker PickerFor<T>(string title, IReadOnlyList<T> items, string displayProperty)
    {
        var picker = new Picker { Title = title, ItemsSource = items.ToList(),
            ItemDisplayBinding = new Binding(displayProperty) };
        picker.SetAppThemeColor(Picker.TextColorProperty, Ink, Color.FromArgb("E8F5F1"));
        if (items.Count > 0) picker.SelectedIndex = 0;
        return picker;
    }

    private Picker ChoicePicker(string title, IReadOnlyList<Choice> choices, string selectedValue)
    {
        var picker = PickerFor(title, choices, nameof(Choice.Label));
        picker.SelectedIndex = Math.Max(0, choices.ToList().FindIndex(choice => choice.Value == selectedValue));
        return picker;
    }

    private void ShowForm(string titleKey, Action<VerticalStackLayout> build)
    {
        ResetScroll(); body.Clear(); selectedPhotos.Clear();
        var form = new VerticalStackLayout { Spacing = 14, MaximumWidthRequest = 620 };
        var title = Text(L(titleKey), 28); title.FontAttributes = FontAttributes.Bold; form.Add(title);
        build(form);
        form.Add(SecondaryButton("MobileBack", () => { ShowDashboard(); return Task.CompletedTask; }));
        body.Add(Card(form, Paper, Color.FromArgb("17312D"), 28, 1));
    }

    private void ShowAddMember() => ShowForm("AddFamilyMember", form =>
    {
        var name = Field("DisplayName"); var email = Field("Email"); email.Keyboard = Keyboard.Email;
        var password = Field("TemporaryPassword", true);
        FileResult? photo = null;
        var roles = new[] { new Choice("Child", L("Role_Child")),
            new Choice("Parent", L("Role_Parent")) };
        var role = ChoicePicker(L("FamilyRole"), roles, "Child");
        form.Add(name); form.Add(email); form.Add(password); form.Add(role);
        AddPhotoPicker(form, "MemberPhoto", file => photo = file);
        form.Add(Button("AddFamilyMember", async () =>
        {
            var id = await api!.AddMemberAsync(member!.Id, new CreateMemberRequest(name.Text ?? "", email.Text ?? "",
                password.Text ?? "", (role.SelectedItem as Choice)?.Value ?? "Child"));
            if (photo is not null) await UploadManagementPhotoAsync("members", id, photo);
            message.Text = Format("FamilyMemberAdded", name.Text ?? ""); await RefreshAsync();
        }));
    });

    private void ShowAddPet() => ShowForm("AddPet", form =>
    {
        var name = Field("PetName"); var species = Field("Species"); var birth = Field("BirthDate");
        FileResult? photo = null;
        birth.Keyboard = Keyboard.Numeric; form.Add(name); form.Add(species); form.Add(birth);
        AddPhotoPicker(form, "PetPhoto", file => photo = file);
        form.Add(Button("SavePet", async () =>
        {
            DateOnly? date = DateOnly.TryParse(birth.Text, out var parsed) ? parsed : null;
            var id = await api!.AddPetAsync(member!.Id, new CreatePetRequest(name.Text ?? "", species.Text ?? "", date));
            if (photo is not null) await UploadManagementPhotoAsync("pets", id, photo);
            message.Text = Format("PetAdded", name.Text ?? ""); await RefreshAsync();
        }));
    });

    private void ShowAddTask() => ShowForm("AddCareTask", form =>
    {
        var pets = household!.Pets.ToList(); var members = household.Members.ToList();
        pets.Insert(0, new PetItemDto(0, L("NoPet"), "", null, null));
        var categories = household.Categories.ToList();
        categories.Insert(0, new CategoryItemDto(0, L("NoCategory")));
        var pet = PickerFor(L("ChoosePet"), pets, nameof(PetItemDto.Name));
        var assignee = PickerFor(L("ChooseMember"), members, nameof(HouseholdMemberItemDto.DisplayName));
        var category = PickerFor(L("ChooseCategory"), categories, nameof(CategoryItemDto.Name));
        var name = Field("TaskName");
        var custom = Field("NewCategory");
        FileResult? photo = null;
        var frequencies = new[] { new Choice("Daily", L("Frequency_Daily")),
            new Choice("Weekly", L("Frequency_Weekly")),
            new Choice("AsNeeded", L("Frequency_AsNeeded")) };
        var frequency = ChoicePicker(L("TaskFrequency"), frequencies, "Daily");
        var points = Field("PointValue"); points.Text = "5"; points.Keyboard = Keyboard.Numeric;
        form.Add(name); form.Add(pet); form.Add(assignee); form.Add(category); form.Add(custom); form.Add(frequency); form.Add(points);
        AddPhotoPicker(form, "TaskImage", file => photo = file);
        form.Add(Button("SaveCareTask", async () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) throw new MobileApiException("TaskNameRequired");
            var selectedPet = pet.SelectedItem as PetItemDto;
            if (assignee.SelectedItem is not HouseholdMemberItemDto selectedMember) return;
            int.TryParse(points.Text, out var pointValue);
            var categoryId = category.SelectedItem is CategoryItemDto { Id: > 0 } selectedCategory ? selectedCategory.Id : (int?)null;
            var id = await api!.AddTaskAsync(member!.Id, new CreateTaskRequest(selectedPet is { Id: > 0 } ? selectedPet.Id : null, selectedMember.Id,
                categoryId, custom.Text, (frequency.SelectedItem as Choice)?.Value ?? "Daily", pointValue, name.Text.Trim()));
            if (photo is not null) await UploadManagementPhotoAsync("tasks", id, photo);
            await RefreshAsync();
        }));
    });

    private void ShowAddReward() => ShowForm("AddReward", form =>
    {
        var name = Field("RewardName"); var cost = Field("RewardPointCost"); cost.Text = "10";
        FileResult? photo = null;
        cost.Keyboard = Keyboard.Numeric; form.Add(name); form.Add(cost);
        AddPhotoPicker(form, "RewardImage", file => photo = file);
        var choices = new List<(int Id, CheckBox Box)>();
        foreach (var child in household!.Members.Where(item => item.Role == "Child"))
        {
            var box = new CheckBox(); choices.Add((child.Id, box));
            var row = new HorizontalStackLayout { Spacing = 10, Children = { box, Text(child.DisplayName, 17) } };
            form.Add(row);
        }
        form.Add(Button("SaveReward", async () =>
        {
            int.TryParse(cost.Text, out var pointCost);
            var id = await api!.AddRewardAsync(member!.Id, new CreateRewardRequest(name.Text ?? "", pointCost,
                choices.Where(item => item.Box.IsChecked).Select(item => item.Id).ToList()));
            if (photo is not null) await UploadManagementPhotoAsync("rewards", id, photo);
            await RefreshAsync();
        }));
    });

    private void ShowEditMember(HouseholdMemberItemDto item) => ShowForm("EditFamilyMember", form =>
    {
        var name = Field("DisplayName"); name.Text = item.DisplayName;
        FileResult? photo = null;
        var roles = new[] { new Choice("Child", L("Role_Child")),
            new Choice("Parent", L("Role_Parent")) };
        var role = ChoicePicker(L("FamilyRole"), roles, item.Role);
        form.Add(name); form.Add(role);
        AddPhotoPicker(form, "MemberPhoto", file => photo = file);
        form.Add(Button("SaveChanges", async () =>
        {
            await api!.UpdateMemberAsync(member!.Id, item.Id, new UpdateMemberRequest(name.Text ?? "",
                (role.SelectedItem as Choice)?.Value ?? item.Role));
            if (photo is not null) await UploadManagementPhotoAsync("members", item.Id, photo);
            await LoadSessionAsync();
        }));
    });

    private void ShowEditPet(PetItemDto item) => ShowForm("EditPet", form =>
    {
        var name = Field("PetName"); name.Text = item.Name;
        var species = Field("Species"); species.Text = item.Species;
        FileResult? photo = null;
        var birth = Field("BirthDate"); birth.Text = item.BirthDate?.ToString("yyyy-MM-dd");
        birth.Keyboard = Keyboard.Numeric; form.Add(name); form.Add(species); form.Add(birth);
        AddPhotoPicker(form, "PetPhoto", file => photo = file);
        form.Add(Button("SaveChanges", async () =>
        {
            DateOnly? date = DateOnly.TryParse(birth.Text, out var parsed) ? parsed : null;
            await api!.UpdatePetAsync(member!.Id, item.Id,
                new UpdatePetRequest(name.Text ?? "", species.Text ?? "", date));
            if (photo is not null) await UploadManagementPhotoAsync("pets", item.Id, photo);
            await RefreshAsync();
        }));
    });

    private void ShowEditTask(ManagedTaskItemDto item) => ShowForm("EditCareTask", form =>
    {
        var pets = household!.Pets.ToList(); var members = household.Members.ToList();
        pets.Insert(0, new PetItemDto(0, L("NoPet"), "", null, null));
        var categories = household.Categories.ToList();
        categories.Insert(0, new CategoryItemDto(0, L("NoCategory")));
        var pet = PickerFor(L("ChoosePet"), pets, nameof(PetItemDto.Name));
        pet.SelectedIndex = Math.Max(0, pets.FindIndex(value => value.Id == item.PetId));
        var assignee = PickerFor(L("ChooseMember"), members, nameof(HouseholdMemberItemDto.DisplayName));
        assignee.SelectedIndex = Math.Max(0, members.FindIndex(value => value.Id == item.AssignedMemberId));
        var category = PickerFor(L("ChooseCategory"), categories, nameof(CategoryItemDto.Name));
        category.SelectedIndex = Math.Max(0, categories.FindIndex(value => value.Id == item.CategoryId));
        var name = Field("TaskName"); name.Text = item.Name;
        var custom = Field("NewCategory");
        FileResult? photo = null;
        var frequencies = new[] { new Choice("Daily", L("Frequency_Daily")),
            new Choice("Weekly", L("Frequency_Weekly")),
            new Choice("AsNeeded", L("Frequency_AsNeeded")) };
        var frequency = ChoicePicker(L("TaskFrequency"), frequencies, item.Frequency);
        var points = Field("PointValue"); points.Text = item.Points.ToString(); points.Keyboard = Keyboard.Numeric;
        form.Add(name); form.Add(pet); form.Add(assignee); form.Add(category); form.Add(custom); form.Add(frequency); form.Add(points);
        AddPhotoPicker(form, "TaskImage", file => photo = file);
        form.Add(Button("SaveChanges", async () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) throw new MobileApiException("TaskNameRequired");
            var selectedPet = pet.SelectedItem as PetItemDto;
            if (assignee.SelectedItem is not HouseholdMemberItemDto selectedMember) return;
            int.TryParse(points.Text, out var pointValue);
            var categoryId = category.SelectedItem is CategoryItemDto { Id: > 0 } selectedCategory ? selectedCategory.Id : (int?)null;
            await api!.UpdateTaskAsync(member!.Id, item.Id, new UpdateTaskRequest(selectedPet is { Id: > 0 } ? selectedPet.Id : null,
                selectedMember.Id, categoryId, custom.Text,
                (frequency.SelectedItem as Choice)?.Value ?? item.Frequency, pointValue, name.Text.Trim()));
            if (photo is not null) await UploadManagementPhotoAsync("tasks", item.Id, photo);
            await RefreshAsync();
        }));
    });

    private async Task<IReadOnlyList<FileResult>> ChoosePhotosAsync(int limit)
    {
        var choice = await DisplayActionSheetAsync(L("MobileAddPhoto"), L("Cancel"), null,
            L("MobileTakePhoto"), L("MobilePickPhoto"));
        List<FileResult> files = [];
        if (choice == L("MobileTakePhoto"))
        {
            if (!MediaPicker.Default.IsCaptureSupported) throw new MobileApiException("MobileCameraUnavailable");
            var photo = await MediaPicker.Default.CapturePhotoAsync();
            if (photo is not null) files.Add(photo);
        }
        else if (choice == L("MobilePickPhoto"))
            files = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions { SelectionLimit = limit });
        if (files.Count > limit) throw new MobileApiException("MobilePhotoLimit");
        foreach (var file in files) ValidatePhoto(file);
        return files;
    }

    private void AddPhotoPicker(VerticalStackLayout form, string key, Action<FileResult?> setPhoto)
    {
        var preview = new Image { IsVisible = false, HeightRequest = 150, Aspect = Aspect.AspectFit };
        var remove = SecondaryButton("MobileRemovePhoto", () =>
        { setPhoto(null); preview.Source = null; preview.IsVisible = false; return Task.CompletedTask; });
        remove.IsVisible = false;
        form.Add(SecondaryButton(key, async () =>
        {
            var file = (await ChoosePhotosAsync(1)).FirstOrDefault();
            if (file is null) return;
            setPhoto(file);
            preview.Source = ImageSource.FromStream(_ => file.OpenReadAsync());
            preview.IsVisible = true; remove.IsVisible = true;
        }));
        preview.PropertyChanged += (_, args) =>
        { if (args.PropertyName == nameof(IsVisible)) remove.IsVisible = preview.IsVisible; };
        form.Add(preview); form.Add(remove);
    }

    private Task UploadManagementPhotoAsync(string kind, int id, FileResult file) =>
        api!.UpdateMediaAsync(member!.Id, kind, id,
            new UploadPhoto(file.FileName, MimeType(file), file.OpenReadAsync));

    private void ArrangeColumns()
    {
        columns.ColumnDefinitions.Clear(); columns.RowDefinitions.Clear();
        columns.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        if (wide) columns.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        columns.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        if (!wide) columns.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        if (columns.Children.Count > 1)
        {
            Grid.SetColumn((BindableObject)columns.Children[1], wide ? 1 : 0);
            Grid.SetRow((BindableObject)columns.Children[1], wide ? 0 : 1);
        }
    }

    private ImageSource PhotoSource(string path)
    {
        if (path.StartsWith("/images/tasks/", StringComparison.Ordinal))
            return ImageSource.FromFile(System.IO.Path.GetFileName(path).Replace(".svg", ".png"));
        var client = api!;
        var memberId = member!.Id;
        return ImageSource.FromStream(async _ =>
        {
            try { return new MemoryStream(await client.GetPhotoAsync(memberId, path)); }
            catch { return Stream.Null; }
        });
    }

    private void ShowCompletion(TaskDto task)
    {
        ResetScroll();
        showingCompletion = true;
        body.Clear();
        body.Add(Text(task.PetName + " · " + task.Name, 26));
        body.Add(Text(L("MobileCompletionHelp")));
        var previews = new VerticalStackLayout { Spacing = 10 };
        void RefreshPreviews()
        {
            previews.Clear();
            foreach (var file in selectedPhotos.ToList())
            {
                previews.Add(new Image { Source = ImageSource.FromStream(_ => file.OpenReadAsync()),
                    HeightRequest = 140, Aspect = Aspect.AspectFit });
                previews.Add(Button("MobileRemovePhoto", () =>
                { selectedPhotos.Remove(file); RefreshPreviews(); return Task.CompletedTask; }));
            }
        }
        body.Add(Button("MobileAddPhoto", async () =>
        {
            if (selectedPhotos.Count >= 8) throw new MobileApiException("MobilePhotoLimit");
            var files = await ChoosePhotosAsync(8 - selectedPhotos.Count);
            selectedPhotos.AddRange(files);
            RefreshPreviews();
        }));
        body.Add(previews);
        body.Add(Button("MobileSendCompletion", async () =>
        {
            var photos = selectedPhotos.Select(f => new UploadPhoto(f.FileName, MimeType(f), f.OpenReadAsync)).ToList();
            await api!.CompleteAsync(member!.Id, task.Id, photos);
            selectedPhotos.Clear();
            message.Text = L(member.Role == "Parent" ? "ParentTaskCompleted" : "TaskSentForApproval");
            await RefreshAsync();
        }));
        body.Add(Button("MobileBack", () => { ShowDashboard(); return Task.CompletedTask; }));
    }

    private static string MimeType(FileResult file) => System.IO.Path.GetExtension(file.FileName).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp",
        _ => throw new MobileApiException("InvalidTaskPhoto")
    };
    private static void ValidatePhoto(FileResult file) => _ = MimeType(file);

    protected override bool OnBackButtonPressed()
    {
        if (busy) return true;
        if (!showingCompletion) return base.OnBackButtonPressed();
        ShowDashboard();
        return true;
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true; body.IsEnabled = false; activity.IsVisible = true; activity.IsRunning = true; message.Text = ""; message.IsVisible = false;
        try { await action(); }
        catch (MobileApiException exception)
        {
            if (exception.Code is "SessionExpired" or "MobileAccessDenied")
            {
                if (api is not null) await api.LogoutAsync();
                ShowLogin();
            }
            message.Text = L(exception.Code == "TooManyTaskPhotos" ? "MobilePhotoLimit" : exception.Code);
        }
        catch (PermissionException) { message.Text = L("MobileCameraPermission"); }
        catch (Exception) { message.Text = L("MobileConnectionError"); }
        finally { busy = false; body.IsEnabled = true; activity.IsRunning = false; activity.IsVisible = false; message.IsVisible = !string.IsNullOrEmpty(message.Text); }
    }
}
