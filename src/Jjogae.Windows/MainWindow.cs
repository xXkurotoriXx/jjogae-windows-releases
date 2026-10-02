using System.Globalization;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Microsoft.Win32;
using Microsoft.Toolkit.Uwp.Notifications;

namespace Jjogae.Windows;

public sealed partial class MainWindow : Window
{
    private readonly AppController app;
    private readonly Grid root = new();
    private readonly StackPanel content = new();
    private readonly StackPanel navigation = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel sidebar = new() { Width = 166, Visibility = Visibility.Collapsed };
    private readonly TextBlock status = new() { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Image backdrop = new() { Stretch = Stretch.UniformToFill, IsHitTestVisible = false };
    private readonly BackgroundImageStore backgroundImages;
    private readonly Border header = new(), capsule = new(), footer = new();
    private readonly System.Windows.Forms.NotifyIcon tray = new();
    private readonly WindowsNotifications windowsNotifications = new();
    private readonly string[] sections = ["상황실", "치즈 기록", "방송 기록", "카페 새 글", "설정"];
    private readonly string[] glyphs = ["home", "chart", "calendar", "chat", "settings"];
    private readonly Dictionary<string, BitmapImage> images = new();
    private Button? loginButton, refreshButton;
    private int section, cheesePage;
    private DateOnly month = new(Channel.Today.Year, Channel.Today.Month, 1);
    private DateOnly? selectedDay;
    private long renderedRevision = -1;
    private DateOnly renderedDay = Channel.Today;
    private readonly HashSet<string> selectedCafe = [];
    private bool dark, building, themeLoaded;
    private Brush ink = Brushes.Black, muted = Brushes.Gray, accent = Brushes.SlateBlue, card = Brushes.White, line = Brushes.LightGray;
    private readonly List<AspectFrame> mediaFrames = [];
    private UniformGrid? homeMedia, archiveTiles;
    private Grid? overviewMetrics;
    private int overviewColumns;
    private Grid? headerBar;
    private FrameworkElement? brandTitle, brandImage;
    private bool compactAccountChip;
    private readonly Border bottomNavigation = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(14, 6, 14, 10) };
    private Button? sidebarToggle;
    private bool compactNavigation;

    public MainWindow(AppController controller)
    {
        app = controller;
        installation = new AppInstallation(app.Store.DirectoryPath);
        if (!app.IsTest) { installation.Load(); installation.ReconcileStartup(); }
        updater = new WindowsUpdater(installation, app.Store.DirectoryPath, () => { app.Save(); ExitApplication(); });
        updater.Changed += UpdateInstallationStatus;
        backgroundImages = new BackgroundImageStore(app.Store.DirectoryPath);
        backgroundSaveTimer.Tick += (_, _) => FlushBackgroundPreferences();
        Title = "쪼개 상황실"; MinWidth = WindowLayout.MinimumWidth; MinHeight = WindowLayout.MinimumHeight;
        Width = Math.Max(MinWidth, Math.Min(1000, SystemParameters.WorkArea.Width - 48));
        Height = Math.Max(MinHeight, Math.Min(740, SystemParameters.WorkArea.Height - 48));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Malgun Gothic"); FontSize = 13;
        Language = System.Windows.Markup.XmlLanguage.GetLanguage("ko-KR");
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Icon = LoadImage("pack://application:,,,/Assets/AppIcon.png");
        WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 64, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(12), UseAeroCaptionButtons = false });
        SourceInitialized += (_, _) => InitializeGlass();
        ApplyTheme();
        root.Children.Add(backdrop);
        var layout = new DockPanel();
        BuildHeader(); DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        footer.Padding = new Thickness(26, 7, 26, 9); footer.Child = status;
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        DockPanel.SetDock(bottomNavigation, Dock.Bottom); layout.Children.Add(bottomNavigation);
        sidebar.Margin = new Thickness(14, 20, 4, 12); DockPanel.SetDock(sidebar, Dock.Left); layout.Children.Add(sidebar);
        content.Margin = new Thickness(30, 18, 30, 18);
        content.MaxWidth = WindowLayout.MaximumPageWidth; content.HorizontalAlignment = HorizontalAlignment.Stretch;
        scroll.Content = content; layout.Children.Add(scroll); root.Children.Add(layout); Content = root;
        scroll.SizeChanged += (_, _) => UpdateResponsiveLayout();
        SizeChanged += (_, _) => UpdateResponsiveLayout();
        app.Changed += ModelChanged; app.Notify += ShowNotification;
        if (!app.IsTest)
        {
            ToastNotificationManagerCompat.OnActivated += NotificationActivated;
            windowsNotifications.Changed += UpdateNotificationStatus;
            windowsNotifications.Failed += app.RetryNotification;
            tray.Text = Title;
            using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/AppIcon.ico"))!.Stream;
            tray.Icon = new System.Drawing.Icon(stream); tray.Visible = true;
            tray.DoubleClick += (_, _) => { Show(); WindowState = WindowState.Normal; Activate(); };
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("쪼개 상황실 열기", null, (_, _) => { Show(); WindowState = WindowState.Normal; Activate(); });
            menu.Items.Add("완전히 종료", null, (_, _) => ExitApplication());
            tray.ContextMenuStrip = menu;
        }
        Loaded += async (_, _) =>
        {
            Render(); if (!app.IsTest) await windowsNotifications.Initialize(); app.Start(); await RestoreBackground();
            if (!app.IsTest) { if (UpdateInstaller.ConfirmHealth(Environment.GetCommandLineArgs())) updater.MarkUpdated(); updater.Start(); }
        };
        Closed += (_, _) => { updater.Changed -= UpdateInstallationStatus; updater.Dispose(); };
        Closed += (_, _) =>
        {
            if (app.IsTest) return;
            ToastNotificationManagerCompat.OnActivated -= NotificationActivated;
            windowsNotifications.Changed -= UpdateNotificationStatus;
            windowsNotifications.Failed -= app.RetryNotification;
        };
        Closed += (_, _) => { backgroundClosed = true; glassSource?.RemoveHook(GlassMessages); FlushBackgroundPreferences(); tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); app.Changed -= ModelChanged; app.Notify -= ShowNotification; SystemEvents.UserPreferenceChanged -= PreferencesChanged; };
        SystemEvents.UserPreferenceChanged += PreferencesChanged;
        Activated += (_, _) => { if (!app.IsTest && section == 4) Render(); };
    }

    private void MinimizeToBackground() { FlushBackgroundPreferences(); WindowState = WindowState.Minimized; }
    private void ExitApplication() => Application.Current.Shutdown();
    private void NotificationActivated(ToastNotificationActivatedEventArgsCompat args) => Dispatcher.BeginInvoke(new Action(() =>
    {
        Show(); WindowState = WindowState.Normal; Activate();
        Select(args.Argument == "section=cafe" ? 3 : 0);
    }));

    private void BuildHeader()
    {
        var bar = new Grid { Height = 64, Margin = new Thickness(10, 0, 10, 0) };
        headerBar = bar;
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var toggle = IconButton("sidebar", "사이드바 표시 또는 숨기기", () => { app.State.Settings.SidebarVisible = !app.State.Settings.SidebarVisible; app.Save(); UpdateResponsiveLayout(); });
        sidebarToggle = toggle;
        brand.Children.Add(toggle);
        brandImage = new Image { Source = Icon, Width = 30, Height = 30, Clip = new EllipseGeometry(new Point(15, 15), 15, 15), Margin = new Thickness(5, 0, 10, 0) };
        brandTitle = Text(Title, 16, true);
        brand.Children.Add(brandImage); brand.Children.Add(brandTitle); bar.Children.Add(brand);
        capsule.Child = navigation; capsule.CornerRadius = new CornerRadius(27); capsule.Padding = new Thickness(5);
        Point? swipeStart = null;
        capsule.PreviewMouseLeftButtonDown += (_, e) => swipeStart = e.GetPosition(capsule);
        capsule.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (swipeStart is not { } start) return; swipeStart = null;
            var delta = e.GetPosition(capsule) - start;
            if (Math.Abs(delta.X) > 32 && Math.Abs(delta.X) > Math.Abs(delta.Y) * 1.25)
            { e.Handled = true; Select(Math.Clamp(section + (delta.X < 0 ? 1 : -1), 0, sections.Length - 1)); }
        };
        capsule.BorderThickness = new Thickness(1); capsule.HorizontalAlignment = HorizontalAlignment.Center; capsule.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(capsule, 1); bar.Children.Add(capsule);
        refreshButton = IconButton("refresh", "새로고침", async () => await app.Refresh()); brand.Children.Add(refreshButton);
        loginButton = Button("로그인", () => { if (app.State.Account is null) app.ShowLogin(this); else ShowChatProfile(); }); loginButton.MinWidth = 70; loginButton.MaxWidth = 128; loginButton.HorizontalContentAlignment = HorizontalAlignment.Stretch; brand.Children.Add(loginButton);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Tag = "window-controls" };
        foreach (var (icon, label, action) in new (string, string, Action)[] {
            ("minus", "최소화", () => SystemCommands.MinimizeWindow(this)),
            ("maximize", "최대화 또는 복원", () => { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }),
            ("close", "닫기 · 백그라운드 실행", MinimizeToBackground) })
        {
            var button = IconButton(icon, label, action); button.Style = (Style)FindResource("CaptionButton"); actions.Children.Add(button);
        }
        Grid.SetColumn(actions, 2); bar.Children.Add(actions);
        header.Child = bar; header.BorderThickness = new Thickness(0, 0, 0, 1);
        foreach (var control in Descendants<Button>(bar)) WindowChrome.SetIsHitTestVisibleInChrome(control, true);
    }

    private void ApplyTheme()
    {
        var saved = app.State.Settings;
        var systemLight = Convert.ToInt32(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1), CultureInfo.InvariantCulture) != 0;
        var nextDark = saved.Theme == "dark" || saved.Theme == "system" && !systemLight;
        if (!themeLoaded || dark != nextDark)
        {
            dark = nextDark;
            Application.Current.Resources.MergedDictionaries.Clear();
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent." + (dark ? "Dark" : "Light") + ".xaml") });
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Theme.xaml") });
            themeLoaded = true;
        }
        nativeGlassActive = ApplyNativeGlass();
        ink = Brush(dark ? "#F6F5FC" : "#222638"); muted = Brush(dark ? "#C1C5D6" : "#555E76");
        accent = Brush(dark ? "#C6B5FF" : "#6345B9");
        line = GlassGradient(dark ? "#65E3E9FF" : "#EEFFFFFF", dark ? "#243E4964" : "#606D7798");
        card = GlassGradient(dark ? "#E02A3248" : "#EBFFFFFF", dark ? "#D51C2234" : "#D5E9EDF8");
        Brush? buttonSurface = GlassGradient(dark ? "#B955607C" : "#EFFFFFFF", dark ? "#A32D354F" : "#B8E1E6F3");
        if (SystemParameters.HighContrast)
        {
            ink = SystemColors.WindowTextBrush; muted = ink; accent = SystemColors.HighlightBrush;
            line = ink; card = SystemColors.WindowBrush; buttonSurface = null;
        }
        foreach (var (key, value) in new (string, Brush)[] { ("Ink", ink), ("Muted", muted), ("Accent", accent), ("Line", line), ("Surface", card), ("ButtonSurface", buttonSurface ?? SystemColors.ControlBrush), ("Hover", Brush(dark ? "#28FFFFFF" : "#247E6EB6")), ("AccentFillColorDefaultBrush", accent) })
            Application.Current.Resources[key] = value;
        Background = nativeGlassActive ? Brushes.Transparent : Brush(dark ? "#171C2A" : "#EEF0F8"); Foreground = ink;
        root.Background = nativeGlassActive ? Brush(dark ? "#48131A2B" : "#48EDF1FF") : Background;
        if (SystemParameters.HighContrast) Background = root.Background = SystemColors.WindowBrush;
        backdrop.Opacity = saved.BackgroundOpacity;
        backdrop.Visibility = SystemParameters.HighContrast ? Visibility.Collapsed : Visibility.Visible;
        // An opaque title surface prevents the system accent caption from bleeding through.
        header.Background = Brush(dark ? "#202536" : "#F3F4F9"); header.BorderBrush = line;
        capsule.Background = buttonSurface ?? SystemColors.ControlBrush; capsule.BorderBrush = line;
        if (SystemParameters.HighContrast) header.Background = SystemColors.WindowBrush;
        footer.Background = Brushes.Transparent; status.Foreground = muted;
    }
    private void PreferencesChanged(object sender, UserPreferenceChangedEventArgs e) => Dispatcher.BeginInvoke(new Action(Render));
    private void ModelChanged()
    {
        status.Text = app.Status; status.ToolTip = app.Status;
        if (refreshButton is not null) refreshButton.IsEnabled = !app.Busy;
        UpdateSettingsStatus();
        UpdateAccountChip();
        var dayChanged = renderedDay != Channel.Today;
        if (dayChanged) { cheeseSelection.Advance(Channel.Today); }
        if (!building && section != 4 && (renderedRevision != app.DataRevision || dayChanged)) Render();
    }
    private bool ShowNotification(Notification notification) => !app.IsTest && windowsNotifications.Send(notification);
    private void Select(int next)
    {
        if (section == 3 && next != 3) { ResetRecovery(); recoveryExpanded = false; }
        section = next; Render(); scroll.ScrollToTop();
    }
    private void Render()
    {
        if (building) return;
        building = true;
        var offset = scroll.VerticalOffset;
        try
        {
            ApplyTheme(); status.Text = app.Status;
            navigation.Children.Clear(); sidebar.Children.Clear(); mediaFrames.Clear();
            for (var index = 0; index < sections.Length; index++)
            {
                var target = index;
                var button = IconButton(glyphs[index], sections[index], () => Select(target)); button.Width = 44; button.Height = 42; button.Margin = new Thickness(1, 0, 1, 0);
                button.Background = index == section ? GlassGradient(dark ? "#DD796AA6" : "#FFF7F1FF", dark ? "#B24C416F" : "#D1D9CCF6") : Brushes.Transparent;
                button.BorderThickness = index == section ? new Thickness(1) : new Thickness(0);
                if (SystemParameters.HighContrast) button.Background = SystemColors.ControlBrush;
                button.Content = IconView(glyphs[index], 21, index == section ? accent : muted);
                WindowChrome.SetIsHitTestVisibleInChrome(button, true); navigation.Children.Add(button);
                var link = Button(sections[index], () => Select(target)); link.HorizontalContentAlignment = HorizontalAlignment.Left; link.Margin = new Thickness(0, 3, 0, 3);
                link.Background = index == section ? card : Brushes.Transparent; sidebar.Children.Add(link);
            }
            content.Children.Clear(); homeMedia = null; overviewMetrics = null; overviewColumns = 0; archiveTiles = null; recordPanels = null; recordPanelLeft = null; recordPanelRight = null;
            var heading = Text(section == 0 ? Title : sections[section], 26, true); heading.Margin = new Thickness(0, 0, 0, 16); content.Children.Add(heading);
            StartupQuestion();
            switch (section) { case 0: Overview(); break; case 1: CheesePage(); break; case 2: BroadcastPage(); break; case 3: CafePage(); break; case 4: SettingsPage(); break; }
            UpdateResponsiveLayout();
            UpdateAccountChip(); renderedRevision = app.DataRevision; renderedDay = Channel.Today;
        }
        finally { building = false; }
        scroll.ScrollToVerticalOffset(offset);
    }

    private void UpdateResponsiveLayout()
    {
        var compact = WindowLayout.Compact(ActualWidth);
        if (headerBar is not null && compactNavigation != compact)
        {
            compactNavigation = compact;
            if (compact) { headerBar.Children.Remove(capsule); bottomNavigation.Child = capsule; }
            else { bottomNavigation.Child = null; headerBar.Children.Add(capsule); }
        }
        bottomNavigation.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        if (sidebarToggle is not null) sidebarToggle.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        sidebar.Visibility = !compact && app.State.Settings.SidebarVisible ? Visibility.Visible : Visibility.Collapsed;
        capsule.Visibility = compact || sidebar.Visibility != Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        foreach (var nav in navigation.Children.OfType<Button>()) { nav.Width = compact ? 58 : 44; nav.Height = compact ? 44 : 34; }
        var hideBrand = ActualWidth < 1400;
        if (brandTitle is not null) brandTitle.Visibility = hideBrand ? Visibility.Collapsed : Visibility.Visible;
        if (brandImage is not null) brandImage.Visibility = hideBrand ? Visibility.Collapsed : Visibility.Visible;
        if (loginButton is not null) loginButton.MaxWidth = ActualWidth < 1100 ? 128 : 200;
        var nextCompactChip = ActualWidth < 1100;
        if (compactAccountChip != nextCompactChip) { compactAccountChip = nextCompactChip; UpdateAccountChip(); }
        sidebar.Width = 205;
        sidebar.Margin = new Thickness(compact ? 8 : 14, compact ? 10 : 20, 4, 12);
        content.Margin = compact ? new Thickness(14, 18, 14, 10) : new Thickness(26, 12, 26, 18);
        footer.Padding = compact ? new Thickness(14, 4, 14, 5) : new Thickness(26, 7, 26, 9);
        var available = Math.Max(0, scroll.ActualWidth - content.Margin.Left - content.Margin.Right - 18);
        if (homeMedia is not null)
        {
            homeMedia.Columns = WindowLayout.Columns(available) > 1 ? 2 : 1;
        }
        if (overviewMetrics is not null)
        {
            var columns = OverviewExpanded ? 2 : compact ? (available >= 360 ? 2 : 1) : available >= 780 ? 4 : 3;
            if (overviewColumns != columns)
            {
                overviewColumns = columns;
                overviewMetrics.ColumnDefinitions.Clear(); overviewMetrics.RowDefinitions.Clear();
                // Use one track per card except when a three-card final row needs shared subdivisions.
                var units = 12;
                for (var track = 0; track < units; track++) overviewMetrics.ColumnDefinitions.Add(new ColumnDefinition());
                var index = 0; var rowIndex = 0;
                foreach (var rowCount in WindowLayout.BalancedRows(overviewMetrics.Children.Count, columns))
                {
                    overviewMetrics.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    for (var column = 0; column < rowCount; column++)
                    {
                        var span = units / rowCount; var child = overviewMetrics.Children[index++];
                        Grid.SetRow(child, rowIndex); Grid.SetColumn(child, column * span); Grid.SetColumnSpan(child, span);
                    }
                    rowIndex++;
                }
            }
        }
        if (archiveTiles is not null) archiveTiles.Columns = WindowLayout.Columns(available);
        UpdateOverviewLayout(available);
        if (recordPanels is not null && recordPanelLeft is not null && recordPanelRight is not null)
        {
            var stacked = available < 750;
            recordPanels.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            Grid.SetRow(recordPanelRight, stacked ? 1 : 0); Grid.SetColumn(recordPanelRight, stacked ? 0 : 1);
            recordPanelLeft.Margin = new Thickness(0, 0, stacked ? 0 : 14, 14);
        }
    }

    private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    private TextBlock Text(string value, double size = 13, bool bold = false, bool secondary = false)
    {
        var text = new TextBlock { Text = value, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 3) };
        text.SetResourceReference(TextBlock.ForegroundProperty, secondary ? "Muted" : "Ink");
        text.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty,
            new System.Windows.Data.Binding(nameof(TextBlock.Text)) { Source = text });
        return text;
    }
    private Button Button(string label, Action action)
    {
        var button = new Button { Style = (Style)FindResource("SoftButton"), Content = label, Margin = new Thickness(0, 0, 8, 0) };
        button.Click += (_, _) => { try { action(); } catch (Exception e) { Error(e); } };
        return button;
    }
    private Button Button(string label, Func<Task> action)
    {
        var button = Button(label, () => { });
        button.Click += async (_, _) => { button.IsEnabled = false; try { await action(); } catch (Exception e) { Error(e); } finally { button.IsEnabled = true; } };
        return button;
    }
    private Button IconButton(string key, string label, Action action) => DecorateIconButton(Button("", action), key, label);
    private Button IconButton(string key, string label, Func<Task> action) => DecorateIconButton(Button("", action), key, label);
    private Button DecorateIconButton(Button button, string key, string label)
    {
        button.Content = IconView(key, 20, accent, true); button.Width = 38; button.Height = 38; button.Padding = new Thickness(8); button.BorderThickness = new Thickness(0); button.Background = Brushes.Transparent;
        button.ToolTip = label; System.Windows.Automation.AutomationProperties.SetName(button, label); return button;
    }
    private static UIElement IconView(string key, double size, Brush color, bool themeAccent = false)
    {
        if (key == "settings")
        {
            var outline = new StreamGeometry();
            using (var drawing = outline.Open())
            {
                var points = new List<Point>();
                for (var tooth = 0; tooth < 8; tooth++)
                    foreach (var (offset, radius) in new[] { (-20d, 8d), (-10d, 10d), (10d, 10d), (20d, 8d) })
                    {
                        var angle = (tooth * 45 + offset - 90) * Math.PI / 180;
                        points.Add(new Point(12 + radius * Math.Cos(angle), 12 + radius * Math.Sin(angle)));
                    }
                drawing.BeginFigure(points[0], false, true); drawing.PolyLineTo(points.Skip(1).ToArray(), true, false);
            }
            outline.Freeze();
            var geometry = new GeometryGroup(); geometry.Children.Add(outline); geometry.Children.Add(new EllipseGeometry(new Point(12, 12), 3.1, 3.1)); geometry.Freeze();
            var shape = new System.Windows.Shapes.Path { Data = geometry, Stroke = color, StrokeThickness = 1.55, StrokeLineJoin = PenLineJoin.Round };
            if (themeAccent) shape.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Accent");
            var gear = new Canvas { Width = 24, Height = 24 }; gear.Children.Add(shape);
            return new Viewbox { Width = size, Height = size, Child = gear };
        }
        var data = key switch {
            "home" => "M3,11 L12,3 21,11 M5,10 L5,21 10,21 10,14 14,14 14,21 19,21 19,10",
            "chart" => "M4,20 L21,20 M6,17 L6,11 M11,17 L11,7 M16,17 L16,3",
            "activity" => "M2,12 L7,12 10,4 14,21 17,12 22,12",
            "calendar" => "M5,5 L19,5 Q21,5 21,7 L21,19 Q21,21 19,21 L5,21 Q3,21 3,19 L3,7 Q3,5 5,5 M3,10 L21,10 M8,2 L8,7 M16,2 L16,7",
            "chat" => "M5,4 L19,4 Q22,4 22,7 L22,16 Q22,19 19,19 L10,19 5,22 5,19 Q2,19 2,16 L2,7 Q2,4 5,4 M7,11 L7.1,11 M12,11 L12.1,11 M17,11 L17.1,11",
            "sidebar" => "M4,4 L20,4 20,20 4,20 Z M9,4 L9,20 M6,8 L7,8 M6,12 L7,12",
            "refresh" => "M20,8 A8,8 0 1 0 20,17 M20,3 L20,8 15,8",
            "minus" => "M5,12 L19,12", "maximize" => "M5,5 L19,5 19,19 5,19 Z", "close" => "M5,5 L19,19 M19,5 L5,19",
            "chevron-left" => "M15,5 L8,12 15,19", "chevron-right" => "M9,5 L16,12 9,19", "chevron-down" => "M5,9 L12,16 19,9",
            "people" => "M15,8 A3,3 0 1 1 9,8 A3,3 0 1 1 15,8 M5,21 L5,18 Q5,13 12,13 Q19,13 19,18 L19,21 M4,6 Q0,9 4,12 M20,6 Q24,9 20,12",
            "heart" => "M12,21 L3,12 C-2,3 8,1 12,7 C16,1 26,3 21,12 Z",
            "star" => "M12,2 L15,8 22,9 17,14 18,21 12,18 6,21 7,14 2,9 9,8 Z",
            "tree" => "M12,2 L5,9 8,9 3,16 10,16 10,22 14,22 14,16 21,16 16,9 19,9 Z",
            "gift" => "M3,9 L21,9 21,14 3,14 Z M5,14 L5,22 19,22 19,14 M12,9 L12,22 M12,9 C1,9 5,-1 12,9 C19,-1 23,9 12,9",
            "external" => "M14,3 L21,3 21,10 M21,3 L10,14 M10,5 L5,5 Q3,5 3,7 L3,19 Q3,21 5,21 L17,21 Q19,21 19,19 L19,14",
            "play" => "M8,4 L21,12 8,20 Z", _ => "M4,12 L20,12" };
        var canvas = new Canvas { Width = 24, Height = 24 };
        var path = new System.Windows.Shapes.Path { Data = Geometry.Parse(data), Stroke = color, StrokeThickness = 1.65, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        if (themeAccent) path.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Accent");
        canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }
    private static void Error(Exception error) => MessageBox.Show(error.Message, "쪼개 상황실", MessageBoxButton.OK, MessageBoxImage.Warning);
    private static StackPanel Column(params UIElement[] children) { var panel = new StackPanel(); foreach (var child in children) panel.Children.Add(child); return panel; }
    private static WrapPanel Row(params UIElement[] children) { var row = new WrapPanel { Margin = new Thickness(0, 5, 0, 10) }; foreach (var child in children) row.Children.Add(child); return row; }
    private Border Card(UIElement child) => new() { Child = child, Background = card, BorderBrush = line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(22), Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 14) };
    private UIElement Metric(string title, string value, string detail = "", string icon = "star", bool textValue = false)
    {
        var top = new Grid(); top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) }); top.ColumnDefinitions.Add(new ColumnDefinition());
        var symbol = new Viewbox { Width = 20, Height = 20, Child = MetricIcon(icon), HorizontalAlignment = HorizontalAlignment.Left }; top.Children.Add(symbol);
        var label = Text(title, 11, secondary: true); label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetColumn(label, 1); top.Children.Add(label);
        var number = Text(value, textValue ? 16 : 20, true); number.TextWrapping = TextWrapping.NoWrap; number.Margin = new Thickness(0, 3, 0, 2); number.ToolTip = value;
        var fitted = new Viewbox { Child = number, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, Height = 27 };
        var small = Text(detail.Replace('\n', ' '), 10, secondary: true); small.Margin = new Thickness(0); small.TextWrapping = TextWrapping.NoWrap; small.TextTrimming = TextTrimming.CharacterEllipsis; small.ToolTip = detail;
        var cardView = Card(Column(top, fitted, small)); cardView.MinHeight = 82; cardView.CornerRadius = new CornerRadius(16); cardView.Padding = new Thickness(12, 9, 12, 9); return cardView;
    }
    private UIElement MetricIcon(string icon)
    {
        var asset = icon switch { "chzzk" => "BrandCHZZK", "youtube" => "BrandYouTube", "naver-cafe" => "BrandNaverCafe", _ => null };
        if (asset is null) return IconView(icon, 19, accent);
        var image = new Image { Source = CachedImage($"pack://application:,,,/Assets/{asset}.png"), Width = 24, Height = 24, Stretch = Stretch.Uniform, Tag = icon };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }
    private static BitmapImage LoadImage(string uri) { var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(uri); image.EndInit(); image.Freeze(); return image; }
    private BitmapImage CachedImage(string uri) { if (!images.TryGetValue(uri, out var image)) { image = LoadImage(uri); images[uri] = image; } return image; }
    private UIElement Thumbnail(string uri)
    {
        var holder = new Grid { Background = Brush(dark ? "#191720" : "#EFECF3") };
        var placeholder = new Border { Child = IconView("play", 32, muted), Opacity = .55 }; holder.Children.Add(placeholder);
        var image = new Image { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        try
        {
            if (images.TryGetValue(uri, out var cached)) image.Source = cached;
            else if (uri.StartsWith("pack://", StringComparison.Ordinal)) image.Source = CachedImage(uri);
            else if (ApiClient.SafeImage(uri))
            {
                _ = SetDisplayImage(image, uri, placeholder);
            }
            if (image.Source is not null) placeholder.Visibility = Visibility.Collapsed;
            image.ImageFailed += (_, _) => { image.Visibility = Visibility.Collapsed; placeholder.Visibility = Visibility.Visible; };
        }
        catch { image.Source = null; }
        holder.Children.Add(image);
        var frame = new AspectFrame { Child = holder }; mediaFrames.Add(frame); return frame;
    }
    private readonly Dictionary<string, Task<BitmapImage?>> imageLoads = new();
    private async Task SetDisplayImage(Image image, string url, UIElement? placeholder = null)
    {
        if (images.TryGetValue(url, out var cached)) { image.Source = cached; if (placeholder is not null) placeholder.Visibility = Visibility.Collapsed; return; }
        if (!imageLoads.TryGetValue(url, out var pending))
        {
            if (imageLoads.Count >= 24) return;
            pending = FetchImage(); imageLoads[url] = pending;
        }
        var bitmap = await pending; imageLoads.Remove(url);
        if (bitmap is null) return;
        if (images.Count >= 120 && images.Keys.FirstOrDefault(x => x.StartsWith("https://", StringComparison.Ordinal)) is { } oldest) images.Remove(oldest);
        images[url] = bitmap; image.Source = bitmap; if (placeholder is not null) placeholder.Visibility = Visibility.Collapsed;
        async Task<BitmapImage?> FetchImage()
        {
            try
            {
                var bytes = await app.DisplayImage(url);
                return await Task.Run(() =>
                {
                    using var stream = new MemoryStream(bytes);
                    var decoded = new BitmapImage(); decoded.BeginInit(); decoded.CacheOption = BitmapCacheOption.OnLoad; decoded.DecodePixelWidth = 960; decoded.StreamSource = stream; decoded.EndInit(); decoded.Freeze(); return decoded;
                });
            }
            catch { return null; }
        }
    }
    private ComboBox Choice(IEnumerable<string> choices, string selected, Action<string> changed)
    {
        var combo = new ComboBox { MinWidth = 138, MinHeight = 36, FontSize = 13, Margin = new Thickness(0, 0, 8, 0), ItemsSource = choices, SelectedItem = selected };
        combo.SelectionChanged += (_, _) => { if (!building && combo.SelectedItem is string value) changed(value); }; return combo;
    }
    private DatePicker DateInput(DateOnly? value) => new() { SelectedDate = value?.ToDateTime(TimeOnly.MinValue), DisplayDateEnd = Channel.Today.ToDateTime(TimeOnly.MinValue), Width = 166, MinHeight = 36, FontSize = 13, Margin = new Thickness(0, 3, 10, 5) };
    private static DateOnly? DateValue(DatePicker picker) => picker.SelectedDate is { } date ? DateOnly.FromDateTime(date) : null;
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is T value) yield return value; foreach (var item in Descendants<T>(child)) yield return item; }
    }
}
