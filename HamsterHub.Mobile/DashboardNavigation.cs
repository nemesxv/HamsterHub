using HamsterHub.Contracts;

namespace HamsterHub.Mobile;

public sealed partial class MainPage
{
    private enum DashboardSection { Today, Rewards, Family, History }
    private DashboardSection dashboardSection;
    private int dashboardHistoryVisible = 10;
    private readonly Dictionary<DashboardSection, double> sectionScroll = [];
    private readonly Grid dashboardNavigation = new() { ColumnSpacing = 6, Padding = new Thickness(10, 8), IsVisible = false };

    private void RenderDashboardNavigation()
    {
        dashboardNavigation.Clear(); dashboardNavigation.ColumnDefinitions.Clear();
        if (member is null) { dashboardNavigation.IsVisible = false; return; }
        if (member.Role == "Parent" && dashboardSection == DashboardSection.Rewards) dashboardSection = DashboardSection.Today;
        var tabs = new List<(DashboardSection Section, string Icon, string Key)>
        {
            (DashboardSection.Today, "✓", "MobileTabToday"),
            (DashboardSection.Family, "👪", "MobileTabFamily"),
            (DashboardSection.History, "📷", "MobileTabHistory")
        };
        if (member.Role == "Child") tabs.Insert(1, (DashboardSection.Rewards, "🎁", "Rewards"));
        for (var index = 0; index < tabs.Count; index++)
        {
            dashboardNavigation.ColumnDefinitions.Add(new(GridLength.Star));
            var tab = tabs[index];
            var selected = tab.Section == dashboardSection;
            var content = new VerticalStackLayout { Spacing = 0 };
            var icon = Text(tab.Icon, 23); icon.HorizontalTextAlignment = TextAlignment.Center;
            var label = Text(L(tab.Key), 12); label.HorizontalTextAlignment = TextAlignment.Center;
            label.FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None;
            content.Add(icon); content.Add(label);
            var tile = (Border)TappableCard(content, L(tab.Key) + (selected ? ". " + L("MobileSelectedSection") : ""), () =>
            {
                if (dashboardSection == tab.Section) return Task.CompletedTask;
                sectionScroll[dashboardSection] = scroll.ScrollY;
                dashboardSection = tab.Section;
                ShowDashboard(resetScroll: false);
                RestoreScroll(sectionScroll.GetValueOrDefault(dashboardSection));
                return Task.CompletedTask;
            }, selected ? Mint : Paper, Color.FromArgb(selected ? "244C43" : "17312D"));
            tile.Padding = new Thickness(4, 2);
            tile.StrokeThickness = selected ? 2 : 0;
            dashboardNavigation.Add(tile, index);
        }
        dashboardNavigation.SetAppThemeColor(BackgroundColorProperty, Cream, Color.FromArgb("102522"));
        dashboardNavigation.IsVisible = true;
    }

    private View ChildFamilySection()
    {
        var section = new VerticalStackLayout { Spacing = 14 };
        section.Add(RoleSectionHeading("👪", L("FamilyMembers")));
        foreach (var item in household!.Members)
            section.Add(EntityRow(item.DisplayName, $"{L("Role_" + item.Role)} · ★ {item.Balance}",
                "👤", item.PhotoPath, () => ShowMemberProfileAsync(item), true));
        section.Add(RoleSectionHeading("🐾", L("FamilyPets")));
        if (household.Pets.Count == 0) section.Add(Text(L("MobileNoPets"), 16));
        foreach (var item in household.Pets)
            section.Add(EntityRow(item.Name, item.Species, "🐹", item.PhotoPath, () => ShowPetProfileAsync(item), true));
        return section;
    }

    private View RewardHistorySection()
    {
        var section = new VerticalStackLayout { Spacing = 12 };
        section.Add(RoleSectionHeading("🎁", L("PointHistory")));
        if (household!.RewardHistory.Count == 0) section.Add(Text(L("MobileNoRewardHistory"), 16));
        foreach (var item in household.RewardHistory)
        {
            var detail = item.Status == "Approved" ? $"{L("RewardStatus_" + item.Status)} · −{item.PointsCost} ★"
                : $"{L("RewardStatus_" + item.Status)} · {Format("MobilePotentialCost", item.PointsCost)}";
            detail += "\n" + item.RequestedAt.ToLocalTime().ToString("g", System.Globalization.CultureInfo.GetCultureInfo(Strings.Culture));
            section.Add(ManagementRow(item.RewardName, detail));
        }
        return section;
    }

    private void AddHistoryCards(VerticalStackLayout container, IReadOnlyList<CareLogDto> history, bool child)
    {
        var shown = 0;
        Button? more = null;
        void Append()
        {
            if (more is not null) container.Remove(more);
            var end = Math.Min(shown == 0 && !showingForm ? dashboardHistoryVisible : shown + 10, history.Count);
            while (shown < end) container.Add(RoleLogCard(history[shown++], false, child));
            if (!showingForm) dashboardHistoryVisible = Math.Max(10, shown);
            if (shown < history.Count)
            {
                more = SecondaryButton("MobileMoreHistory", () => { Append(); return Task.CompletedTask; });
                container.Add(more);
            }
        }
        Append();
    }

    private View CheckRow(CheckBox box, string label)
    {
        var row = new Grid { ColumnSpacing = 10, MinimumHeightRequest = 52,
            ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        row.Add(box);
        var text = Text(label, 16); text.VerticalTextAlignment = TextAlignment.Center;
        var tap = new TapGestureRecognizer(); tap.Tapped += (_, _) => { if (!busy) box.IsChecked = !box.IsChecked; };
        text.GestureRecognizers.Add(tap); row.Add(text, 1);
        SemanticProperties.SetDescription(box, label);
        return row;
    }

    private View CardFlow(IReadOnlyList<View> cards)
    {
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        foreach (var card in cards) grid.Add(card);
        var currentColumns = 0;
        void Arrange()
        {
            var count = grid.Width >= 680 ? 2 : 1;
            if (currentColumns == count) return;
            currentColumns = count;
            grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
            for (var column = 0; column < count; column++) grid.ColumnDefinitions.Add(new(GridLength.Star));
            for (var row = 0; row < (cards.Count + count - 1) / count; row++) grid.RowDefinitions.Add(new(GridLength.Auto));
            for (var index = 0; index < cards.Count; index++)
            { Grid.SetColumn(cards[index], index % count); Grid.SetRow(cards[index], index / count); }
        }
        grid.SizeChanged += (_, _) => Arrange(); Arrange();
        return grid;
    }
}
