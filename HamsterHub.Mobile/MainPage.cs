using HamsterHub.Client;
using HamsterHub.Contracts;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;

namespace HamsterHub.Mobile;

public sealed partial class MainPage : ContentPage
{
    private sealed record Choice(string Value, string Label);

    private const string DefaultServerAddress = "http://95.165.103.141:5080/";
    private const string CustomServerPreference = "custom-server";
    private const string LegacyServerPreference = "server";
    private const string AutoUpdateCheckPreference = "automatically-check-updates";
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
    private DateTimeOffset? lastUpdatedAt;
    private AndroidReleaseDto? availableUpdate;
    private Uri? updateServer;
    private DateTimeOffset lastUpdateCheck;
    private bool checkingUpdate;
    private readonly VerticalStackLayout updateContent = new() { Spacing = 10 };
    private Border updateCard = null!;
    private readonly VerticalStackLayout body = new() { Spacing = 16 };
    private readonly Label message = new() { FontSize = 16, IsVisible = false };
    private readonly ActivityIndicator activity = new() { IsVisible = false, HeightRequest = 24 };
    private readonly Grid columns = new() { ColumnSpacing = 24, RowSpacing = 24 };
    private readonly ScrollView scroll = new();
    private readonly RefreshView refreshView = new();
    private readonly Microsoft.Maui.Dispatching.IDispatcherTimer dashboardRefreshTimer;
    private bool foreground = true;
    private readonly List<FileResult> selectedPhotos = [];
    private bool initialized;
    private bool busy;
    private bool wide;
    private bool showingCompletion;
    private bool showingForm;
    private bool showingSettings;
    private readonly Dictionary<Entry, (string Key, Label Error)> formInputs = [];
    private readonly Dictionary<Picker, (string Title, bool Required, Label Error)> formPickers = [];
    private readonly List<(Func<string?> Validate, Label Error)> formGroups = [];
    private readonly Dictionary<string, Label> formPhotoErrors = [];
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
        updateCard = Card(updateContent, Mint);
        updateCard.IsVisible = false;
        root.Add(activity);
        root.Add(message);
        root.Add(body);
        scroll.Content = root;
        refreshView.Content = scroll;
        refreshView.Refreshing += async (_, _) =>
        {
            try
            {
                if (session is not null && member is not null && !showingCompletion && !showingForm && !photoViewerOpen)
                    await RunAsync(RefreshAsync);
            }
            finally { refreshView.IsRefreshing = false; }
        };
        Content = refreshView;
        dashboardRefreshTimer = Dispatcher.CreateTimer();
        dashboardRefreshTimer.Interval = TimeSpan.FromMinutes(1);
        dashboardRefreshTimer.IsRepeating = true;
        dashboardRefreshTimer.Tick += async (_, _) =>
        {
            if (!foreground || busy || session is null || member is null || showingCompletion || showingForm || photoViewerOpen) return;
            await RunAsync(() => RefreshAsync(preserveScroll: true));
        };
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
            if (Window is { } window)
            {
                window.Activated += (_, _) => { foreground = true; dashboardRefreshTimer.Start(); };
                window.Deactivated += (_, _) => { foreground = false; dashboardRefreshTimer.Stop(); };
                window.Stopped += (_, _) => { foreground = false; dashboardRefreshTimer.Stop(); };
                window.Resumed += async (_, _) =>
                {
                    foreground = true;
                    dashboardRefreshTimer.Start();
                    await RunAsync(async () => { AppUpdateInstaller.Continue(); await Task.CompletedTask; });
                    if (showingSettings) ShowSettings();
                    if (session is not null && member is not null && !showingCompletion && !showingForm && !photoViewerOpen)
                        await RunAsync(RefreshAsync);
                    await CheckUpdateAsync();
                };
            }
            dashboardRefreshTimer.Start();
            await SavedAccounts.LoadAsync();
            ShowLogin();
            await RunAsync(async () =>
            {
                Connect(ConfiguredServerAddress());
                if (!Preferences.Default.Get("restore-active-session", true))
                { await api!.LogoutAsync(); TaskNotifications.Clear(); }
                else if (await api!.RestoreAsync()) await LoadSessionAsync();
            });
            await CheckUpdateAsync();
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
        availableUpdate = null;
        updateCard.IsVisible = false;
        lastUpdateCheck = default;
        http?.Dispose();
        http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { BaseAddress = server, Timeout = TimeSpan.FromSeconds(45) };
        api = new HamsterHubClient(http, new SecureSessionStore(server)) { Culture = Strings.Culture };
    }

    private Button Button(string key, Func<Task> action)
    {
        var button = new Button { Text = string.IsNullOrEmpty(ButtonIcon(key)) ? L(key) : ButtonIcon(key) + "  " + L(key), MinimumHeightRequest = 52, CornerRadius = 16,
            Margin = new Thickness(0, 0, 4, 4), BackgroundColor = MintDeep, TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold, FontSize = 16, Padding = new Thickness(16, 10) };
        SemanticProperties.SetDescription(button, L(key));
        button.Clicked += async (_, _) =>
        {
            if (key is "LoginSubmit" or "SaveChanges" or "SavePet" or "SaveCareTask" or "SaveReward" ||
                (key == "AddFamilyMember" && showingForm))
                if (!ValidateInputs()) return;
            await RunAsync(action);
        };
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
        ResetScroll(); body.Clear(); showingForm = true; showingSettings = true; formInputs.Clear(); formPickers.Clear(); formGroups.Clear(); formPhotoErrors.Clear();
        var settings = new VerticalStackLayout { Spacing = 14, MaximumWidthRequest = 620 };
        settings.Add(Text(L("MobileSettings"), 28));
        var automaticUpdates = new Switch { IsToggled = Preferences.Default.Get(AutoUpdateCheckPreference, true) };
        SemanticProperties.SetDescription(automaticUpdates, L("AppAutoCheckUpdates"));
        var automaticUpdateRow = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 12 };
        automaticUpdateRow.Add(Text(L("AppAutoCheckUpdates"), 16));
        automaticUpdateRow.Add(automaticUpdates, 1);
        automaticUpdates.Toggled += async (_, args) =>
        {
            Preferences.Default.Set(AutoUpdateCheckPreference, args.Value);
            if (args.Value) await CheckUpdateAsync();
        };
        settings.Add(automaticUpdateRow);
        settings.Add(SecondaryButton("AppCheckUpdates", () => CheckUpdateAsync(manual: true)));
        if (updateCard.Parent is Microsoft.Maui.Controls.Layout previousSettings) previousSettings.Remove(updateCard);
        RenderUpdateCard();
        settings.Add(updateCard);
        if (!TaskNotifications.NotificationsAllowed)
        {
            settings.Add(PermissionWarning("NotificationPermissionWarning"));
            settings.Add(SecondaryButton("AllowNotifications", async () =>
            { await TaskNotifications.RequestPermissionAsync(); ShowSettings(); }));
        }
        if (!TaskNotifications.ExactAlarmsAllowed)
        {
            settings.Add(PermissionWarning("AlarmPermissionWarning"));
            settings.Add(SecondaryButton("AllowExactAlarms", TaskNotifications.OpenExactAlarmSettingsAsync));
        }
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
        {
            settings.Add(SecondaryButton("MobileSwitchAccount", async () =>
            { TaskNotifications.Clear(); await api!.LogoutAsync(); ShowLogin(); }));
            settings.Add(SecondaryButton("Logout", async () => { TaskNotifications.Clear(); await api!.LogoutAsync(); ShowLogin(); }));
        }
        settings.Add(SecondaryButton("MobileBack", () =>
        { if (session is null) ShowLogin(); else ShowDashboard(); return Task.CompletedTask; }));
        body.Add(Card(settings));
    }

    private View PermissionWarning(string key)
    {
        var label = new Label { Text = "⚠ " + L(key), FontSize = 15, TextColor = Color.FromArgb("614900") };
        return new Border { Content = label, Padding = 14, StrokeThickness = 0,
            BackgroundColor = Color.FromArgb("FFF1B8"), StrokeShape = new RoundRectangle { CornerRadius = 16 } };
    }

    private async Task CheckUpdateAsync(bool manual = false)
    {
        if (!manual && !Preferences.Default.Get(AutoUpdateCheckPreference, true)) return;
        if (checkingUpdate || (!manual && DateTimeOffset.UtcNow - lastUpdateCheck < TimeSpan.FromMinutes(15))) return;
        checkingUpdate = true;
        try
        {
            var address = server ?? ServerAddress.Parse(ConfiguredServerAddress());
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
                { BaseAddress = address, Timeout = TimeSpan.FromSeconds(10) };
            var release = await new AndroidUpdateClient(client).CheckAsync();
            if (server is not null && server != address) return;
            lastUpdateCheck = DateTimeOffset.UtcNow;
            availableUpdate = release?.VersionCode > AppUpdateInstaller.InstalledVersion ? release : null;
            updateServer = address;
            RenderUpdateCard();
            if (availableUpdate is null && manual) { message.Text = L(release is null ? "AppUpdateCheckFailed" : "AppUpToDate"); message.IsVisible = true; }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or
            System.Text.Json.JsonException or InvalidDataException)
        {
            lastUpdateCheck = DateTimeOffset.UtcNow;
            if (manual) { message.Text = L("AppUpdateCheckFailed"); message.IsVisible = true; }
        }
        finally { checkingUpdate = false; }
    }

    private void RenderUpdateCard()
    {
        updateContent.Clear();
        updateCard.IsVisible = availableUpdate is not null;
        if (availableUpdate is not { } update) return;
        updateContent.Add(Text(Format("AppUpdateAvailable", update.VersionName), 20));
        updateContent.Add(Button("AppUpdateNow", InstallUpdateAsync));
    }

    private async Task InstallUpdateAsync()
    {
        if (availableUpdate is not { } release || updateServer is null) return;
        var progressLabel = Text(L("AppUpdateDownloading"), 14);
        updateContent.Add(progressLabel);
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
                { BaseAddress = updateServer, Timeout = TimeSpan.FromMinutes(5) };
            var path = System.IO.Path.Combine(FileSystem.CacheDirectory, "updates", "HamsterHub.apk");
            var progress = new Progress<double>(value => progressLabel.Text =
                Format("AppUpdateProgress", (int)(value * 100)));
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await new AndroidUpdateClient(client).DownloadAsync(release, path, progress, deadline.Token);
            if (AppUpdateInstaller.NeedsPermission &&
                !await DisplayAlertAsync(L("AppUpdateNow"), L("AppUpdatePermission"), L("AppUpdateContinue"), L("AppUpdateLater"))) return;
            AppUpdateInstaller.Start(path, release);
            progressLabel.Text = L("AppUpdateInstallReady");
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or
            IOException or Android.Content.ActivityNotFoundException or Java.Lang.SecurityException)
        { progressLabel.Text = L("AppUpdateFailed"); }
    }

    private Entry Field(string key, bool password = false)
    {
        var required = key is not ("NewCategory" or "MobileServer" or "NewPassword");
        var entry = new Entry { Placeholder = L(key) + (required ? " *" : ""), IsPassword = password, MinimumHeightRequest = 52 };
        entry.SetAppThemeColor(Entry.TextColorProperty, Ink, Color.FromArgb("E8F5F1"));
        entry.SetAppThemeColor(Entry.PlaceholderColorProperty, InkSoft, Color.FromArgb("A9C2BC"));
        SemanticProperties.SetDescription(entry, L(key));
        var error = new Label { IsVisible = false, FontSize = 13 };
        error.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("AD2424"), Color.FromArgb("FFB0A7"));
        formInputs[entry] = (key, error);
        entry.TextChanged += (_, _) =>
        {
            if (!error.IsVisible) return;
            var reason = InputError(key, entry.Text);
            error.Text = reason is null ? "" : L(reason);
            error.IsVisible = reason is not null;
        };
        return entry;
    }

    private void AddInput(VerticalStackLayout form, Entry input)
    {
        if (input.IsPassword) { AddPasswordInput(form, input); return; }
        var key = formInputs[input].Key;
        var label = Text(L(key) + (key is "NewCategory" or "MobileServer" or "NewPassword" ? "" : " *"), 14);
        label.SetBinding(IsVisibleProperty, new Binding(nameof(IsVisible), source: input));
        form.Add(label);
        form.Add(input);
        if (formInputs.TryGetValue(input, out var details)) form.Add(details.Error);
    }

    private static string? InputError(string key, string? text)
    {
        var value = text?.Trim() ?? "";
        if (value.Length == 0) return key is "NewCategory" or "MobileServer" or "NewPassword" ? null : "FieldRequired";
        return key switch
        {
            "Email" when !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(value) => "InvalidEmail",
            "TemporaryPassword" or "NewPassword" when text!.Length < 4 => "PasswordLength",
            "DisplayName" or "TaskName" or "RewardName" when value.Length is < 2 or > 100 => "NameLength",
            "PetName" when value.Length > 100 => "NameLength",
            "Species" when value.Length is < 2 or > 100 => "MobileSpeciesLength",
            "NewCategory" when value.Length is < 2 or > 100 => "MobileCategoryNameLength",
            "PointValue" when !int.TryParse(value, out var points) || points is < 0 or > 1000 => "PointRange",
            "RewardPointCost" when !int.TryParse(value, out var cost) || cost is < 1 or > 100000 => "RewardPointRange",
            _ => null
        };
    }

    private bool ValidateInputs()
    {
        Entry? first = null;
        message.Text = ""; message.IsVisible = false;
        foreach (var (input, details) in formInputs)
        {
            if (details.Error.Parent is null || !input.IsVisible) continue;
            var reason = InputError(details.Key, input.Text);
            details.Error.Text = reason is null ? "" : L(reason);
            details.Error.IsVisible = reason is not null;
            if (reason is not null) first ??= input;
        }
        var additionalErrors = false;
        foreach (var (picker, details) in formPickers)
        {
            if (details.Error.Parent is null) continue;
            details.Error.IsVisible = details.Required && picker.SelectedItem is null;
            details.Error.Text = details.Error.IsVisible ? L("FieldRequired") : "";
            additionalErrors |= details.Error.IsVisible;
        }
        foreach (var group in formGroups)
        {
            var reason = group.Validate();
            group.Error.Text = reason is null ? "" : L(reason);
            group.Error.IsVisible = reason is not null;
            additionalErrors |= group.Error.IsVisible;
        }
        first?.Focus();
        return first is null && !additionalErrors;
    }

    private bool ShowInputError(string code)
    {
        if (code is "InvalidTaskPhoto" or "InvalidMemberImage" or "MemberImageTooLarge" or
            "InvalidPetImage" or "PetImageTooLarge" or "InvalidTaskImage" or "TaskImageTooLarge" or
            "InvalidRewardImage" or "RewardImageTooLarge")
        {
            if (formPhotoErrors.Values.FirstOrDefault() is { } photoError)
            { photoError.Text = L(code); photoError.IsVisible = true; return true; }
        }
        if (code == "ChooseRewardAudience" && formGroups.Count > 0)
        {
            formGroups[0].Error.Text = L(code); formGroups[0].Error.IsVisible = true; return true;
        }
        if (code == "ChooseMember")
        {
            foreach (var details in formPickers.Values.Where(item => item.Title == L("ChooseMember")))
            { details.Error.Text = L(code); details.Error.IsVisible = true; }
            return formPickers.Values.Any(item => item.Title == L("ChooseMember"));
        }
        var key = code switch
        {
            "InvalidEmail" or "EmailInUse" => "Email",
            "PasswordLength" or "MobilePasswordLength" or "PasswordRequirements" => formInputs.Values.Any(item => item.Key == "NewPassword") ? "NewPassword" : "TemporaryPassword",
            "MobileCategoryNameLength" => "NewCategory",
            "PointRange" => "PointValue",
            "RewardPointRange" => "RewardPointCost",
            "MobileSpeciesLength" => "Species",
            _ => null
        };
        var candidates = formInputs.Where(pair => pair.Value.Error.Parent is not null &&
            (pair.Value.Key == key || (code == "NameLength" && pair.Value.Key is "DisplayName" or "TaskName" or "PetName" or "RewardName"))).ToList();
        if (candidates.Count == 0) return false;
        foreach (var pair in candidates)
        { pair.Value.Error.Text = L(code); pair.Value.Error.IsVisible = true; }
        candidates[0].Key.Focus();
        return true;
    }

    private void ShowLogin(SavedAccount? selectedAccount = null)
    {
        formInputs.Clear(); formPickers.Clear(); formGroups.Clear(); formPhotoErrors.Clear(); showingSettings = false;
        ResetScroll();
        body.Clear();
        showingForm = false;
        showingCompletion = false;
        session = null;
        member = null;
        dashboard = null;
        household = null;
        lastUpdatedAt = null;
        selectedPhotos.Clear();
        AddSettings();
        if (selectedAccount is null) AddSavedAccountCards();
        var form = new VerticalStackLayout { Spacing = 14, MaximumWidthRequest = 480 };
        form.Add(Text(L(selectedAccount is null && SavedAccounts.Items.Count > 0 ? "MobileAnotherAccount" : "LoginTitle"), 26));
        if (selectedAccount is null && SavedAccounts.Items.Count == 0) form.Add(Text(L("MobileLoginIntro")));
        var address = Field("MobileServer");
        address.Keyboard = Keyboard.Url;
        address.Text = selectedAccount?.Server ?? ConfiguredServerAddress();
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
        AddInput(form, address);
        var email = Field("Email"); email.Keyboard = Keyboard.Email;
        email.Text = selectedAccount?.Email;
        var password = Field("Password", true);
        var code = Field("MobileTwoFactor"); code.Keyboard = Keyboard.Numeric;
        code.IsVisible = false;
        foreach (var field in new[] { email, password, code }) AddInput(form, field);
        var remember = AddToggle(form, "MobileRememberAccount", true);
        var direct = AddToggle(form, "MobileDirectSignIn", selectedAccount is null || selectedAccount.Tokens is not null);
        remember.Toggled += (_, args) => { direct.IsEnabled = args.Value; if (!args.Value) direct.IsToggled = false; };
        Button? login = null;
        login = Button("LoginSubmit", async () =>
        {
            var selectedAddress = address.IsVisible || selectedAccount is not null ? address.Text ?? "" : ConfiguredServerAddress();
            var selectedServer = ServerAddress.Parse(selectedAddress);
            if (address.IsVisible || selectedAccount is not null) SaveServerOverride(selectedServer.AbsoluteUri);
            Preferences.Default.Remove("active-saved-account");
            TaskNotifications.Clear();
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
            await SaveSignedInAccountAsync(email.Text ?? "", remember.IsToggled, direct.IsToggled);
            await CheckUpdateAsync();
        });
        form.Add(login);
        form.Add(SecondaryButton("MobileCreateFamily", async () =>
        {
            var selectedAddress = address.IsVisible ? address.Text ?? "" : ConfiguredServerAddress();
            var selectedServer = ServerAddress.Parse(selectedAddress);
            await Launcher.Default.OpenAsync(new Uri(selectedServer, "?dialog=signup"));
        }));
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

    private Task RefreshAsync() => RefreshAsync(preserveScroll: false);

    private async Task RefreshAsync(bool preserveScroll)
    {
        var position = scroll.ScrollY;
        var dashboardTask = api!.GetDashboardAsync(member!.Id);
        var householdTask = api.GetHouseholdAsync(member.Id);
        await Task.WhenAll(dashboardTask, householdTask);
        dashboard = await dashboardTask;
        household = await householdTask;
        lastUpdatedAt = DateTimeOffset.Now;
        try { await TaskNotifications.ConfigureAsync(server!, member!, api!); }
        catch (HttpRequestException) { message.Text = L("NotificationSyncFailed"); }
        catch (TaskCanceledException) { message.Text = L("NotificationSyncFailed"); }
        ShowDashboard(resetScroll: !preserveScroll);
        if (preserveScroll) await scroll.ScrollToAsync(0, position, false);
    }

    private void ShowDashboard(bool resetScroll = true)
    {
        formInputs.Clear(); formPickers.Clear(); formGroups.Clear(); formPhotoErrors.Clear(); showingSettings = false;
        if (resetScroll) ResetScroll();
        showingCompletion = false;
        showingForm = false;
        body.Clear();
        selectedPhotos.Clear();
        AddSettings();
        if (member is not null)
        {
            if (lastUpdatedAt is { } refreshed)
                body.Add(Text(Format("MobileUpdatedAt", refreshed.LocalDateTime.ToString("t",
                    System.Globalization.CultureInfo.GetCultureInfo(Strings.Culture))), 13));
        }
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
        if (household is not null) columns.Add(ChildHouseholdSection());
        ArrangeColumns();
        body.Add(columns);
        body.Add(RoleHistorySection("RecentCare", true));
    }

    private void ShowParentRoleDashboard()
    {
        var pendingTotal = dashboard!.PendingApprovals.Count + (household?.RewardRequests.Count ?? 0);
        body.Add(RoleHero(true));
        body.Add(RoleStats(
            ("✓", dashboard.Tasks.Count(item => item.CanComplete).ToString(), L("CareTasks"), Mint),
            ("!", pendingTotal.ToString(), L("AwaitingApproval"), Color.FromArgb("FFF0C8")),
            ("↻", dashboard.History.Count.ToString(), L("RecentCare"), Peach)));

        var attention = new VerticalStackLayout { Spacing = 14 };
        attention.Add(RoleSectionHeading(dashboard.PendingApprovals.Count == 0 ? "✓" : "!",
            L("MobileApprovals"), L(dashboard.PendingApprovals.Count == 0 ? "MobileNoCareApprovals" : "AttentionHint")));
        foreach (var log in dashboard.PendingApprovals) attention.Add(RoleLogCard(log, true, false));
        body.Add(Card(attention, dashboard.PendingApprovals.Count == 0 ? Mint : Color.FromArgb("FFF7D8"),
            dashboard.PendingApprovals.Count == 0 ? Color.FromArgb("25443E") : Color.FromArgb("3C3525"), 26, 1));

        if (household is not null) body.Add(ParentRewardRequests());

        if (household is not null) body.Add(ParentManagementSection());
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
        var scoreValue = Text((parent ? dashboard!.PendingApprovals.Count + (household?.RewardRequests.Count ?? 0) : dashboard!.Balance).ToString(), 34);
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
        if (wide) { hero.Add(copy); hero.Add(scoreBadge, 1); }
        else
        {
            hero.ColumnDefinitions.Clear(); hero.ColumnDefinitions.Add(new(GridLength.Star));
            hero.RowDefinitions.Add(new(GridLength.Auto)); hero.RowDefinitions.Add(new(GridLength.Auto));
            hero.RowSpacing = 12; hero.Add(copy); hero.Add(scoreBadge, 0, 1);
            scoreBadge.HorizontalOptions = LayoutOptions.Start;
        }
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
        var ordered = dashboard.Tasks.OrderByDescending(task => task.CanComplete).ToList();
        for (var index = 0; index < ordered.Count; index++)
            tasks.Add(RoleTaskCard(ordered[index], child, index));
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
        if (!child)
        {
            var editTap = new TapGestureRecognizer();
            editTap.Tapped += (_, _) =>
            {
                if (!busy && household?.Tasks.FirstOrDefault(item => item.Id == task.Id) is { } managed)
                    ShowEditTask(managed);
            };
            image.GestureRecognizers.Add(editTap);
            var titleTap = new TapGestureRecognizer();
            titleTap.Tapped += (_, _) =>
            {
                if (!busy && household?.Tasks.FirstOrDefault(item => item.Id == task.Id) is { } managed)
                    ShowEditTask(managed);
            };
            titleRow.GestureRecognizers.Add(titleTap);
            content.Add(Text(L("MobileTapToEdit"), 13));
        }
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
        var points = Text(log.Status switch
        {
            "Approved" => "★ +" + log.Points,
            "Pending" => Format("MobilePotentialPoints", log.Points),
            _ => L("MobileNoPointsAwarded")
        }, 16);
        points.FontAttributes = FontAttributes.Bold;
        points.SetAppThemeColor(Label.TextColorProperty, MintDeep, Color.FromArgb("72C8B8"));
        statusRow.Add(points, 1);
        content.Add(statusRow);
        content.Add(Text(log.CompletedAt.ToLocalTime().ToString("g",
            System.Globalization.CultureInfo.GetCultureInfo(Strings.Culture)), 14));
        var gallery = log.Photos.Select(PhotoSource).ToList();
        for (var index = 0; index < gallery.Count; index++)
            content.Add(TappablePhoto(gallery[index], gallery, index));
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
        var actions = new Grid { ColumnSpacing = 12, RowSpacing = 12,
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) },
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto) } };
        var addActions = new (string Icon, string Key, string Hint, Action Open)[]
        {
            ("👨‍👩‍👧", "AddFamilyMember", "MobileCreateMemberHint", ShowAddMember),
            ("🐹", "AddPet", "MobileCreatePetHint", ShowAddPet),
            ("📋", "AddCareTask", "MobileCreateTaskHint", ShowAddTask),
            ("🎁", "AddReward", "MobileCreateRewardHint", ShowAddReward)
        };
        for (var index = 0; index < addActions.Length; index++)
        {
            var action = addActions[index];
            actions.Add(CreateTile(action.Icon, action.Key, action.Hint, action.Open, index % 2 == 1), index % 2, index / 2);
        }
        section.Add(actions);
        section.Add(Text(L("MobileTapToEdit"), 14));
        section.Add(ManagementHeading("FamilyMembers"));
        foreach (var item in household!.Members)
            section.Add(EntityRow(item.DisplayName, $"{L("Role_" + item.Role)} · {item.Email}", "👤", item.PhotoPath,
                () => { ShowEditMember(item); return Task.CompletedTask; }));
        section.Add(ManagementHeading("Pets"));
        foreach (var item in household.Pets)
            section.Add(EntityRow(item.Name, item.Species, "🐹", item.PhotoPath,
                () => { ShowEditPet(item); return Task.CompletedTask; }));
        section.Add(ManagementHeading("CareTasks"));
        foreach (var item in household.Tasks)
            section.Add(EntityRow(item.Name, $"{item.PetName} · {item.AssignedMemberName} · ★ {item.Points}", "📋", item.ImagePath,
                () => { ShowEditTask(item); return Task.CompletedTask; }));
        section.Add(ManagementHeading("Rewards"));
        foreach (var item in household.Rewards)
            section.Add(EntityRow(item.Name, Format("RewardCostCount", item.PointCost), "🎁", item.ImagePath,
                () => { ShowEditReward(item); return Task.CompletedTask; }));
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
            var request = Button(reward.HasPendingRequest ? "RewardWaiting" :
                reward.CanAfford ? "RequestReward" : "NeedMorePoints", async () =>
            { await api!.RequestRewardAsync(member!.Id, reward.Id); await RefreshAsync(); });
            if (!reward.CanAfford && !reward.HasPendingRequest)
            {
                request.Text = Format("StarsToReward", reward.PointCost - dashboard!.Balance);
                SemanticProperties.SetDescription(request, request.Text);
            }
            request.IsEnabled = reward.CanAfford && !reward.HasPendingRequest;
            if (string.IsNullOrWhiteSpace(reward.ImagePath))
                card.Insert(0, new Label { Text = "🎁", FontSize = 56, HorizontalTextAlignment = TextAlignment.Center });
            card.Add(request);
            section.Add(Card(card, Color.FromArgb("FFF0C8"), Color.FromArgb("3C3525"), 28, 2));
        }

        section.Add(RoleSectionHeading("👪", L("FamilyMembers")));
        foreach (var item in household.Members)
            section.Add(EntityRow(item.DisplayName, $"{L("Role_" + item.Role)} · ★ {item.Balance}",
                "👤", item.PhotoPath, () => ShowMemberProfileAsync(item), true));
        section.Add(RoleSectionHeading("🐾", L("FamilyPets")));
        foreach (var item in household.Pets)
            section.Add(EntityRow(item.Name, item.Species, "🐹", item.PhotoPath, () => ShowPetProfileAsync(item), true));
        if (household.RewardHistory.Count > 0)
        {
            section.Add(RoleSectionHeading("↻", L("PointHistory")));
            foreach (var item in household.RewardHistory)
                section.Add(ManagementRow(item.RewardName,
                    item.Status == "Approved"
                        ? $"{L("RewardStatus_" + item.Status)} · −{item.PointsCost} ★"
                        : $"{L("RewardStatus_" + item.Status)} · {Format("MobilePotentialCost", item.PointsCost)}"));
        }
        return Card(section, Color.FromArgb("FFFDF7"), Color.FromArgb("17312D"), 30, 2);
    }

    private Label ManagementHeading(string key)
    {
        var heading = Text(L(key), 22); heading.FontAttributes = FontAttributes.Bold; return heading;
    }

    private View ManagementRow(string titleText, string detail)
    {
        var copy = new VerticalStackLayout { Spacing = 4 };
        var title = Text(titleText, 19); title.FontAttributes = FontAttributes.Bold; copy.Add(title);
        copy.Add(Text(detail, 14));
        return Card(copy, Mint, Color.FromArgb("25443E"), 18, 1);
    }

    private async Task<bool> ConfirmDelete(string name) =>
        await DisplayAlertAsync(L("Delete"), Format("MobileDeleteConfirm", name), L("Delete"), L("Cancel"));

    private Picker PickerFor<T>(string title, IReadOnlyList<T> items, string displayProperty)
    {
        var required = title != L("ChoosePet") && title != L("ChooseCategory");
        var picker = new Picker { Title = title + (required ? " *" : ""), ItemsSource = items.ToList(),
            ItemDisplayBinding = new Binding(displayProperty) };
        var error = new Label { IsVisible = false, FontSize = 13 };
        error.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("AD2424"), Color.FromArgb("FFB0A7"));
        formPickers[picker] = (title, required, error);
        picker.SelectedIndexChanged += (_, _) => { if (picker.SelectedItem is not null) error.IsVisible = false; };
        picker.SetAppThemeColor(Picker.TextColorProperty, Ink, Color.FromArgb("E8F5F1"));
        if (items.Count > 0) picker.SelectedIndex = 0;
        return picker;
    }

    private void AddPicker(VerticalStackLayout form, Picker picker)
    {
        form.Add(picker);
        if (formPickers.TryGetValue(picker, out var details)) form.Add(details.Error);
    }

    private Picker ChoicePicker(string title, IReadOnlyList<Choice> choices, string selectedValue)
    {
        var picker = PickerFor(title, choices, nameof(Choice.Label));
        picker.SelectedIndex = Math.Max(0, choices.ToList().FindIndex(choice => choice.Value == selectedValue));
        return picker;
    }

    private void ShowForm(string titleKey, Action<VerticalStackLayout> build)
    {
        formInputs.Clear(); formPickers.Clear(); formGroups.Clear(); formPhotoErrors.Clear(); showingSettings = false;
        ResetScroll(); body.Clear(); selectedPhotos.Clear(); showingForm = true;
        var form = new VerticalStackLayout { Spacing = 14, MaximumWidthRequest = 620 };
        var title = Text(L(titleKey), 28); title.FontAttributes = FontAttributes.Bold; form.Add(title);
        form.Add(SecondaryButton("MobileBack", () => { ShowDashboard(); return Task.CompletedTask; }));
        build(form);
        form.Add(SecondaryButton("MobileBack", () => { ShowDashboard(); return Task.CompletedTask; }));
        body.Add(Card(form, Paper, Color.FromArgb("17312D"), 28, 1));
    }

    private static int ParsePoints(Entry field, int minimum, int maximum, string errorKey)
    {
        if (int.TryParse(field.Text, out var value) && value >= minimum && value <= maximum)
            return value;
        field.Focus();
        throw new MobileApiException(errorKey);
    }

    private static string? OptionalCategory(Entry field)
    {
        var value = field.Text?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        if (value.Length is < 2 or > 100)
        {
            field.Focus();
            throw new MobileApiException("MobileCategoryNameLength");
        }
        return value;
    }

    private Func<DateOnly?> AddBirthDatePicker(VerticalStackLayout form, DateOnly? initial)
    {
        var known = new CheckBox { IsChecked = initial.HasValue };
        var row = new HorizontalStackLayout { Spacing = 10,
            Children = { known, Text(L("MobileBirthDateKnown"), 16) } };
        var date = new DatePicker
        {
            Date = initial?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today,
            MaximumDate = DateTime.Today,
            IsVisible = initial.HasValue
        };
        SemanticProperties.SetDescription(date, L("BirthDate"));
        known.CheckedChanged += (_, args) => date.IsVisible = args.Value;
        form.Add(row);
        form.Add(date);
        return () => known.IsChecked && date.Date is DateTime selected
            ? DateOnly.FromDateTime(selected) : null;
    }

    private void ShowAddMember() => ShowForm("AddFamilyMember", form =>
    {
        var name = Field("DisplayName"); var email = Field("Email"); email.Keyboard = Keyboard.Email;
        var password = Field("TemporaryPassword", true);
        FileResult? photo = null;
        int? createdId = null;
        var roles = new[] { new Choice("Child", L("Role_Child")),
            new Choice("Parent", L("Role_Parent")) };
        var role = ChoicePicker(L("FamilyRole"), roles, "Child");
        AddInput(form, name); AddInput(form, email); AddInput(form, password); AddPicker(form, role);
        AddPhotoPicker(form, "MemberPhoto", file => photo = file);
        form.Add(Button("AddFamilyMember", async () =>
        {
            if (createdId is null)
            {
                if (string.IsNullOrWhiteSpace(name.Text) || name.Text.Trim().Length is < 2 or > 100)
                    throw new MobileApiException("NameLength");
                if (string.IsNullOrWhiteSpace(email.Text) ||
                    !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email.Text.Trim()))
                    throw new MobileApiException("InvalidEmail");
                if (password.Text is null || password.Text.Length < 4)
                    throw new MobileApiException("MobilePasswordLength");
            }
            createdId ??= await api!.AddMemberAsync(member!.Id, new CreateMemberRequest(name.Text ?? "", email.Text ?? "",
                password.Text ?? "", (role.SelectedItem as Choice)?.Value ?? "Child"));
            if (photo is not null)
            {
                await UploadManagementPhotoAsync("members", createdId.Value, photo);
                photo = null;
            }
            message.Text = Format("FamilyMemberAdded", name.Text ?? ""); await RefreshAsync();
        }));
    });

    private void ShowAddPet() => ShowForm("AddPet", form =>
    {
        var name = Field("PetName"); var species = Field("Species");
        FileResult? photo = null;
        int? createdId = null;
        AddInput(form, name); AddInput(form, species);
        var birthDate = AddBirthDatePicker(form, null);
        AddPhotoPicker(form, "PetPhoto", file => photo = file);
        form.Add(Button("SavePet", async () =>
        {
            createdId ??= await api!.AddPetAsync(member!.Id, new CreatePetRequest(name.Text ?? "", species.Text ?? "", birthDate()));
            if (photo is not null)
            {
                await UploadManagementPhotoAsync("pets", createdId.Value, photo);
                photo = null;
            }
            message.Text = Format("PetAdded", name.Text ?? ""); await RefreshAsync();
        }));
    });

    private Func<TaskReminderDto?> AddTaskReminderPicker(VerticalStackLayout form, Picker frequency,
        TaskReminderDto? initial)
    {
        var enabled = new Switch { IsToggled = initial is not null };
        SemanticProperties.SetDescription(enabled, L("TaskNotification"));
        form.Add(new HorizontalStackLayout { Spacing = 10,
            Children = { enabled, Text(L("TaskNotification"), 16) } });
        var zone = initial?.TimeZoneId ?? TimeZoneInfo.Local.Id;
        var options = new VerticalStackLayout { Spacing = 8 };
        var time = new TimePicker { Time = initial?.Time.ToTimeSpan() ?? TimeSpan.FromHours(18) };
        var date = new DatePicker { Date = initial?.StartDate.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today };
        SemanticProperties.SetDescription(time, L("NotificationTime"));
        SemanticProperties.SetDescription(date, L("NotificationStartDate"));
        options.Add(Text(L("NotificationTime"), 15)); options.Add(time);
        options.Add(Text(L("NotificationStartDate"), 15)); options.Add(date);
        options.Add(Text(L("NotificationScheduleHint") + " " + zone, 14));
        var hint = Text(L("NotificationUnlimitedHint"), 14);
        form.Add(hint); form.Add(options);
        void Update()
        {
            var unlimited = (frequency.SelectedItem as Choice)?.Value == "AsNeeded";
            enabled.IsEnabled = !unlimited;
            if (unlimited) enabled.IsToggled = false;
            options.IsVisible = enabled.IsToggled;
            hint.IsVisible = unlimited;
        }
        enabled.Toggled += (_, _) => Update();
        frequency.SelectedIndexChanged += (_, _) => Update();
        Update();
        return () => enabled.IsToggled && time.Time is TimeSpan chosenTime && date.Date is DateTime chosenDate
            ? new TaskReminderDto(TimeOnly.FromTimeSpan(chosenTime), DateOnly.FromDateTime(chosenDate), zone) : null;
    }

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
        int? createdId = null;
        var frequencies = new[] { new Choice("Daily", L("Frequency_Daily")),
            new Choice("Weekly", L("Frequency_Weekly")),
            new Choice("Once", L("Frequency_Once")),
            new Choice("AsNeeded", L("Frequency_AsNeeded")) };
        var frequency = ChoicePicker(L("TaskFrequency"), frequencies, "Daily");
        var points = Field("PointValue"); points.Text = "5"; points.Keyboard = Keyboard.Numeric;
        AddInput(form, name); AddPicker(form, pet); AddPicker(form, assignee); AddPicker(form, category); AddInput(form, custom); AddPicker(form, frequency); AddInput(form, points);
        var reminder = AddTaskReminderPicker(form, frequency, null);
        AddPhotoPicker(form, "TaskImage", file => photo = file);
        form.Add(Button("SaveCareTask", async () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) throw new MobileApiException("TaskNameRequired");
            if (name.Text.Trim().Length is < 2 or > 100) throw new MobileApiException("NameLength");
            var selectedPet = pet.SelectedItem as PetItemDto;
            if (assignee.SelectedItem is not HouseholdMemberItemDto selectedMember) throw new MobileApiException("ChooseMember");
            var pointValue = ParsePoints(points, 0, 1000, "PointRange");
            var categoryId = category.SelectedItem is CategoryItemDto { Id: > 0 } selectedCategory ? selectedCategory.Id : (int?)null;
            createdId ??= await api!.AddTaskAsync(member!.Id, new CreateTaskRequest(selectedPet is { Id: > 0 } ? selectedPet.Id : null, selectedMember.Id,
                categoryId, OptionalCategory(custom), (frequency.SelectedItem as Choice)?.Value ?? "Daily", pointValue, name.Text.Trim(), reminder()));
            if (photo is not null)
            {
                await UploadManagementPhotoAsync("tasks", createdId.Value, photo);
                photo = null;
            }
            await RefreshAsync();
        }));
    });

    private void ShowAddReward() => ShowForm("AddReward", form =>
    {
        var name = Field("RewardName"); var cost = Field("RewardPointCost"); cost.Text = "10";
        FileResult? photo = null;
        int? createdId = null;
        cost.Keyboard = Keyboard.Numeric; AddInput(form, name); AddInput(form, cost);
        AddPhotoPicker(form, "RewardImage", file => photo = file);
        form.Add(Text(L("RewardAudience") + " *", 16));
        var choices = new List<(int Id, CheckBox Box)>();
        foreach (var child in household!.Members.Where(item => item.Role == "Child"))
        {
            var box = new CheckBox(); choices.Add((child.Id, box));
            var row = new HorizontalStackLayout { Spacing = 10, Children = { box, Text(child.DisplayName, 17) } };
            form.Add(row);
        }
        var audienceError = new Label { IsVisible = false, FontSize = 13 };
        audienceError.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("AD2424"), Color.FromArgb("FFB0A7"));
        form.Add(audienceError);
        formGroups.Add((() => choices.Any(item => item.Box.IsChecked) ? null : "ChooseRewardAudience", audienceError));
        form.Add(Button("SaveReward", async () =>
        {
            var pointCost = ParsePoints(cost, 1, 100000, "RewardPointRange");
            createdId ??= await api!.AddRewardAsync(member!.Id, new CreateRewardRequest(name.Text ?? "", pointCost,
                choices.Where(item => item.Box.IsChecked).Select(item => item.Id).ToList()));
            if (photo is not null)
            {
                await UploadManagementPhotoAsync("rewards", createdId.Value, photo);
                photo = null;
            }
            await RefreshAsync();
        }));
    });

    private void ShowEditMember(HouseholdMemberItemDto item) => ShowForm("EditFamilyMember", form =>
    {
        var name = Field("DisplayName"); name.Text = item.DisplayName;
        var email = Field("Email"); email.Text = item.Email; email.Keyboard = Keyboard.Email;
        var password = Field("NewPassword", true);
        FileResult? photo = null;
        var roles = new[] { new Choice("Child", L("Role_Child")), new Choice("Parent", L("Role_Parent")) };
        var role = ChoicePicker(L("FamilyRole"), roles, item.Role); role.IsEnabled = !item.IsCurrentUser;
        AddInput(form, name); AddInput(form, email); AddInput(form, password);
        form.Add(Text(L("MobileKeepPassword"), 13)); AddPicker(form, role);
        var privacy = new VerticalStackLayout { Spacing = 10, IsVisible = item.Role == "Child" };
        privacy.Add(Text(L("HistoryPrivacy"), 18));
        var canView = AddToggle(privacy, "MobileViewFamilyHistory", item.CanViewOtherChildrenHistory);
        var share = AddToggle(privacy, "MobileShareHistory", item.ShareHistoryWithChildren);
        role.SelectedIndexChanged += (_, _) => privacy.IsVisible = (role.SelectedItem as Choice)?.Value == "Child";
        form.Add(privacy);
        AddPhotoPicker(form, "MemberPhoto", file => photo = file, item.PhotoPath,
            () => api!.UpdateMediaAsync(member!.Id, "members", item.Id, null, true));
        form.Add(SecondaryButton("MobileViewHistory", () => ShowMemberProfileAsync(item)));
        form.Add(Button("SaveChanges", async () =>
        {
            // Upload before a self password reset invalidates the current bearer session.
            if (photo is not null) { await UploadManagementPhotoAsync("members", item.Id, photo); photo = null; }
            await api!.UpdateMemberAsync(member!.Id, item.Id, new UpdateMemberRequest(name.Text ?? "",
                (role.SelectedItem as Choice)?.Value ?? item.Role, email.Text ?? "",
                string.IsNullOrEmpty(password.Text) ? null : password.Text, canView.IsToggled, share.IsToggled));
            foreach (var saved in SavedAccounts.Items.Where(account => account.Server == server!.AbsoluteUri &&
                string.Equals(account.Email, item.Email, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                var id = SavedAccounts.IdFor(server!, email.Text ?? item.Email);
                await SavedAccounts.RemoveAsync(saved.Id);
                await SavedAccounts.SaveAsync(saved with { Id = id, Email = email.Text ?? item.Email,
                    Name = name.Text ?? item.DisplayName,
                    Tokens = !string.IsNullOrEmpty(password.Text) || (role.SelectedItem as Choice)?.Value != item.Role ? null : saved.Tokens });
                if (item.IsCurrentUser) Preferences.Default.Set("active-saved-account", id);
            }
            if (item.IsCurrentUser && !string.IsNullOrEmpty(password.Text))
            {
                TaskNotifications.Clear(); await api.LogoutAsync();
                ShowLogin(); message.Text = L("MobilePasswordChangedSignIn"); return;
            }
            await LoadSessionAsync();
        }));
        if (!item.IsCurrentUser) AddDeleteAction(form, item.DisplayName, () => api!.ArchiveMemberAsync(member!.Id, item.Id));
    });

    private void ShowEditPet(PetItemDto item) => ShowForm("EditPet", form =>
    {
        var name = Field("PetName"); name.Text = item.Name;
        var species = Field("Species"); species.Text = item.Species;
        FileResult? photo = null;
        AddInput(form, name); AddInput(form, species);
        var birthDate = AddBirthDatePicker(form, item.BirthDate);
        AddPhotoPicker(form, "PetPhoto", file => photo = file, item.PhotoPath,
            () => api!.UpdateMediaAsync(member!.Id, "pets", item.Id, null, true));
        form.Add(SecondaryButton("MobileViewHistory", () => ShowPetProfileAsync(item)));
        form.Add(Button("SaveChanges", async () =>
        {
            await api!.UpdatePetAsync(member!.Id, item.Id,
                new UpdatePetRequest(name.Text ?? "", species.Text ?? "", birthDate()));
            if (photo is not null) await UploadManagementPhotoAsync("pets", item.Id, photo);
            await RefreshAsync();
        }));
        AddDeleteAction(form, item.Name, () => api!.ArchivePetAsync(member!.Id, item.Id));
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
            new Choice("Once", L("Frequency_Once")),
            new Choice("AsNeeded", L("Frequency_AsNeeded")) };
        var frequency = ChoicePicker(L("TaskFrequency"), frequencies, item.Frequency);
        var points = Field("PointValue"); points.Text = item.Points.ToString(); points.Keyboard = Keyboard.Numeric;
        AddInput(form, name); AddPicker(form, pet); AddPicker(form, assignee); AddPicker(form, category); AddInput(form, custom); AddPicker(form, frequency); AddInput(form, points);
        var reminder = AddTaskReminderPicker(form, frequency, item.Reminder);
        AddPhotoPicker(form, "TaskImage", file => photo = file, item.ImagePath,
            () => api!.UpdateMediaAsync(member!.Id, "tasks", item.Id, null, true));
        form.Add(Button("SaveChanges", async () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) throw new MobileApiException("TaskNameRequired");
            var selectedPet = pet.SelectedItem as PetItemDto;
            if (assignee.SelectedItem is not HouseholdMemberItemDto selectedMember) return;
            var pointValue = ParsePoints(points, 0, 1000, "PointRange");
            var categoryId = category.SelectedItem is CategoryItemDto { Id: > 0 } selectedCategory ? selectedCategory.Id : (int?)null;
            await api!.UpdateTaskAsync(member!.Id, item.Id, new UpdateTaskRequest(selectedPet is { Id: > 0 } ? selectedPet.Id : null,
                selectedMember.Id, categoryId, OptionalCategory(custom),
                (frequency.SelectedItem as Choice)?.Value ?? item.Frequency, pointValue, name.Text.Trim(), reminder()));
            if (photo is not null) await UploadManagementPhotoAsync("tasks", item.Id, photo);
            await RefreshAsync();
        }));
        AddDeleteAction(form, item.Name, () => api!.ArchiveTaskAsync(member!.Id, item.Id));
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

    private void AddPhotoPicker(VerticalStackLayout form, string key, Action<FileResult?> setPhoto,
        string? existing = null, Func<Task>? removeExisting = null)
    {
        var error = new Label { IsVisible = false, FontSize = 13 };
        error.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("AD2424"), Color.FromArgb("FFB0A7"));
        formPhotoErrors[key] = error;
        var preview = new Image { IsVisible = !string.IsNullOrWhiteSpace(existing), HeightRequest = 150,
            Aspect = Aspect.AspectFit, Source = existing is null ? null : PhotoSource(existing) };
        var previewTap = new TapGestureRecognizer();
        previewTap.Tapped += async (_, _) =>
        {
            if (preview.Source is null || photoViewerOpen || busy) return;
            photoViewerOpen = true;
            var viewer = new PhotoViewerPage(new[] { preview.Source }, 0);
            viewer.Disappearing += (_, _) => photoViewerOpen = false;
            try { await Navigation.PushModalAsync(viewer); } catch { photoViewerOpen = false; }
        };
        preview.GestureRecognizers.Add(previewTap);
        var replacement = false;
        var remove = SecondaryButton("MobileRemovePhoto", async () =>
        {
            if (!replacement && removeExisting is not null) { await removeExisting(); existing = null; }
            setPhoto(null); replacement = false;
            preview.Source = existing is null ? null : PhotoSource(existing);
            preview.IsVisible = existing is not null; error.IsVisible = false;
        });
        remove.IsVisible = preview.IsVisible;
        form.Add(SecondaryButton(key, async () =>
        {
            var file = (await ChoosePhotosAsync(1)).FirstOrDefault();
            if (file is null) return;
            setPhoto(file); replacement = true;
            error.IsVisible = false;
            preview.Source = ImageSource.FromStream(_ => file.OpenReadAsync());
            preview.IsVisible = true; remove.IsVisible = true;
        }));
        preview.PropertyChanged += (_, args) =>
        { if (args.PropertyName == nameof(IsVisible)) remove.IsVisible = preview.IsVisible; };
        form.Add(error); form.Add(preview); form.Add(remove);
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
        body.Add(Text(L("MobileCompletionShort"), 16));
        body.Add(TappablePhoto(PhotoSource(task.ImagePath), height: 180));
        var previews = new VerticalStackLayout { Spacing = 10 };
        void RefreshPreviews()
        {
            previews.Clear();
            foreach (var file in selectedPhotos.ToList())
            {
                previews.Add(TappablePhoto(ImageSource.FromStream(_ => file.OpenReadAsync()), height: 170));
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
            TaskNotifications.SuppressTask(task.Id, task.Frequency);
            selectedPhotos.Clear();
            message.Text = L(member.Role == "Parent" ? "ParentTaskCompleted" : "TaskSentForApproval");
            await RefreshAsync();
        }));
        body.Add(SecondaryButton("MobileBack", () => { ShowDashboard(); return Task.CompletedTask; }));
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
        if (showingCompletion || showingForm)
        {
            if (session is null) ShowLogin(); else ShowDashboard();
            return true;
        }
        return base.OnBackButtonPressed();
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true; body.IsEnabled = false; activity.IsVisible = true; activity.IsRunning = true; message.Text = ""; message.IsVisible = false;
        try { await action(); }
        catch (MobileApiException exception)
        {
            if (exception.Code == "SessionExpired")
            {
                if (api is not null) await api.LogoutAsync();
                if (SavedAccounts.Items.FirstOrDefault(item => item.Id == Preferences.Default.Get("active-saved-account", "")) is { } saved)
                    await SavedAccounts.SaveAsync(saved with { Tokens = null });
                TaskNotifications.Clear();
                ShowLogin();
            }
            var codes = exception.Details.Count > 0 ? exception.Details : new[] { exception.Code };
            message.Text = string.Join("\n", codes.Where(code => !ShowInputError(code))
                .Select(code => L(code == "TooManyTaskPhotos" ? "MobilePhotoLimit" : code)).Distinct());
        }
        catch (PermissionException) { message.Text = L("MobileCameraPermission"); }
        catch (HttpRequestException) { message.Text = L("MobileConnectionError"); }
        catch (OperationCanceledException) { message.Text = L("MobileRequestTimeout"); }
        catch (Exception) { message.Text = L("MobileUnexpectedError"); }
        finally { busy = false; body.IsEnabled = true; activity.IsRunning = false; activity.IsVisible = false; message.IsVisible = !string.IsNullOrEmpty(message.Text);
            if (message.IsVisible) ResetScroll(); }
    }
}
