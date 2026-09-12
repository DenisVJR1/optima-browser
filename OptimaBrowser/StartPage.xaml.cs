using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OptimaBrowser;

public sealed class RequestEventArgs : EventArgs
{
    public string? Url { get; init; }
    public bool NewTab { get; init; }
}

public partial class StartPage : UserControl
{
    public event EventHandler<RequestEventArgs>? Requested;
    public event EventHandler? AchievementsClicked;
    public event EventHandler<string>? EnginePicked;
    public event EventHandler? EngineCycled;

    private readonly DispatcherTimer _clock;
    private static readonly string[] Tips =
    {
        "Ctrl+L — одразу в адресний рядок. Спробуй, буде швидше.",
        "Ctrl+T нова вкладка, Ctrl+W закрити, Ctrl+Tab перемикання.",
        "Ctrl+Shift+R — режим читання прямо з сайту.",
        "Контекстне меню ⋮ вміє скріншот, PDF і переклад.",
        "Середня кнопка миші по вкладці закриває її.",
        "Alt+← / Alt+→ — назад/вперед, як у великих.",
        "Ctrl+D — закладка на сторінку.",
        "Ctrl+±/− — масштаб, Ctrl+0 — скинути.",
        "Пасхалка: Konami-код ↑↑↓↓←→←→BA дає золотий дощ.",
        "Адресний рядок любить «котик» — спробуй.",
        "Меню ⋮ → Перекласти сторінку знає 12 мов — і запам'ятовує вибір.",
        "Клік по RAM у статус-барі — миттєве прибирання пам'яті.",
        "Backspace — назад, Ctrl+Enter — сайт .com, Alt+Enter — нова вкладка.",
        "Ця порада змінюється щодня. Сьогодні — ця.",
    };

    public StartPage()
    {
        InitializeComponent();
        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => RenderClock();
        _clock.Start();
        RenderClock();
        BuildQuickLinks();
        IsVisibleChanged += (_, _) => { if (IsVisible) NtpSearch.Focus(); };
    }

    public void FocusSearch()
    {
        NtpSearch.Focus();
        NtpSearch.SelectAll();
    }

    public void ApplyContext(int unlocked, int total)
    {
        AchePillText.Text = $"🏆 Досягнень: {unlocked}/{total}";
        var now = DateTime.Now;
        NtpGreeting.Text = now.Hour switch
        {
            < 5 or >= 23 => "Доброї ночі! Працюєш, поки всі сплять — похвально.",
            < 12 => "Доброго ранку! Кава вже зроблена.",
            < 18 => "Доброго дня! Час великих справ.",
            _ => "Доброго вечора! Оптимальний час для всього.",
        };
        NtpTip.Text = "Порада дня: " + Tips[(now.DayOfYear * 7 + now.Hour) % Tips.Length];
    }

    public void LoadRecent(IEnumerable<(string Host, string Url)> recent)
    {
        RecentRow.Children.Clear();
        var items = recent.ToList();
        RecentLabel.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (host, url) in items)
        {
            var tag = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(11, 6, 11, 6),
                Margin = new Thickness(0, 0, 8, 6),
                Cursor = Cursors.Hand,
                ToolTip = url,
            };
            tag.MouseLeftButtonUp += (_, _) =>
                Requested?.Invoke(this, new RequestEventArgs { Url = url });
            var inner = new StackPanel { Orientation = Orientation.Horizontal };
            inner.Children.Add(new TextBlock
            {
                Text = "🌐",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 7, 0),
            });
            inner.Children.Add(new TextBlock
            {
                Text = host,
                FontSize = 12.5,
                Foreground = (Brush)FindResource("BrushTextHi"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            tag.Child = inner;
            RecentRow.Children.Add(tag);
        }
    }

    public void SetEngines(IEnumerable<(string Id, string Label, string Icon, bool Current)> engines)
    {
        EnginesRow.Children.Clear();
        try
        {
            var all = engines.ToList();
            var cur = all.FirstOrDefault(e => e.Current);
            foreach (var (id, label, icon, current) in all)
            {
                var img = new Image
                {
                    Source = LoadPng(icon),
                    Width = current ? 22 : 18,
                    Height = current ? 22 : 18,
                    Stretch = Stretch.Uniform,
                };
                var btn = new Border
                {
                    Width = 34,
                    Height = 34,
                    CornerRadius = new CornerRadius(10),
                    Margin = new Thickness(2, 0, 2, 4),
                    Cursor = Cursors.Hand,
                    ToolTip = label,
                    Background = current
                        ? new SolidColorBrush(Color.FromArgb(60, 255, 255, 255))
                        : new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
                    BorderBrush = current
                        ? (Brush)FindResource("BrushStrokeStrong")
                        : new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                };
                btn.Child = img;
                btn.MouseLeftButtonUp += (_, _) => EnginePicked?.Invoke(this, id);
                EnginesRow.Children.Add(btn);
            }
            if (cur.Id != null) EngineMark.Source = LoadPng(cur.Icon);
        }
        catch { }
        EnginesRow.Visibility = Visibility.Visible;
    }

    private static BitmapImage LoadPng(string uri)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(uri);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private void EngineMark_Click(object sender, MouseButtonEventArgs e)
        => EngineCycled?.Invoke(this, EventArgs.Empty);

    private void NtpLogo_Click(object sender, MouseButtonEventArgs e)
    {
        var el = (FrameworkElement)sender;
        var rt = new RotateTransform();
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.RenderTransform = rt;
        rt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(700)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void AchePill_Click(object sender, MouseButtonEventArgs e)
        => AchievementsClicked?.Invoke(this, EventArgs.Empty);

    private void RenderClock()
    {
        var now = DateTime.Now;
        NtpClock.Text = now.ToString("HH:mm");
        var uk = new CultureInfo("uk-UA");
        string d = now.ToString("dddd, d MMMM yyyy", uk);
        if (d.Length > 0) d = char.ToUpperInvariant(d[0]) + d[1..];
        NtpDate.Text = d;
    }

    private static readonly (string Name, string Url, Color A, Color B)[] Quick =
    {
        ("GitHub",        "https://github.com",             Color.FromRgb(0x35, 0xE2, 0xD0), Color.FromRgb(0x1B, 0x6E, 0x66)),
        ("YouTube",       "https://www.youtube.com",         Color.FromRgb(0x8B, 0x7C, 0xFF), Color.FromRgb(0x4A, 0x3F, 0x9E)),
        ("Wikipedia",     "https://uk.wikipedia.org",        Color.FromRgb(0x43, 0xE0, 0xA0), Color.FromRgb(0x1F, 0x7A, 0x55)),
        ("Hacker News",   "https://news.ycombinator.com",    Color.FromRgb(0xF2, 0xA0, 0x4A), Color.FromRgb(0x8A, 0x4F, 0x1B)),
        ("MDN",           "https://developer.mozilla.org",   Color.FromRgb(0x35, 0xE2, 0xD0), Color.FromRgb(0x8B, 0x7C, 0xFF)),
        ("DuckDuckGo",    "https://duckduckgo.com",          Color.FromRgb(0x43, 0xE0, 0xA0), Color.FromRgb(0x35, 0xE2, 0xD0)),
        ("Google",        "https://www.google.com",          Color.FromRgb(0x8B, 0x7C, 0xFF), Color.FromRgb(0x6B, 0x5C, 0xE0)),
        ("Bing",          "https://www.bing.com",            Color.FromRgb(0xF2, 0xA0, 0x4A), Color.FromRgb(0x43, 0xE0, 0xA0)),
    };

    private static readonly Dictionary<string, string> BrandIcons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GitHub"] = "github", ["YouTube"] = "youtube", ["Wikipedia"] = "wikipedia",
        ["Hacker News"] = "hackernews", ["MDN"] = "mdn", ["DuckDuckGo"] = "duckduckgo",
        ["Google"] = "google", ["Bing"] = "bing",
    };

    private void BuildQuickLinks()
    {
        foreach (var (name, url, a, b) in Quick)
        {
            var tile = new Border
            {
                Width = 56,
                Height = 56,
                CornerRadius = new CornerRadius(16),
                Margin = new Thickness(10, 4, 10, 0),
                Background = new LinearGradientBrush(a, b, new Point(0, 0), new Point(1, 1)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                BorderThickness = new Thickness(1),
            };

            var grid = new Grid();
            grid.Children.Add(new TextBlock
            {
                Text = name[..Math.Min(2, name.Length)].ToUpperInvariant(),
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });

            if (BrandIcons.TryGetValue(name, out var slug))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri($"pack://application:,,,/OptimaBrowser;component/Assets/brands/{slug}.ico");
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    var img = new Image
                    {
                        Width = 34,
                        Height = 34,
                        Stretch = Stretch.Uniform,
                        IsHitTestVisible = false,
                        Source = bmp,
                    };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                    grid.Children.Add(img);
                }
                catch { }
            }
            tile.Child = grid;

            var label = new TextBlock
            {
                Text = name,
                FontSize = 11.5,
                Foreground = (Brush)FindResource("BrushTextMid"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0),
            };

            var cell = new StackPanel { Margin = new Thickness(2, 0, 2, 6), Cursor = Cursors.Hand };
            cell.Children.Add(tile);
            cell.Children.Add(label);
            cell.ToolTip = url;
            cell.MouseLeftButtonUp += (_, _) =>
                Requested?.Invoke(this, new RequestEventArgs { Url = url, NewTab = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) });
            QuickGrid.Children.Add(cell);
        }
    }

    private void NtpSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            Requested?.Invoke(this, new RequestEventArgs { Url = NtpSearch.Text });
    }
}