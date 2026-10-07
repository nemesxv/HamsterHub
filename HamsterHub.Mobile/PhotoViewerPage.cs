namespace HamsterHub.Mobile;

internal sealed class PhotoViewerPage : ContentPage
{
    public PhotoViewerPage(IReadOnlyList<ImageSource> photos, int selected)
    {
        BackgroundColor = Color.FromArgb("102522");
        var image = new Image { Aspect = Aspect.AspectFit };
        var viewport = new Grid { IsClippedToBounds = true };
        viewport.Add(image);
        var count = new Label { TextColor = Colors.White, FontSize = 16,
            HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center };
        var index = selected;
        double panX = 0, panY = 0;
        void Reset() { image.Scale = 1; image.TranslationX = image.TranslationY = 0; }
        void Bound()
        {
            image.TranslationX = Math.Clamp(image.TranslationX, -viewport.Width * (image.Scale - 1) / 2,
                viewport.Width * (image.Scale - 1) / 2);
            image.TranslationY = Math.Clamp(image.TranslationY, -viewport.Height * (image.Scale - 1) / 2,
                viewport.Height * (image.Scale - 1) / 2);
        }
        void Show()
        {
            Reset(); image.Source = photos[index]; count.Text = $"{index + 1} / {photos.Count}";
            SemanticProperties.SetDescription(image, Strings.Get("MobileCarePhoto"));
        }
        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += (_, args) =>
        {
            if (args.Status != GestureStatus.Running || viewport.Width <= 0 || viewport.Height <= 0) return;
            var previous = image.Scale;
            image.Scale = Math.Clamp(previous * args.Scale, 1, 6);
            var ratio = image.Scale / previous;
            var focalX = (args.ScaleOrigin.X - .5) * viewport.Width;
            var focalY = (args.ScaleOrigin.Y - .5) * viewport.Height;
            image.TranslationX = focalX - (focalX - image.TranslationX) * ratio;
            image.TranslationY = focalY - (focalY - image.TranslationY) * ratio;
            Bound();
        };
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, args) =>
        {
            if (args.StatusType == GestureStatus.Started)
            { panX = image.TranslationX; panY = image.TranslationY; }
            if (args.StatusType == GestureStatus.Running && image.Scale > 1)
            { image.TranslationX = panX + args.TotalX; image.TranslationY = panY + args.TotalY; Bound(); }
        };
        var doubleTap = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
        doubleTap.Tapped += (_, _) => { if (image.Scale > 1) Reset(); else image.Scale = 2.5; };
        viewport.GestureRecognizers.Add(pinch); viewport.GestureRecognizers.Add(pan);
        viewport.GestureRecognizers.Add(doubleTap);
        Button Control(string text, Action action)
        {
            var button = new Button { Text = text, MinimumHeightRequest = 52, CornerRadius = 14, FontSize = 14,
                LineBreakMode = LineBreakMode.WordWrap, Padding = new Thickness(8, 10),
                TextColor = Colors.White, BackgroundColor = Color.FromArgb("2F8174") };
            button.Clicked += (_, _) => action(); return button;
        }
        var controls = new Grid { ColumnSpacing = 8, Padding = 12,
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) } };
        controls.Add(Control("↺ " + Strings.Get("PhotoResetZoom"), Reset));
        controls.Add(Control("✕ " + Strings.Get("Close"), () => _ = Navigation.PopModalAsync()), 1);
        var navigation = new Grid { ColumnSpacing = 8, Padding = 12,
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star), new(GridLength.Star) } };
        var previousButton = Control("◀ " + Strings.Get("PhotoPrevious"), () =>
        { index = (index - 1 + photos.Count) % photos.Count; Show(); });
        var nextButton = Control(Strings.Get("PhotoNext") + " ▶", () =>
        { index = (index + 1) % photos.Count; Show(); });
        previousButton.IsVisible = nextButton.IsVisible = photos.Count > 1;
        navigation.Add(previousButton); navigation.Add(count, 1); navigation.Add(nextButton, 2);
        var hint = new Label { Text = Strings.Get("PhotoZoomHint"), TextColor = Colors.White,
            FontSize = 14, HorizontalTextAlignment = TextAlignment.Center, Margin = 12 };
        var root = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star),
            new(GridLength.Auto), new(GridLength.Auto) } };
        root.Add(navigation); root.Add(viewport, 0, 1); root.Add(hint, 0, 2); root.Add(controls, 0, 3);
        Content = root;
        Show();
    }
}
