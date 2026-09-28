using HamsterHub.Client;
using HamsterHub.Contracts;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;

namespace HamsterHub.Mobile;

public sealed class MainPage : ContentPage
{
    private const string DefaultServerAddress = "http://95.165.103.141:5080/";
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
    private readonly List<FileResult> selectedPhotos = [];
    private bool initialized;
    private bool busy;
    private bool wide;
    private bool showingCompletion;
    private static string L(string key) => Strings.Get(key);

    public MainPage()
    {
        Title = "HamsterHub";
        this.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("FFF8ED"), Color.FromArgb("211E1B"));
        message.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("8B3636"), Color.FromArgb("FFB7A9"));
        SemanticProperties.SetDescription(message, L("MobileStatus"));
        var root = new VerticalStackLayout { Padding = new Thickness(20, 24), Spacing = 12,
            MaximumWidthRequest = 1200, HorizontalOptions = LayoutOptions.Fill };
        root.Add(new Label { Text = "HamsterHub", FontSize = 30, FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("C98046") });
        root.Add(activity);
        root.Add(message);
        root.Add(body);
        Content = new ScrollView { Content = root };
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
                var saved = Preferences.Default.Get("server", "");
                if (string.IsNullOrWhiteSpace(saved)) return;
                Connect(saved);
                if (await api!.RestoreAsync()) await LoadSessionAsync();
            });
        };
    }

    private void Connect(string address)
    {
        server = ServerAddress.Parse(address);
        http?.Dispose();
        http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { BaseAddress = server, Timeout = TimeSpan.FromSeconds(45) };
        api = new HamsterHubClient(http, new SecureSessionStore(server)) { Culture = Strings.Culture };
        Preferences.Default.Set("server", server.AbsoluteUri);
    }

    private Button Button(string key, Func<Task> action)
    {
        var button = new Button { Text = L(key), MinimumHeightRequest = 52, CornerRadius = 16,
            Margin = new Thickness(0, 0, 4, 4), BackgroundColor = Color.FromArgb("F2AB68"), TextColor = Color.FromArgb("38291F"),
            FontSize = 17, Padding = new Thickness(16, 10) };
        SemanticProperties.SetDescription(button, L(key));
        button.Clicked += async (_, _) => await RunAsync(action);
        return button;
    }

    private Label Text(string text, int size = 18)
    {
        var label = new Label { Text = text, FontSize = size };
        label.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("403329"), Color.FromArgb("F8ECDD"));
        return label;
    }

    private Border Card(View content)
    {
        var border = new Border { Content = content, Padding = 18, StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 22 } };
        border.SetAppThemeColor(BackgroundColorProperty, Colors.White, Color.FromArgb("332D27"));
        return border;
    }

    private void AddSettings()
    {
        var settings = new FlexLayout { Wrap = FlexWrap.Wrap };
        settings.Add(Button("SwitchLanguage", async () =>
        {
            Strings.Culture = Strings.Culture == "ru" ? "en" : "ru";
            Preferences.Default.Set("language", Strings.Culture);
            if (api is not null) api.Culture = Strings.Culture;
            if (session is null) ShowLogin(); else await LoadSessionAsync();
        }));
        settings.Add(Button("ToggleTheme", () =>
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
        entry.SetAppThemeColor(Entry.TextColorProperty, Color.FromArgb("403329"), Color.FromArgb("F8ECDD"));
        entry.SetAppThemeColor(Entry.PlaceholderColorProperty, Color.FromArgb("736353"), Color.FromArgb("C0AB95"));
        SemanticProperties.SetDescription(entry, L(key));
        return entry;
    }

    private void ShowLogin()
    {
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
        address.Text = Preferences.Default.Get("server", DefaultServerAddress);
        address.IsVisible = false;
        Button? advanced = null;
        advanced = Button("MobileAdvancedSettings", () =>
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
            Connect(address.Text ?? "");
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
        showingCompletion = false;
        body.Clear();
        selectedPhotos.Clear();
        AddSettings();
        var accountRow = new Grid { ColumnDefinitions = new ColumnDefinitionCollection
            { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 12 };
        var userName = Text(session!.DisplayName, 24);
        userName.VerticalOptions = LayoutOptions.Center;
        accountRow.Add(userName);
        accountRow.Add(Button("Logout", async () => { await api!.LogoutAsync(); ShowLogin(); }), 1);
        body.Add(accountRow);
        if (member is null) { body.Add(Text(L("MobileNoHousehold"))); return; }
        var picker = new Picker { Title = L("MobileHousehold"), ItemsSource = session.Memberships.ToList(),
            ItemDisplayBinding = new Binding(nameof(MemberDto.HouseholdName)), SelectedItem = member };
        picker.SetAppThemeColor(Picker.TextColorProperty, Color.FromArgb("403329"), Color.FromArgb("F8ECDD"));
        SemanticProperties.SetDescription(picker, L("MobileHousehold"));
        picker.SelectedIndexChanged += async (_, _) =>
        {
            if (picker.SelectedItem is MemberDto next && next.Id != member?.Id)
                await RunAsync(async () => { member = next; await RefreshAsync(); });
        };
        if (session.Memberships.Count > 1) body.Add(picker);
        else body.Add(Text(member.HouseholdName, 16));
        var balanceRow = new Grid { ColumnDefinitions = new ColumnDefinitionCollection
            { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 12 };
        var balance = Text($"★ {dashboard!.Balance} · {L("CarePoints")}", 22);
        balance.VerticalOptions = LayoutOptions.Center;
        balanceRow.Add(balance);
        balanceRow.Add(Button("MobileRefresh", RefreshAsync), 1);
        body.Add(balanceRow);
        columns.Children.Clear();
        var tasks = new VerticalStackLayout { Spacing = 16 };
        tasks.Add(Text(L("ChooseTask"), 24));
        if (dashboard.Tasks.Count == 0) tasks.Add(Text(L("MobileNoTasks")));
        foreach (var task in dashboard.Tasks)
        {
            var content = new VerticalStackLayout { Spacing = 10 };
            var image = new Image { Source = PhotoSource(task.ImagePath),
                HeightRequest = 150, Aspect = Aspect.AspectFit };
            SemanticProperties.SetDescription(image, task.Name + ", " + task.PetName);
            content.Add(image);
            content.Add(Text(task.PetName, 24));
            content.Add(Text($"{task.Name} · ★ {task.Points}"));
            var complete = Button(task.CanComplete ? "MarkComplete" : "MobileAlreadyRecorded",
                () => { ShowCompletion(task); return Task.CompletedTask; });
            complete.IsEnabled = task.CanComplete;
            content.Add(complete);
            tasks.Add(Card(content));
        }
        var history = new VerticalStackLayout { Spacing = 16 };
        if (member.Role == "Parent")
        {
            history.Add(Text(L("MobileApprovals"), 24));
            if (dashboard.PendingApprovals.Count == 0) history.Add(Text(L("MobileNoApprovals")));
            foreach (var log in dashboard.PendingApprovals) history.Add(LogCard(log, true));
        }
        history.Add(Text(L("MobileHistory"), 24));
        if (dashboard.History.Count == 0) history.Add(Text(L("MobileNoHistory")));
        foreach (var log in dashboard.History) history.Add(LogCard(log, false));
        columns.Add(tasks);
        columns.Add(history);
        ArrangeColumns();
        body.Add(columns);
    }

    private Border LogCard(CareLogDto log, bool review)
    {
        var content = new VerticalStackLayout { Spacing = 10 };
        content.Add(Text($"{log.PetName} · {log.TaskName}", 20));
        if (review) content.Add(Text(log.MemberName));
        content.Add(Text($"{L("MobileStatus" + log.Status)} · ★ {log.Points}"));
        content.Add(Text(log.CompletedAt.ToLocalTime().ToString("g",
            System.Globalization.CultureInfo.GetCultureInfo(Strings.Culture)), 14));
        foreach (var photo in log.Photos)
        {
            var image = new Image { Source = PhotoSource(photo),
                HeightRequest = 160, Aspect = Aspect.AspectFit };
            SemanticProperties.SetDescription(image, L("MobileCarePhoto"));
            content.Add(image);
        }
        if (review)
        {
            content.Add(Button("MobileApprove", async () =>
            { await api!.ReviewAsync(member!.Id, log.Id, true); await RefreshAsync(); }));
            content.Add(Button("MobileReject", async () =>
            { await api!.ReviewAsync(member!.Id, log.Id, false); await RefreshAsync(); }));
        }
        return Card(content);
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
