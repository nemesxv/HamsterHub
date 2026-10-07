using HamsterHub.Contracts;

namespace HamsterHub.Mobile;

public sealed partial class MainPage
{
    private bool photoViewerOpen;
    private static string ButtonIcon(string key) => key switch
    {
        "MobileSettings" => "⚙", "MobileBack" => "←", "Logout" => "↪",
        "DoneButton" or "MarkComplete" or "MobileSendCompletion" => "✓",
        "MobileAlreadyRecorded" or "RewardWaiting" => "⌛",
        "MobileAddPhoto" or "TaskImage" or "PetPhoto" or "MemberPhoto" or "RewardImage" => "📷",
        "MobileRemovePhoto" or "Delete" => "✕", "RequestReward" or "MobileGiveReward" => "🎁",
        "SaveChanges" or "SaveCareTask" or "SavePet" or "SaveReward" => "✓",
        "MobileApprove" or "Approve" => "✓", "MobileReject" or "Reject" => "✕",
        "LoginSubmit" or "SavedAccountSignIn" => "→", "AppCheckUpdates" => "↻",
        "AllowNotifications" => "🔔", "AllowExactAlarms" => "⏰",
        _ => ""
    };

    private View TappableCard(View content, string description, Func<Task> action,
        Color? light = null, Color? dark = null)
    {
        content.InputTransparent = true;
        var grid = new Grid(); grid.Add(content);
        var tap = new Button { Text = "", BackgroundColor = Colors.Transparent,
            BorderWidth = 0, MinimumHeightRequest = 64, Margin = 0, Padding = 0 };
        SemanticProperties.SetDescription(tap, description);
        tap.Clicked += async (_, _) => await RunAsync(action);
        grid.Add(tap);
        return Card(grid, light, dark, 22, 1);
    }

    private View CreateTile(string icon, string key, string hintKey, Action open, bool alternate)
    {
        var stack = new VerticalStackLayout { Spacing = 6, MinimumHeightRequest = 160,
            VerticalOptions = LayoutOptions.Center };
        var visual = Text(icon, 48); visual.HorizontalTextAlignment = TextAlignment.Center; stack.Add(visual);
        var title = Text(L(key), 17); title.FontAttributes = FontAttributes.Bold;
        title.HorizontalTextAlignment = TextAlignment.Center; stack.Add(title);
        var hint = Text(L(hintKey), 13); hint.HorizontalTextAlignment = TextAlignment.Center; stack.Add(hint);
        return TappableCard(stack, L(key) + ". " + L(hintKey),
            () => { open(); return Task.CompletedTask; }, alternate ? Peach : Mint,
            Color.FromArgb(alternate ? "563C32" : "244C43"));
    }

    private View EntityRow(string titleText, string detail, string icon, string? photoPath, Func<Task> open,
        bool child = false)
    {
        var copy = new VerticalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
        var title = Text(titleText, child ? 22 : 19); title.FontAttributes = FontAttributes.Bold; copy.Add(title);
        var details = Text(detail, 14); details.LineBreakMode = LineBreakMode.CharacterWrap; copy.Add(details);
        var grid = new Grid { ColumnSpacing = 12,
            ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) } };
        if (!string.IsNullOrWhiteSpace(photoPath))
            grid.Add(new Image { Source = PhotoSource(photoPath), WidthRequest = child ? 64 : 52,
                HeightRequest = child ? 64 : 52, Aspect = Aspect.AspectFill });
        else
            grid.Add(new Label { Text = icon, FontSize = child ? 44 : 36, WidthRequest = child ? 64 : 52,
                VerticalTextAlignment = TextAlignment.Center, HorizontalTextAlignment = TextAlignment.Center });
        grid.Add(copy, 1);
        var arrow = Text("›", 28); arrow.VerticalTextAlignment = TextAlignment.Center; grid.Add(arrow, 2);
        return TappableCard(grid, titleText + ". " + detail + ". " + L(child ? "MobileOpenProfile" : "Edit"), open,
            child ? Mint : Paper, Color.FromArgb("25443E"));
    }

    private void AddDeleteAction(VerticalStackLayout form, string name, Func<Task> archive)
    {
        var remove = SecondaryButton("Delete", async () =>
        { if (await ConfirmDelete(name)) { await archive(); await RefreshAsync(); } });
        remove.BackgroundColor = Peach; remove.TextColor = Color.FromArgb("7D2F24");
        remove.Margin = new Thickness(0, 16, 0, 0); form.Add(remove);
    }

    private Switch AddToggle(VerticalStackLayout form, string key, bool initial)
    {
        var toggle = new Switch { IsToggled = initial };
        var row = new Grid { ColumnSpacing = 12,
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        var label = Text(L(key), 16); label.VerticalTextAlignment = TextAlignment.Center;
        var tap = new TapGestureRecognizer(); tap.Tapped += (_, _) =>
        { if (!busy && toggle.IsEnabled) toggle.IsToggled = !toggle.IsToggled; };
        label.GestureRecognizers.Add(tap); row.Add(label); row.Add(toggle, 1);
        SemanticProperties.SetDescription(toggle, L(key)); form.Add(row); return toggle;
    }

    private void AddPasswordInput(VerticalStackLayout form, Entry input)
    {
        // Keep the field label visible after typing and offer an explicit visibility toggle.
        var key = formInputs[input].Key;
        form.Add(Text(L(key) + (key == "NewPassword" ? "" : " *"), 14));
        var grid = new Grid { ColumnSpacing = 6,
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        grid.Add(input);
        var reveal = new Button { Text = "◉", WidthRequest = 52, MinimumHeightRequest = 52,
            BackgroundColor = Mint, TextColor = Ink, CornerRadius = 14 };
        SemanticProperties.SetDescription(reveal, L("MobileShowPassword"));
        reveal.Clicked += (_, _) =>
        {
            input.IsPassword = !input.IsPassword;
            reveal.Text = input.IsPassword ? "◉" : "⊘";
            SemanticProperties.SetDescription(reveal, L(input.IsPassword ? "MobileShowPassword" : "MobileHidePassword"));
        };
        grid.Add(reveal, 1); form.Add(grid); form.Add(formInputs[input].Error);
    }

    private Image TappablePhoto(ImageSource source, IReadOnlyList<ImageSource>? gallery = null, int index = 0,
        double height = 160)
    {
        var image = new Image { Source = source, HeightRequest = height, Aspect = Aspect.AspectFit };
        SemanticProperties.SetDescription(image, L("MobileCarePhoto") + ". " + L("PhotoOpen"));
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            if (photoViewerOpen || busy) return;
            photoViewerOpen = true;
            var viewer = new PhotoViewerPage(gallery ?? new[] { source }, index);
            viewer.Disappearing += (_, _) => photoViewerOpen = false;
            try { await Navigation.PushModalAsync(viewer); }
            catch { photoViewerOpen = false; }
        };
        image.GestureRecognizers.Add(tap); return image;
    }

    private async Task ShowMemberProfileAsync(HouseholdMemberItemDto item)
    {
        var history = await api!.GetMemberHistoryAsync(member!.Id, item.Id);
        ShowProfile(item.DisplayName, $"{L("Role_" + item.Role)} · ★ {item.Balance}", "👤", item.PhotoPath, history);
    }

    private async Task ShowPetProfileAsync(PetItemDto item)
    {
        var history = await api!.GetPetHistoryAsync(member!.Id, item.Id);
        var details = item.Species;
        if (item.BirthDate is { } birthDate) details += " · " + L("BirthDate") + ": " + birthDate.ToString("d",
            System.Globalization.CultureInfo.GetCultureInfo(Strings.Culture));
        ShowProfile(item.Name, details, "🐹", item.PhotoPath, history);
    }

    private void ShowProfile(string name, string detail, string icon, string? photo,
        IReadOnlyList<CareLogDto> history)
    {
        ShowForm("MobileProfile", form =>
        {
            if (photo is not null) form.Add(TappablePhoto(PhotoSource(photo), height: 230));
            else { var visual = Text(icon, 64); visual.HorizontalTextAlignment = TextAlignment.Center; form.Add(visual); }
            var title = Text(name, 28); title.FontAttributes = FontAttributes.Bold; form.Add(title);
            form.Add(Text(detail)); form.Add(RoleSectionHeading("📷", L("RecentCare")));
            if (history.Count == 0) form.Add(Text(L("MobileNoHistory"), 16));
            AddHistoryCards(form, history, member!.Role == "Child");
        });
    }

    private void ShowEditReward(RewardItemDto item) => ShowForm("MobileEditReward", form =>
    {
        var name = Field("RewardName"); name.Text = item.Name;
        var cost = Field("RewardPointCost"); cost.Text = item.PointCost.ToString(); cost.Keyboard = Keyboard.Numeric;
        AddInput(form, name); AddInput(form, cost);
        FileResult? photo = null; AddPhotoPicker(form, "RewardImage", file => photo = file, item.ImagePath,
            () => api!.UpdateMediaAsync(member!.Id, "rewards", item.Id, null, true));
        form.Add(Text(L("RewardAudience") + " *", 16));
        var choices = new List<(int Id, CheckBox Box)>();
        foreach (var child in household!.Members.Where(item => item.Role == "Child"))
        {
            var box = new CheckBox { IsChecked = item.VisibleToMemberIds.Contains(child.Id) };
            choices.Add((child.Id, box));
            form.Add(CheckRow(box, child.DisplayName));
        }
        var error = Text("", 13); error.TextColor = Color.FromArgb("DE5757"); error.IsVisible = false;
        form.Add(error); formGroups.Add((() => choices.Any(item => item.Box.IsChecked) ? null : "ChooseRewardAudience", error));
        form.Add(Button("SaveChanges", async () =>
        {
            await api!.UpdateRewardAsync(member!.Id, item.Id, new CreateRewardRequest(name.Text ?? "",
                ParsePoints(cost, 1, 100000, "RewardPointRange"), choices.Where(item => item.Box.IsChecked).Select(item => item.Id).ToList()));
            if (photo is not null) await UploadManagementPhotoAsync("rewards", item.Id, photo);
            await ApplyPendingPhotoChangesAsync();
            await RefreshAsync();
        }));
        var children = household.Members.Where(child => child.Role == "Child").ToList();
        if (children.Count > 0)
        {
            var picker = PickerFor(L("ChooseChild"), children, nameof(HouseholdMemberItemDto.DisplayName));
            AddPicker(form, picker);
            form.Add(SecondaryButton("MobileGiveReward", async () =>
            {
                if (picker.SelectedItem is HouseholdMemberItemDto child &&
                    await DisplayAlertAsync(L("MobileGiveReward"), $"{item.Name} · {child.DisplayName} · ★ {item.PointCost}", L("Approve"), L("Cancel")))
                { await api!.PurchaseRewardAsync(member!.Id, item.Id, child.Id); await RefreshAsync(); }
            }));
        }
        AddDeleteAction(form, item.Name, () => api!.ArchiveRewardAsync(member!.Id, item.Id));
    }, item.Id);
}
