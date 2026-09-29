using HamsterHub.Client;
using HamsterHub.Contracts;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;

namespace HamsterHub.Mobile;

public sealed class MainPage : ContentPage
{
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

    private void AddSettings()
    {
        var settings = new FlexLayout { Wrap = FlexWrap.Wrap };
        settings.Add(SecondaryButton("SwitchLanguage", async () =>
        {
            Strings.Culture = Strings.Culture == "ru" ? "en" : "ru";
            Preferences.Default.Set("language", Strings.Culture);
            if (api is not null) api.Culture = Strings.Culture;
            if (session is null) ShowLogin(); else await LoadSessionAsync();
        }));
        settings.Add(SecondaryButton("ToggleTheme", () =>
        {
            var app = Application.Current!;
            app.UserAppTheme = app.RequestedTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
            Preferences.Default.Set("theme", app.UserAppTheme == AppTheme.Dark ? "dark" : "light");
            return Task.CompletedTask;
        }));
        body.Add(settings);
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
        dashboard = await api!.GetDashboardAsync(member!.Id);
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
            accountRow.Add(SecondaryButton("Logout", async () => { await api!.LogoutAsync(); ShowLogin(); }), 1);
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
        accountRow.Add(SecondaryButton("Logout", async () => { await api!.LogoutAsync(); ShowLogin(); }), 1);
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
        grid.Add(new Label { Text = icon, FontSize = 28, VerticalTextAlignment = TextAlignment.Center });
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
            return ImageSource.FromFile(System.IO.Path.GetFileName(path));
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
        body.Add(Button("MobilePickPhoto", async () =>
        {
            if (selectedPhotos.Count >= 8) throw new MobileApiException("MobilePhotoLimit");
            var file = await FilePicker.Default.PickAsync(new PickOptions { FileTypes = FilePickerFileType.Images });
            if (file is not null) { ValidatePhoto(file); selectedPhotos.Add(file); RefreshPreviews(); }
        }));
        body.Add(Button("MobileTakePhoto", async () =>
        {
            if (selectedPhotos.Count >= 8) throw new MobileApiException("MobilePhotoLimit");
            if (!MediaPicker.Default.IsCaptureSupported) throw new MobileApiException("MobileCameraUnavailable");
            var file = await MediaPicker.Default.CapturePhotoAsync();
            if (file is not null) { ValidatePhoto(file); selectedPhotos.Add(file); RefreshPreviews(); }
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
