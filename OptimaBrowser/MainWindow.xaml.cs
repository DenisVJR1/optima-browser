using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using Path = System.IO.Path;

namespace OptimaBrowser;

public sealed class TabVM : System.ComponentModel.INotifyPropertyChanged
{
    public string Id { get; } = Guid.NewGuid().ToString("N");

    private string _title = "Нова вкладка";
    public string Title
    {
        get => _title;
        set { if (_title != value) { _title = value; Changed(nameof(Title)); } }
    }

    private string _icon = "";
    public string Icon
    {
        get => _icon;
        set { if (_icon != value) { _icon = value; Changed(nameof(Icon)); } }
    }

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive != value) { _isActive = value; Changed(nameof(IsActive)); } }
    }

    public string? Url { get; set; }
    public bool IsPrivate { get; set; }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string n) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n));
}

public partial class MainWindow : Window
{
    private enum VaultMode { Unlock, Setup, Change, Disable }

    // ---------- themes ----------
    private sealed record ThemeDef(string Id, string Label, uint BgTop, uint BgBot, uint Acc1, uint Acc2, uint Acc3);
    private static readonly ThemeDef[] Themes =
    {
        new("glass",  "Glass — темний",    0x121827, 0x070A13, 0x35E2D0, 0x43E0A0, 0x8B7CFF),
        new("ocean",  "🌊 Океан — синій",   0x0B1226, 0x04060F, 0x59D5FF, 0x3DD6C4, 0x8E7BFF),
        new("ember",  "🔥 Полум'я — теплий", 0x24110A, 0x0D0605, 0xFF8A4B, 0xFFC24B, 0xFF7B9C),
        new("forest", "🌲 Ліс — зелений",   0x0A1612, 0x040B08, 0x69F0AE, 0x35E2D0, 0x8B7CFF),
    };
    private bool _acrylic;

    // ---------- search engines ----------
    private sealed record EngineDef(string Id, string Label, string Url, string Icon);
    private static readonly EngineDef[] Engines =
    {
        new("bing",      "Bing",            "https://www.bing.com/search?q=",                "pack://application:,,,/OptimaBrowser;component/Assets/png/bing.png"),
        new("google",    "Google",          "https://www.google.com/search?q=",              "pack://application:,,,/OptimaBrowser;component/Assets/png/google.png"),
        new("ddg",       "DuckDuckGo",      "https://duckduckgo.com/?q=",                    "pack://application:,,,/OptimaBrowser;component/Assets/png/ddg.png"),
        new("ecosia",    "Ecosia",          "https://www.ecosia.org/search?q=",              "pack://application:,,,/OptimaBrowser;component/Assets/png/ecosia.png"),
        new("brave",     "Brave Search",    "https://search.brave.com/search?q=",            "pack://application:,,,/OptimaBrowser;component/Assets/png/brave.png"),
        new("qwant",     "Qwant",           "https://www.qwant.com/?q=",                     "pack://application:,,,/OptimaBrowser;component/Assets/png/qwant.png"),
        new("startpage", "Startpage",       "https://www.startpage.com/sp/search?query=",    "pack://application:,,,/OptimaBrowser;component/Assets/png/startpage.png"),
        new("yahoo",     "Yahoo",           "https://search.yahoo.com/search?p=",            "pack://application:,,,/OptimaBrowser;component/Assets/png/yahoo.png"),
        new("mojeek",    "Mojeek",          "https://www.mojeek.com/search?q=",              "pack://application:,,,/OptimaBrowser;component/Assets/png/mojeek.png"),
        new("ukwiki",    "Вікіпедія (укр)", "https://uk.wikipedia.org/w/index.php?search=", "pack://application:,,,/OptimaBrowser;component/Assets/png/ukwiki.png"),
    };

    // ---------- переклад: 12 мов ----------
    private static readonly (string Code, string Label)[] TLangs =
    {
        ("uk", "Українською 🇺🇦"), ("en", "Англійською 🇬🇧"), ("de", "Німецькою 🇩🇪"),
        ("fr", "Французькою 🇫🇷"), ("es", "Іспанською 🇪🇸"), ("it", "Італійською 🇮🇹"),
        ("pl", "Польською 🇵🇱"), ("pt", "Португальською 🇵🇹"), ("nl", "Нідерландською 🇳🇱"),
        ("cs", "Чеською 🇨🇿"), ("tr", "Турецькою 🇹🇷"), ("zh-CN", "Китайською 🇨🇳"),
    };

    // ---------- state ----------
    private readonly List<TabVM> _tabs = new();
    private int _activeIdx = -1;
    private bool _coreReady;
    private bool _suspended;
    private string? _pendingNav;
    private TabVM? _pendingTab;
    private string? _pendingHtml;
    private Task? _initTask;

    private readonly Achievements _ach;
    private readonly DispatcherTimer _achToastTimer = new() { Interval = TimeSpan.FromSeconds(2.8) };

    private readonly List<HistEntry> _history = new();
    private readonly Dictionary<string, string> _bookmarks = new();
    private readonly List<DlItem> _downloads = new();
    private readonly Dictionary<string, long> _upgradedHttp = new();
    private readonly DispatcherTimer _suggestTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _metricsTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _dirty;
    private bool _uiSyncing;

    private Settings _settings = new();
    private long _blockedCount;
    private int _zoomPct = 100;
    private int _readerSize = 16;
    private string _drawerMode = "bookmarks";

    // ---------- нові фішки ----------
    private readonly Stack<string> _closedTabs = new();
    private int _findIdx;
    private bool _fullscreen;
    private static readonly (string P, string Url)[] Quick =
    {
        ("g ",   "https://www.google.com/search?q="),
        ("w ",   "https://uk.wikipedia.org/w/index.php?search="),
        ("ddg ", "https://duckduckgo.com/?q="),
        ("y ",   "https://www.youtube.com/results?search_query="),
        ("gh ",  "https://github.com/search?q="),
        ("m ",   "https://www.google.com/maps/search/"),
        ("e ",   "https://www.ecosia.org/search?q="),
        ("b ",   "https://www.bing.com/search?q="),
    };

    private const string FindJs = @"(function(q,idx){
var st=document.getElementById('optima-find-style');
if(!st){st=document.createElement('style');st.id='optima-find-style';st.textContent='optima-mark{background:#35e2d0;color:#0d1412;border-radius:2px;padding:0 1px}optima-mark.cur{background:#ffd54a;color:#0d1412}';document.head.appendChild(st);}
var ms=document.querySelectorAll('optima-mark');
for(var i=0;i<ms.length;i++){var t=document.createTextNode(ms[i].textContent||'');ms[i].replaceWith(t);}
if(!q)return JSON.stringify({n:0,i:0});
var re;try{re=new RegExp(q.replace(/[.*+?^${}()|[\]\\]/g,'$&'),'gi');}catch(err){return JSON.stringify({n:0,i:0});}
var w=document.createTreeWalker(document.body,NodeFilter.SHOW_TEXT);
var c=0;
while(w.nextNode()){var node=w.currentNode;
  if(!node.nodeValue||!node.parentElement||!node.nodeValue.trim())continue;
  var s=node.nodeValue;var m;var last=0;var parts=[];
  while((m=re.exec(s))!==null){parts.push(s.slice(last,m.index),m[0]);last=m.index+m[0].length;}
  if(parts.length>1){parts.push(s.slice(last));
    var frag=document.createDocumentFragment();
    for(var k=0;k<parts.length;k++){if(k%2===1){var mark=document.createElement('optima-mark');mark.textContent=parts[k];frag.appendChild(mark);c++;}else if(parts[k])frag.appendChild(document.createTextNode(parts[k]));}
    node.parentElement.replaceChild(frag,node);}}
var all=document.querySelectorAll('optima-mark');
if(all.length===0)return JSON.stringify({n:0,i:0});
var idx2=((idx%all.length)+all.length)%all.length;
all[idx2].classList.add('cur');
try{all[idx2].scrollIntoView({block:'center'});}catch(err){}
return JSON.stringify({n:all.length,i:idx2});
})";

    // ---------- vault ----------
    private string? _vaultPass;
    private bool _vaultActive;
    private bool _locked;
    private VaultMode _vaultMode;
    private string[] _sessUrls = Array.Empty<string>();
    private bool[] _sessPriv = Array.Empty<bool>();

    // ---------- easter eggs ----------
    private readonly Queue<Key> _konami = new();
    private readonly Random _rnd = new();
    private readonly List<FX> _fx = new();
    private readonly DispatcherTimer _fxTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private bool _eggBlock100, _eggBlock1000;
    private static readonly Key[] KonamiSeq =
        { Key.Up, Key.Up, Key.Down, Key.Down, Key.Left, Key.Right, Key.Left, Key.Right, Key.B, Key.A };

    private sealed class FX { public FrameworkElement El = null!; public double Vx, Vy; }

    private static readonly HashSet<string> BlockedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "doubleclick.net", "googlesyndication.com", "googleadservices.com", "googletagmanager.com",
        "googletagservices.com", "google-analytics.com", "adservice.google.com", "analytics.google.com",
        "taboola.com", "outbrain.com", "criteo.com", "adzerk.net", "moatads.com", "adsrvr.org",
        "adnxs.com", "rubiconproject.com", "openx.net", "pubmatic.com", "bidswitch.net",
        "smartadserver.com", "adform.net", "adroll.com", "quantserve.com", "scorecardresearch.com",
        "chartbeat.com", "demdex.net", "bluekai.com", "krxd.net", "agkn.com", "1rx.io",
        "popads.net", "propellerads.com", "adthrive.com", "mediavine.com", "ezoic.net",
        "mgid.com", "mc.yandex.", "metrika.yandex.", "an.yandex.ru", "adfox.ru", "adskeeper.com",
        "exoclick.com", "trafficjunky.net", "zedo.com", "mathtag.com", "mmstat.com",
        "facebook.net", "hotjar.com", "mixpanel.com", "segment.io", "amplitude.com", "branch.io",
        "intercom.io", "crazyegg.com", "luckyorange.com", "fullstory.com", "mouseflow.com",
        "snap.licdn.com", "liadm.com", "sentry.io",
        "adsafeprotected.com", "carbonads.com", "buysellads.com", "revcontent.com", "media.net",
        "yieldmo.com", "spotxchange.com", "springserve.com", "sonobi.com", "gumgum.com",
        "33across.com", "undertone.com", "districtm.io", "triplelift.com", "teads.tv",
        "comscore.com", "imrworldwide.com", "stackadapt.com", "sharethrough.com", "lotame.com",
        "doubleverify.com", "integralads.com", "audienceinsights.net", "polymorphads.com",
        "cpmstar.com", "adventive.com", "content.ad", "advertising.com", "unruly.co",
        "pixel.facebook.com", "connect.facebook.net", "ads-twitter.com", "static.criteo.net",
        "bidsxchange.com", "ad-maven.com", "skimresources.com", "aniview.com", "dsp.io",
    };

    private string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OptimaBrowser");
    private string HistoryFile => Path.Combine(DataDir, "history.json");
    private string BookmarkFile => Path.Combine(DataDir, "bookmarks.json");
    private string SessionFile => Path.Combine(DataDir, "session.json");
    private string BoundsFile => Path.Combine(DataDir, "window.json");
    private string SettingsFile => Path.Combine(DataDir, "settings.json");
    private string VaultFile => Path.Combine(DataDir, "vault.obx");
    private string CoreDataDir => Path.Combine(DataDir, "WebView2");

    private sealed record HistEntry(string Url, string Title, long T);
    private sealed record SuggItem(string Glyph, string Title, string Sub, string Url);
    private sealed record SessionState(string[] Tabs, bool[] Private);
    private sealed record Bounds(double Left, double Top, double Width, double Height, bool Max);
    private sealed class Settings
    {
        public string Search { get; set; } = "bing";
        public bool AdBlock { get; set; } = true;
        public bool ForceDark { get; set; } = false;
        public int Zoom { get; set; } = 100;
        public string Theme { get; set; } = "glass";
        public string Home { get; set; } = "";
        public bool ForceHttps { get; set; } = true;
        public bool SaveHistory { get; set; } = true;
        public bool SaveSession { get; set; } = true;
        public bool StartRecent { get; set; } = true;
        public bool Animations { get; set; } = true;
        public bool ShowStatus { get; set; } = true;
        public string TranslateLang { get; set; } = "uk";
    }
    private sealed record DrawerItem(string Glyph, string Title, string Sub, string Url);
    private sealed record VaultPayload(List<HistEntry> History, Dictionary<string, string> Bookmarks, string[] SessUrls, bool[] SessPriv);
    private sealed record DlItem(string Name, string Url, string Store);

    private const string AboutHtml = """
        <!doctype html><html><head><meta charset="utf-8"><title>Про Optima Browser</title><style>
        body{margin:0;font-family:'Segoe UI',system-ui,sans-serif;background:linear-gradient(160deg,#121827,#0a0d16 60%,#070a13);color:#edf1f7;display:flex;align-items:center;justify-content:center;min-height:100vh}
        .card{max-width:640px;padding:48px;border:1px solid rgba(255,255,255,.14);border-radius:24px;background:rgba(255,255,255,.05);box-shadow:0 30px 80px rgba(0,0,0,.5)}
        .logo{width:64px;height:64px;border-radius:18px;background:linear-gradient(135deg,#35e2d0,#43e0a0,#8b7cff);display:flex;align-items:center;justify-content:center;font-size:34px;font-weight:800;color:#0a1412;margin-bottom:18px}
        h1{margin:0 0 6px;font-size:26px}.s{color:#9da6bb;font-size:13px;margin-bottom:24px}
        ul{color:#c7cdda;font-size:14px;line-height:1.9;padding-left:18px}
        .hint{color:#5c6577;font-size:12px;margin-top:24px;border-top:1px solid rgba(255,255,255,.1);padding-top:16px}
        b{color:#35e2d0}
        </style></head><body><div class="card">
        <div class="logo">O</div>
        <h1>Optima Browser</h1>
        <div class="s">версія 1.7.1 · рідке скло · один рушій · Optima Vault · досягнення · 4 теми · примусовий HTTPS</div>
        <ul>
        <li>Єдиний WebView2-рушій на всі вкладки, lazy-старт, suspend у фоновому режимі</li>
        <li>Блокування реклами й трекерів на рівні рушія</li>
        <li>Optima Vault — власне шифрування даних AES-256-GCM</li>
        <li>Примусовий HTTPS: http:// автоматично піднімається до https:// — реальний TLS</li>
        <li>Завантаження з Ctrl+J, менеджер завантажень у папці Downloads, імена без перезапису</li>
        <li>Пошук на сторінці (Ctrl+F), повний екран (F11), мут звуку, калькулятор у адресному рядку</li>
        <li>Швидкий пошук префіксами: «g запит», «w запит», «y запит», «gh запит»…</li>
        <li>Переклад сторінок — 12 мов (меню ⋮ → Перекласти сторінку), вибір запам'ятовується</li>
        <li>Очищення даних браузера (Ctrl+Shift+Delete), Ctrl+Shift+T — відновити закриту вкладку</li>
        <li>30 досягнень, 4 теми, режим читання, скріншот, PDF</li>
        </ul>
        <div class="hint">Пасхалки: ↑↑↓↓←→←→BA, 42, «слава україні», «котик», optima:about, 13 вкладок, подвійний клік по логотипу. Гарячі: Ctrl+F пошук · Ctrl+H історія · Ctrl+J завантаження · Ctrl+L адресний рядок · F11 повний екран · Ctrl+Shift+T закрита вкладка · Ctrl+Shift+Delete очистити дані.</div>
        <div class="hint">Пасхалки: ↑↑↓↓←→←→BA, 42, «слава україні», «котик», optima:about, 13 вкладок, подвійний клік по логотипу. Гарячі: Ctrl+F пошук · Ctrl+H історія · Ctrl+J завантаження · Ctrl+L адресний рядок · F11 повний екран · Ctrl+Shift+T закрита вкладка · Ctrl+Shift+Delete очистити дані.</div>
        </div></body></html>
        """;

    // ---------- lifecycle ----------

    public MainWindow()
    {
        InitializeComponent();
        _ach = new Achievements(Path.Combine(DataDir, "achievements.json"));
        _ach.OnUnlocked += ShowAchievementToast;
        _achToastTimer.Tick += (_, _) =>
        {
            _achToastTimer.Stop();
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400));
            fade.Completed += (_, _) => AchToast.Visibility = Visibility.Collapsed;
            AchToast.BeginAnimation(OpacityProperty, fade);
        };
        Start.AchievementsClicked += (_, _) => OpenAchievements();
        Start.EnginePicked += (_, id) => SetEngine(id);
        Start.EngineCycled += (_, _) =>
        {
            int i = Array.FindIndex(Engines, x => x.Id == _settings.Search);
            SetEngine(Engines[(i + 1) % Engines.Length].Id);
        };
        _suggestTimer.Tick += (_, _) => { _suggestTimer.Stop(); BuildSuggestions(); };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); FlushPersist(); };
        _metricsTimer.Tick += (_, _) => SampleMetrics();
        _fxTimer.Tick += FxTick;
        StateChanged += (_, _) => MaxGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        SizeChanged += (_, _) => RefreshSuggPos();
        SearchCmb.ItemsSource = Engines;
        ThemeCmb.ItemsSource = Themes.Select(t => t.Label).ToList();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        TryAcrylic();
        LoadPersist();
        RefreshEngineMark();
        ApplyTheme(_settings.Theme);
        RestoreSession();
        if (_tabs.Count == 0) NewTab();
        ApplyZoom();
        if (_ach.WasFresh) _ach.Unlock("first_launch");
        if (_vaultActive && _vaultPass == null)
        {
            _locked = true;
            ShowStart();
            OpenVault(VaultMode.Unlock);
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _metricsTimer.Stop();
        _fxTimer.Stop();
        _achToastTimer.Stop();
        FlushPersist();
        ApplySessionSave();
        try { Core.Dispose(); } catch { }
    }

    // ---------- title bar drag (кнопки вікна не перехоплюються жодним chrome) ----------

    private void TitleBar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2) { e.Handled = true; MaxBtn_Click(this, new RoutedEventArgs()); return; }
        if (e.OriginalSource is DependencyObject d)
        {
            if (FindParent<ButtonBase>(d) != null) return;                        // віконні кнопки самі обробляють
            if (FindParent<Border>(d) is Border pb && pb.DataContext is TabVM) return; // клік по вкладці
            if (d is StackPanel || FindParent<StackPanel>(d) != null) return;     // brand/плитки
        }
        DragMove();
    }

    private static T? FindParent<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null) { if (d is T t) return t; d = VisualTreeHelper.GetParent(d); }
        return null;
    }

    // ---------- real liquid glass (Windows 11 native acrylic via DWM) ----------

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    private void TryAcrylic()
    {
        try
        {
            if (Environment.OSVersion.Version.Build < 22000) return; // акрил — Windows 11
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int val = 3; // DWMSBT_TRANSIENTWINDOW
            if (DwmSetWindowAttribute(hwnd, 38, ref val, sizeof(int)) == 0)
            {
                _acrylic = true;
                Background = Brushes.Transparent;
            }
        }
        catch { }
    }

    // ---------- themes ----------

    private static Color Col(uint rgb, byte a = 0xFF) => Color.FromArgb(a, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private void ApplyTheme(string id)
    {
        try
        {
            var t = Themes.First(x => x.Id == id);
            byte alpha = _acrylic ? (byte)0xD0 : (byte)0xFF;
            Root.Background = new LinearGradientBrush(Col(t.BgTop, alpha), Col(t.BgBot, alpha), new Point(0, 0), new Point(0, 1));
            if (FindResource("BrushAccentCyan") is SolidColorBrush c1) c1.Color = Col(t.Acc1);
            if (FindResource("BrushAccentEmerald") is SolidColorBrush c2) c2.Color = Col(t.Acc2);
            if (FindResource("BrushAccentViolet") is SolidColorBrush c3) c3.Color = Col(t.Acc3);
            if (FindResource("BrushAccentGradient") is LinearGradientBrush g)
            {
                g.GradientStops.Clear();
                g.GradientStops.Add(new GradientStop(Col(t.Acc1), 0));
                g.GradientStops.Add(new GradientStop(Col(t.Acc2), 0.55));
                g.GradientStops.Add(new GradientStop(Col(t.Acc3), 1));
            }
            if (B1.Fill is RadialGradientBrush b1 && b1.GradientStops.Count > 0) b1.GradientStops[0].Color = Col(t.Acc1, 0x2E);
            if (B2.Fill is RadialGradientBrush b2 && b2.GradientStops.Count > 0) b2.GradientStops[0].Color = Col(t.Acc3, 0x34);
            if (B3.Fill is RadialGradientBrush b3 && b3.GradientStops.Count > 0) b3.GradientStops[0].Color = Col(t.Acc2, 0x24);
        }
        catch { }
    }

    private void ThemeCmb_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_uiSyncing || ThemeCmb.SelectedIndex < 0) return;
        _settings.Theme = Themes[ThemeCmb.SelectedIndex].Id;
        SaveSettings();
        ApplyTheme(_settings.Theme);
    }

    // ---------- search engine switch ----------

    private void SetEngine(string id)
    {
        var eng = Array.Find(Engines, x => x.Id == id);
        if (eng == null) return;
        _settings.Search = eng.Id;
        SaveSettings();
        RefreshEngineMark();
        Start.SetEngines(Engines.Select(x => (x.Id, x.Label, x.Icon, x.Id == eng.Id)));
        StatusC($"Пошуковик: {eng.Label}");
    }

    private void RefreshEngineMark()
    {
        var eng = Array.Find(Engines, x => x.Id == _settings.Search) ?? Engines[0];
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit(); bmp.UriSource = new Uri(eng.Icon); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.EndInit(); bmp.Freeze();
            EngineMark.Source = bmp;
        }
        catch { }
    }

    private void EngineMark_Click(object sender, MouseButtonEventArgs e)
    {
        int i = Array.FindIndex(Engines, x => x.Id == _settings.Search);
        SetEngine(Engines[(i + 1) % Engines.Length].Id);
    }

    private static Image Png(string name)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri($"pack://application:,,,/OptimaBrowser;component/Assets/png/{name}.png");
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            var img = new Image { Source = bmp, Width = 20, Height = 20, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            return img;
        }
        catch { return new Image { Width = 20, Height = 20 }; }
    }

    // ---------- persistence ----------

    private void LoadPersist()
    {
        try
        {
            if (File.Exists(SettingsFile))
                _settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsFile)) ?? new Settings();
            _zoomPct = Math.Clamp(_settings.Zoom, 30, 300);
            _vaultActive = File.Exists(VaultFile);
            if (!_vaultActive)
            {
                if (File.Exists(HistoryFile))
                {
                    var h = JsonSerializer.Deserialize<List<HistEntry>>(File.ReadAllText(HistoryFile));
                    if (h != null) { _history.Clear(); _history.AddRange(h); }
                }
                if (File.Exists(BookmarkFile))
                {
                    var b = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(BookmarkFile));
                    if (b != null) foreach (var kv in b) _bookmarks[kv.Key] = kv.Value;
                }
            }
        }
        catch { }
        RefreshSettingsUi();
        UpdateShieldUi();
    }

    private void SaveSettings()
    {
        try { Directory.CreateDirectory(DataDir); File.WriteAllText(SettingsFile, JsonSerializer.Serialize(_settings)); } catch { }
    }

    private void MarkDirty() { _dirty = true; _saveTimer.Stop(); _saveTimer.Start(); }

    private void FlushPersist()
    {
        if (!_dirty) return;
        _dirty = false;
        try
        {
            Directory.CreateDirectory(DataDir);
            if (_vaultActive) { if (_vaultPass == null) return; WriteVault(); }
            else
            {
                File.WriteAllText(HistoryFile, JsonSerializer.Serialize(_history.Take(400).ToList()));
                File.WriteAllText(BookmarkFile, JsonSerializer.Serialize(_bookmarks));
            }
        }
        catch { }
    }

    private void WriteVault()
    {
        var payload = new VaultPayload(_history.Take(400).ToList(), new Dictionary<string, string>(_bookmarks), _sessUrls, _sessPriv);
        File.WriteAllBytes(VaultFile, Vault.Encrypt(JsonSerializer.SerializeToUtf8Bytes(payload), _vaultPass!));
    }

    private void RestoreSession()
    {
        try
        {
            if (File.Exists(BoundsFile))
            {
                var b = JsonSerializer.Deserialize<Bounds>(File.ReadAllText(BoundsFile));
                if (b != null) { if (b.Max) WindowState = WindowState.Maximized; else if (b.Width > 200 && b.Height > 200) { Left = b.Left; Top = b.Top; Width = b.Width; Height = b.Height; } }
            }
            if (_vaultActive || !_settings.SaveSession || !File.Exists(SessionFile)) return;
            var s = JsonSerializer.Deserialize<SessionState>(File.ReadAllText(SessionFile));
            if (s == null) return;
            int i = 0;
            foreach (var url in s.Tabs) { if (string.IsNullOrEmpty(url)) { i++; continue; } NewTab(url, s.Private != null && i < s.Private.Length && s.Private[i]); i++; }
        }
        catch { }
    }

    private void CaptureSession()
    {
        _sessUrls = _tabs.Where(t => !string.IsNullOrEmpty(t.Url)).Select(t => t.Url!).ToArray();
        _sessPriv = _tabs.Where(t => !string.IsNullOrEmpty(t.Url)).Select(t => t.IsPrivate).ToArray();
    }

    private void ApplySessionSave()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(BoundsFile, JsonSerializer.Serialize(new Bounds(Left, Top, Width, Height, WindowState == WindowState.Maximized)));
            if (_vaultActive) { if (_vaultPass != null) { CaptureSession(); WriteVault(); } return; }
            if (_settings.SaveSession)
            {
                CaptureSession();
                File.WriteAllText(SessionFile, JsonSerializer.Serialize(new SessionState(_sessUrls, _sessPriv)));
            }
        }
        catch { }
    }

    // ---------- vault ----------

    private bool TryUnlockVault(string pass)
    {
        try
        {
            var data = Vault.Decrypt(File.ReadAllBytes(VaultFile), pass);
            var payload = JsonSerializer.Deserialize<VaultPayload>(data);
            _history.Clear(); _bookmarks.Clear();
            if (payload != null) { if (payload.History != null) _history.AddRange(payload.History); if (payload.Bookmarks != null) foreach (var kv in payload.Bookmarks) _bookmarks[kv.Key] = kv.Value; _sessUrls = payload.SessUrls ?? Array.Empty<string>(); _sessPriv = payload.SessPriv ?? Array.Empty<bool>(); }
            _vaultPass = pass; _locked = false;
            for (int i = 0; i < _sessUrls.Length; i++) if (!string.IsNullOrEmpty(_sessUrls[i])) NewTab(_sessUrls[i], i < _sessPriv.Length && _sessPriv[i]);
            return true;
        }
        catch { return false; }
    }

    private bool VerifyVaultPass(string pass) { try { Vault.Decrypt(File.ReadAllBytes(VaultFile), pass); return true; } catch { return false; } }

    private void EnableVault(string pass)
    {
        _vaultPass = pass; _vaultActive = true; _locked = false; CaptureSession(); WriteVault();
        try { File.Delete(HistoryFile); File.Delete(BookmarkFile); } catch { }
        _ach.Unlock("vault1"); UpdateStar();
    }

    private void ChangeVault(string oldPass, string newPass) { if (!VerifyVaultPass(oldPass)) return; _vaultPass = newPass; CaptureSession(); WriteVault(); }

    private void DisableVault(string pass)
    {
        var data = Vault.Decrypt(File.ReadAllBytes(VaultFile), pass);
        var payload = JsonSerializer.Deserialize<VaultPayload>(data);
        Directory.CreateDirectory(DataDir);
        File.WriteAllText(HistoryFile, JsonSerializer.Serialize(payload?.History ?? new List<HistEntry>()));
        File.WriteAllText(BookmarkFile, JsonSerializer.Serialize(payload?.Bookmarks ?? new Dictionary<string, string>()));
        try { File.Delete(VaultFile); } catch { }
        _vaultActive = false; _vaultPass = null; _locked = false;
    }

    private void LockVault() { CaptureSession(); _vaultPass = null; _locked = true; _history.Clear(); _bookmarks.Clear(); StatusC("Дані заблоковано — Vault закрито"); UpdateStar(); RefreshChrome(); }

    private void OpenVault(VaultMode mode)
    {
        _vaultMode = mode; VaultError.Text = ""; VaultCur.Clear(); VaultNew.Clear(); VaultNew2.Clear();
        bool needCur = mode != VaultMode.Setup; bool needNew = mode is VaultMode.Setup or VaultMode.Change;
        VaultCurWrap.Visibility = needCur ? Visibility.Visible : Visibility.Collapsed;
        VaultNewWrap.Visibility = needNew ? Visibility.Visible : Visibility.Collapsed;
        VaultNew2Wrap.Visibility = needNew ? Visibility.Visible : Visibility.Collapsed;
        switch (mode)
        {
            case VaultMode.Unlock: VaultTitle.Text = "Optima Vault"; VaultActionText.Text = "Розблокувати"; VaultCancelText.Text = "Пропустити"; VaultCurLabel.Text = "Пароль"; VaultMsg.Text = "Дані браузера зашифровані твоїм ключем. Введи майстер-пароль, щоб відкрити історію та закладки."; break;
            case VaultMode.Setup: VaultTitle.Text = "Увімкнути шифрування"; VaultActionText.Text = "Увімкнути"; VaultCancelText.Text = "Скасувати"; VaultNewLabel.Text = "Новий пароль (від 4 символів)"; VaultMsg.Text = "Історія, закладки й сесія шифруватимуться AES-256-GCM. Ключ виводиться з пароля (PBKDF2, 100 000 ітерацій). Забутий пароль — дані недоступні назавжди."; break;
            case VaultMode.Change: VaultTitle.Text = "Змінити пароль"; VaultActionText.Text = "Змінити"; VaultCancelText.Text = "Скасувати"; VaultCurLabel.Text = "Поточний пароль"; VaultMsg.Text = "Спочатку підтверди поточний пароль."; break;
            case VaultMode.Disable: VaultTitle.Text = "Вимкнути шифрування"; VaultActionText.Text = "Вимкнути"; VaultCancelText.Text = "Скасувати"; VaultCurLabel.Text = "Поточний пароль"; VaultMsg.Text = "Дані повернуться у відкриті JSON-файли. Підтверди поточний пароль."; break;
        }
        VaultOverlay.Visibility = Visibility.Visible; (needCur ? VaultCur : VaultNew).Focus();
    }

    private void VaultBox_PasswordChanged(object sender, RoutedEventArgs e) => VaultError.Text = "";
    private void VaultBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; VaultAction_Click(this, new RoutedEventArgs()); } }

    private void VaultAction_Click(object sender, RoutedEventArgs e)
    {
        switch (_vaultMode)
        {
            case VaultMode.Unlock: if (TryUnlockVault(VaultCur.Password)) { VaultOverlay.Visibility = Visibility.Collapsed; StatusC($"Vault розблоковано: {_bookmarks.Count} закладок, {_history.Count} записів"); RefreshChrome(); } else VaultError.Text = "Невірний пароль або файл пошкоджено."; break;
            case VaultMode.Setup: if (VaultNew.Password.Length < 4) { VaultError.Text = "Пароль надто короткий."; break; } if (VaultNew.Password != VaultNew2.Password) { VaultError.Text = "Паролі не збігаються."; break; } EnableVault(VaultNew.Password); VaultOverlay.Visibility = Visibility.Collapsed; StatusC("Optima Vault увімкнено — AES-256-GCM"); break;
            case VaultMode.Change: if (!VerifyVaultPass(VaultCur.Password)) { VaultError.Text = "Поточний пароль невірний."; break; } if (VaultNew.Password.Length < 4) { VaultError.Text = "Новий пароль закороткий."; break; } if (VaultNew.Password != VaultNew2.Password) { VaultError.Text = "Паролі не збігаються."; break; } ChangeVault(VaultCur.Password, VaultNew.Password); VaultOverlay.Visibility = Visibility.Collapsed; StatusC("Пароль Vault змінено"); break;
            case VaultMode.Disable: if (!VerifyVaultPass(VaultCur.Password)) { VaultError.Text = "Пароль невірний."; break; } DisableVault(VaultCur.Password); VaultOverlay.Visibility = Visibility.Collapsed; StatusC("Шифрування вимкнено"); break;
        }
        VaultCur.Clear(); VaultNew.Clear(); VaultNew2.Clear();
    }

    private void VaultCancel_Click(object sender, RoutedEventArgs e) { if (_vaultMode == VaultMode.Unlock) { _locked = true; StatusC("Vault не розблоковано — дані недоступні"); } VaultOverlay.Visibility = Visibility.Collapsed; }
    private void VaultOverlay_MouseDown(object sender, MouseButtonEventArgs e) { if (e.OriginalSource == VaultOverlay) VaultCancel_Click(this, new RoutedEventArgs()); }

    // ---------- achievements ----------

    private void ShowAchievementToast(Achievements.Def d)
    {
        AchToastEmoji.Text = d.Glyph; AchToastTitle.Text = d.Title;
        AchToast.Visibility = Visibility.Visible;
        AchToast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
        _achToastTimer.Stop(); _achToastTimer.Start();
    }

    private void OpenAchievements() { BuildAchievements(); AchOverlay.Visibility = Visibility.Visible; }
    private void AchOverlayClose_Click(object sender, RoutedEventArgs e) => AchOverlay.Visibility = Visibility.Collapsed;
    private void AchOverlay_MouseDown(object sender, MouseButtonEventArgs e) { if (e.OriginalSource == AchOverlay) AchOverlay.Visibility = Visibility.Collapsed; }

    private void BuildAchievements()
    {
        AchPanel.Children.Clear();
        AcheCountText.Text = $"{_ach.UnlockedCount} / {Achievements.All.Length} відкрито";
        var hi = (Brush)FindResource("BrushTextHi"); var low = (Brush)FindResource("BrushTextLow");
        foreach (var d in Achievements.All)
        {
            bool got = _ach.IsUnlocked(d.Id);
            var box = new Border { Width = 54, Height = 54, CornerRadius = new CornerRadius(16), HorizontalAlignment = HorizontalAlignment.Center,
                BorderBrush = got ? (Brush)FindResource("BrushStrokeStrong") : new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), BorderThickness = new Thickness(1) };
            box.Background = got ? (Brush)FindResource("BrushAccentGradient") : new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
            box.Child = new TextBlock { Text = got ? d.Glyph : "?", FontSize = 21, Foreground = got ? new SolidColorBrush(Color.FromArgb(235, 10, 20, 18)) : low, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var cell = new StackPanel { Width = 122, Margin = new Thickness(5, 6, 5, 10) };
            cell.Children.Add(box);
            cell.Children.Add(new TextBlock { Text = got ? d.Title : "???", FontSize = 11.5, FontWeight = got ? FontWeights.SemiBold : FontWeights.Normal, Foreground = got ? hi : low, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) });
            cell.Children.Add(new TextBlock { Text = got ? d.Desc : "Тримайся. Поки таємниця.", FontSize = 10, Foreground = low, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) });
            AchPanel.Children.Add(cell);
        }
    }

    // ---------- tabs ----------

    private TabVM? Active => _activeIdx >= 0 && _activeIdx < _tabs.Count ? _tabs[_activeIdx] : null;
    private bool IsOptimaTab => Active?.Url?.StartsWith("optima:", StringComparison.OrdinalIgnoreCase) == true;

    private void NewTab(string? url = null, bool isPrivate = false)
    {
        // домашня сторінка: якщо задана і відкриваємо чисту вкладку
        if (url == null && !isPrivate && _settings.Home.Length > 0) url = Normalize(_settings.Home);
        var tab = new TabVM { Url = url, IsPrivate = isPrivate };
        _tabs.Add(tab); ActivateTab(tab);
        _ach.Touch("tabs");
        _ach.TryUnlock("tabs10", _ach.Value("tabs") >= 10);
        _ach.TryUnlock("tabs25", _ach.Value("tabs") >= 25);
        if (isPrivate) _ach.Unlock("private1");
        if (_tabs.Count == 13 && !isPrivate) StatusC("13 вкладок. Щасливе число. Продовжуй.");
    }

    private void ActivateTab(TabVM tab)
    {
        _activeIdx = _tabs.IndexOf(tab); foreach (var t in _tabs) t.IsActive = t == tab; RefreshChrome();
        if (string.IsNullOrEmpty(tab.Url)) { ShowStart(); StatusC(tab.IsPrivate ? "Приватна вкладка" : "Готово"); }
        else { ShowWeb(); NavigateTo(tab, tab.Url!, addHistory: false); }
        UpdateStar();
    }

    private void CloseTab(TabVM tab)
    {
        int idx = _tabs.IndexOf(tab); if (idx < 0) return; _tabs.RemoveAt(idx);
        if (!string.IsNullOrEmpty(tab.Url) && !tab.IsPrivate && !tab.Url.StartsWith("optima:", StringComparison.OrdinalIgnoreCase))
        { _closedTabs.Push(tab.Url); while (_closedTabs.Count > 20) _closedTabs.Pop(); }
        if (_tabs.Count == 0) { NewTab(); return; }
        ActivateTab(_tabs[Math.Clamp(idx, 0, _tabs.Count - 1)]);
    }

    private void ReopenClosedTab()
    {
        if (_closedTabs.Count == 0) { StatusC("Немає закритих вкладок"); return; }
        NewTab(_closedTabs.Pop()); StatusC("Закриту вкладку відновлено (Ctrl+Shift+T)");
    }

    private void Tab_Click(object sender, MouseButtonEventArgs e) { if (sender is FrameworkElement fe && fe.DataContext is TabVM t && !t.IsActive) ActivateTab(t); }

    private void Tab_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && sender is FrameworkElement fe && fe.DataContext is TabVM t)
        { e.Handled = true; _ach.Unlock("middle1"); CloseTab(t); }
    }

    private void TabClose_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.Tag is TabVM t) CloseTab(t); }

    private void RefreshChrome() { TabsPill.Text = $"Вкладок: {_tabs.Count}"; if (Active != null && TabsHost.ItemContainerGenerator.ContainerFromItem(Active) is FrameworkElement c) c.BringIntoView(); }

    private void ShowStart()
    {
        Title = "Optima Browser";
        Core.Visibility = Visibility.Collapsed; Start.Visibility = Visibility.Visible;
        Start.ApplyContext(_ach.UnlockedCount, Achievements.All.Length);
        Start.LoadRecent(_settings.StartRecent ? _history.Take(6).Select(h => (TryHost(h.Url), h.Url)) : Enumerable.Empty<(string, string)>());
        var eng = Array.Find(Engines, x => x.Id == _settings.Search) ?? Engines[0];
        Start.SetEngines(Engines.Select(x => (x.Id, x.Label, x.Icon, x.Id == eng.Id)));
        Start.FocusSearch(); SuspendEngineAsync();
    }

    private void ShowWeb() { ResumeEngine(); Start.Visibility = Visibility.Collapsed; Core.Visibility = Visibility.Visible; }

    // ---------- engine suspend ----------

    private async void SuspendEngineAsync() { if (!_coreReady || _suspended || Core.Visibility != Visibility.Collapsed) return; try { _suspended = await Core.CoreWebView2!.TrySuspendAsync(); } catch { } }
    private void ResumeEngine() { if (!_coreReady || !_suspended) return; try { Core.CoreWebView2!.Resume(); _suspended = false; } catch { } }

    // ---------- navigation ----------

    private void NavigateActive(string raw)
    {
        var t = raw.Trim();
        switch (t.ToLowerInvariant())
        {
            case "42": StatusC("42 — відповідь на головне питання життя, Всесвіту та всього такого."); _ach.Unlock("a42"); return;
            case "44": StatusC("44 — удвічі більше відповідей. Ні? Тоді просто цифра."); return;
            case "69": StatusC("69. Тихо. Навіть статус-бар соромиться."); return;
            case "true": StatusC("true. Двійковий світ переміг."); return;
            case "false": StatusC("false. Але Enter ти все одно натиснув."); return;
            case "help" or "пасхалки" or "пасхалка" or "easteregg": StatusC("Пасхалки: Konami ↑↑↓↓←→←→BA, відповідь 42, «слава україні», optima:about, 13 вкладок, 100/1000 блоків, подвійний клік по лого, середня кнопка миші на вкладці. Фішки: Ctrl+F пошук, F11 повний екран, «g запит» — Google, «w» — Вікі, «y» — YouTube, «gh» — GitHub, «2+2» — калькулятор."); return;
            case "що": StatusC("Що? Сам задав питання — сам і відповідай."); return;
            case "слава україні" or "glory to ukraine" or "russian warship": TriggerFlagRain(); return;
            case "котик" or "кіт" or "кот" or "cat": SpawnEmojiRain(new[] { "🐱", "🐈", "🐾", "😻", "🐈⬛" }, 42, "🐱 Мур-мур. Котячий дощ — бо а що ще?"); return;
        }
        if (EvalExpr(t) is double calc) { StatusC($"{t} = {calc:0.########}"); _ach.Unlock("calc1"); return; }
        NavigateTo(Active!, t, addHistory: true);
    }

    // простий обчислювач у адресному рядку (як у Chrome)
    private static double? EvalExpr(string s)
    {
        s = s.Replace(" ", "").Replace(",", ".");
        if (s.Length < 3 || !System.Text.RegularExpressions.Regex.IsMatch(s, @"^[\d+\-*/().^%]+$")) return null;
        if (!s.Any(c => c is '+' or '-' or '*' or '/' or '^' or '%')) return null;
        var nums = new Stack<double>(); var ops = new Stack<char>();
        static int Prec(char c) => c is '+' or '-' ? 1 : c is '*' or '/' or '%' ? 2 : 3;
        static double Apply(double b, double a, char op) => op switch { '+' => a + b, '-' => a - b, '*' => a * b, '/' => a / b, '%' => a % b, _ => Math.Pow(a, b) };
        try
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsDigit(c) || c == '.')
                {
                    int j = i; while (j < s.Length && (char.IsDigit(s[j]) || s[j] == '.')) j++;
                    nums.Push(double.Parse(s.Substring(i, j - i), System.Globalization.CultureInfo.InvariantCulture));
                    i = j - 1;
                }
                else if (c == '(') ops.Push(c);
                else if (c == ')') { while (ops.Count > 0 && ops.Peek() != '(') { char o = ops.Pop(); if (nums.Count < 2) return null; nums.Push(Apply(nums.Pop(), nums.Pop(), o)); } if (ops.Count == 0) return null; ops.Pop(); }
                else if (c is '+' or '-' or '*' or '/' or '^' or '%') { while (ops.Count > 0 && ops.Peek() != '(' && Prec(ops.Peek()) >= Prec(c)) { char o = ops.Pop(); if (nums.Count < 2) return null; nums.Push(Apply(nums.Pop(), nums.Pop(), o)); } ops.Push(c); }
                else return null;
            }
            while (ops.Count > 0) { char o = ops.Pop(); if (o == '(') return null; if (nums.Count < 2) return null; nums.Push(Apply(nums.Pop(), nums.Pop(), o)); }
            return nums.Count == 1 ? nums.Pop() : null;
        }
        catch { return null; }
    }

    private void NavigateTo(TabVM tab, string raw, bool addHistory)
    {
        raw = raw.Trim();
        if (raw.StartsWith("optima:", StringComparison.OrdinalIgnoreCase)) { OpenAbout(tab); return; }
        var url = Normalize(raw);
        if (string.IsNullOrEmpty(url)) { tab.Url = null; ActivateTab(tab); return; }
        if (Reader.Visibility == Visibility.Visible) Reader.Visibility = Visibility.Collapsed;
        tab.Url = url; ShowWeb(); SyncUrlBox(url, force: false); UpdateStar();
        if (_coreReady) { StatusC("Завантаження…"); Core.CoreWebView2?.Navigate(url); }
        else { _pendingNav = url; _pendingTab = tab; StatusC("Запуск рушія…"); _ = InitCoreAsync(); }
    }

    private void OpenAbout(TabVM tab)
    {
        tab.Url = "optima:about"; tab.Title = "Про Optima Browser"; tab.Icon = ""; ShowWeb(); _ach.Unlock("about1");
        if (_coreReady) { try { Core.CoreWebView2!.NavigateToString(AboutHtml); } catch (Exception ex) { StatusC("Помилка: " + ex.Message); } }
        else { _pendingHtml = AboutHtml; StatusC("Запуск рушія…"); _ = InitCoreAsync(); }
    }

    private void OpenDownloads()
    {
        if (Active == null) return;
        var sb = new System.Text.StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>Завантаження</title><style>body{margin:0;font-family:'Segoe UI',sans-serif;background:linear-gradient(160deg,#121827,#0a0d16 60%,#070a13);color:#edf1f7;padding:48px}h1{margin:0 0 4px;font-size:24px}.s{color:#9da6bb;font-size:12px;margin-bottom:24px}.dl{background:rgba(255,255,255,.05);border:1px solid rgba(255,255,255,.12);border-radius:12px;padding:14px 16px;margin-bottom:10px}.n{font-size:14px;font-weight:600}.u{color:#5c6577;font-size:11px;margin-top:2px;word-break:break-all}.p{color:#35e2d0;font-size:11px;margin-top:2px;word-break:break-all}.e{color:#5c6577;font-size:13px}</style></head><body><h1>Завантаження</h1><div class=\"s\">файли зберігаються у папці Downloads · гаряча клавіша Ctrl+J</div>");
        if (_downloads.Count == 0) sb.Append("<div class=\"e\">Поки нічого не завантажено.</div>");
        else foreach (var d in _downloads) sb.Append($"<div class=\"dl\"><div class=\"n\">{Esc(d.Name)}</div><div class=\"u\">{Esc(d.Url)}</div><div class=\"p\">📁 {Esc(d.Store)}</div></div>");
        sb.Append("</body></html>");
        var tab = Active;
        tab.Url = "optima:downloads"; tab.Title = "Завантаження"; tab.Icon = ""; ShowWeb();
        if (_coreReady) { try { Core.CoreWebView2!.NavigateToString(sb.ToString()); } catch (Exception ex) { StatusC("Помилка: " + ex.Message); } }
        else { _pendingHtml = sb.ToString(); StatusC("Запуск рушія…"); _ = InitCoreAsync(); }
    }

    private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private string Normalize(string raw)
    {
        raw = raw.Trim(); if (raw.Length == 0) return "";
        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return raw;
        // швидкий пошук префіксом: «g запит», «w запит», «y запит»…
        foreach (var q in Quick) if (raw.StartsWith(q.P, StringComparison.OrdinalIgnoreCase)) return q.Url + Uri.EscapeDataString(raw.Substring(q.P.Length).Trim());
        if (!raw.Contains(' ') && (raw.Contains('.') || raw.Contains(':'))) return "https://" + raw;
        var eng = Array.Find(Engines, x => x.Id == _settings.Search) ?? Engines[0];
        return eng.Url + Uri.EscapeDataString(raw);
    }

    private async Task InitCoreAsync() { if (_initTask != null) { await _initTask; return; } _initTask = InitCoreImpl(); await _initTask; }

    private async Task InitCoreImpl()
    {
        try
        {
            CoreWebView2EnvironmentOptions? opts = null;
            if (_settings.ForceDark) opts = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--enable-features=WebContentsForceDark" };
            var env = await CoreWebView2Environment.CreateAsync(null, CoreDataDir, opts);
            await Core.EnsureCoreWebView2Async(env);
            var c = Core.CoreWebView2!;
            c.Settings.IsStatusBarEnabled = false; c.Settings.AreDevToolsEnabled = false;
            c.Settings.IsGeneralAutofillEnabled = false; c.Settings.IsPasswordAutosaveEnabled = false;
            c.Settings.IsPinchZoomEnabled = true; c.Settings.IsZoomControlEnabled = true;
            c.NewWindowRequested += Core_NewWindowRequested; c.ProcessFailed += Core_ProcessFailed;
            c.DownloadStarting += Core_DownloadStarting;
            c.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            c.WebResourceRequested += Core_WebResourceRequested;
            _coreReady = true; _metricsTimer.Start(); ApplyZoom();
            if (_pendingHtml != null) { var html = _pendingHtml; _pendingHtml = null; c.NavigateToString(html); }
            else if (_pendingNav != null && _pendingTab != null) { var url = _pendingNav; _pendingNav = null; _pendingTab = null; c.Navigate(url); }
        }
        catch (Exception ex) { StatusC("Помилка рушія: " + ex.Message); }
    }

    private void Core_WebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (!_settings.AdBlock) return;
        try
        {
            var host = new Uri(e.Request.Uri).Host;
            foreach (var b in BlockedHosts)
                if (host.EndsWith(b, StringComparison.OrdinalIgnoreCase))
                {
                    _blockedCount++; e.Response = Core.CoreWebView2!.Environment.CreateWebResourceResponse(null, 403, "Blocked by Optima", null);
                    BlockedPill.Text = $"Заблоковано: {_blockedCount}";
                    _ach.TryUnlock("block50", _blockedCount >= 50); _ach.TryUnlock("block250", _blockedCount >= 250); _ach.TryUnlock("block1000", _blockedCount >= 1000);
                    if (_blockedCount == 100 && !_eggBlock100) { _eggBlock100 = true; StatusC("Заблоковано 100 запитів. Реклама сумує."); }
                    if (_blockedCount == 1000 && !_eggBlock1000) { _eggBlock1000 = true; StatusC("1000 запитів у пилосос. Твій трафік — твої правила."); }
                    return;
                }
        }
        catch { }
    }

    private void Core_ProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e) { if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited) { StatusC("Рушій завершився — відновлюю…"); Dispatcher.BeginInvoke(() => { try { Core.CoreWebView2?.Reload(); } catch { } }); } }

    private void Core_DownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Directory.CreateDirectory(dir);
            string name = "download";
            try { if (Uri.TryCreate(e.DownloadOperation.Uri, UriKind.Absolute, out var du)) name = SafeFileName(Path.GetFileName(du.AbsolutePath)); } catch { }
            if (name.Length == 0) name = "download";
            string full = Path.Combine(dir, name);
            int k = 1; string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name); // колізія імен: «файл (1).png»
            while (File.Exists(full)) full = Path.Combine(dir, $"{stem} ({k++}){ext}");
            name = Path.GetFileName(full);
            e.ResultFilePath = full;
            e.Handled = true;
            _downloads.Insert(0, new DlItem(name, e.DownloadOperation.Uri, full));
            string msg = "Завантаження: " + name;
            Dispatcher.BeginInvoke(() => { StatusC(msg); _ach.Unlock("dl1"); });
        }
        catch { }
    }

    private void Core_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        StatusC("Завантаження…"); ShowLoading(true);
        if (IsOptimaTab) { ShowLoading(false); return; }
        // реальне шифрування трафіку: http:// примусово піднімається до https:// (крім локальних адрес)
        if (_settings.ForceHttps && e.Uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            bool loopback = Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && (u.IsLoopback || string.Equals(u.Host, "localhost", StringComparison.OrdinalIgnoreCase));
            if (!loopback)
            {
                bool recent = _upgradedHttp.TryGetValue(e.Uri, out long last) && DateTime.UtcNow.Ticks - last < TimeSpan.FromSeconds(30).Ticks;
                if (!recent)
                {
                    _upgradedHttp[e.Uri] = DateTime.UtcNow.Ticks;
                    e.Cancel = true;
                    StatusC("🔒 Примусовий HTTPS — піднімаю до https://");
                    ShowLoading(false);
                    Dispatcher.BeginInvoke(() => { try { Core.CoreWebView2?.Navigate("https://" + e.Uri.Substring(7)); } catch { } });
                    _ach.Unlock("https1");
                    return;
                }
            }
        }
        if (Active != null && !string.IsNullOrEmpty(e.Uri)) { Active.Url = e.Uri; SyncUrlBox(e.Uri, force: true); UpdateSecurityGlyph(e.Uri); UpdateStar(); }
    }

    private void Core_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        ShowLoading(false); if (IsOptimaTab) return;
        var tab = Active; var src = Core.CoreWebView2?.Source?.ToString() ?? tab?.Url ?? "";
        if (e.IsSuccess && tab != null && !string.IsNullOrEmpty(src))
        {
            string host = TryHost(src); tab.Url = src;
            tab.Title = string.IsNullOrEmpty(Core.CoreWebView2?.DocumentTitle) ? host : Core.CoreWebView2!.DocumentTitle;
            Title = tab.Title; // заголовок вікна → таскбар показує сторінку
            tab.Icon = string.IsNullOrEmpty(host) ? "" : $"https://www.google.com/s2/favicons?domain={host}&sz=64";
            PushHistory(src, tab.Title); SyncUrlBox(src, force: true);
            StatusC((src.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? "🔒 " : "🌐 ") + host);
            if (!tab.IsPrivate) _ach.Unlock("first_page");
        }
        else if (!e.IsSuccess) StatusC("Помилка: " + e.WebErrorStatus);
    }

    private void Core_SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        BackBtn.IsEnabled = Core.CoreWebView2?.CanGoBack == true; FwdBtn.IsEnabled = Core.CoreWebView2?.CanGoForward == true; ShowLoading(false);
        if (IsOptimaTab) return;
        if (Active != null && Core.CoreWebView2 != null) { var src = Core.CoreWebView2.Source?.ToString() ?? ""; Active.Url = src; SyncUrlBox(src, force: true); UpdateSecurityGlyph(src); }
    }

    private void ShowLoading(bool loading) { StopBtn.Visibility = loading ? Visibility.Visible : Visibility.Collapsed; ReloadBtn.Visibility = loading ? Visibility.Collapsed : Visibility.Visible; }

    private void PushHistory(string url, string title)
    {
        if (Active?.IsPrivate == true || _locked) return;
        if (!_settings.SaveHistory) return;
        if (url.StartsWith("optima:", StringComparison.OrdinalIgnoreCase) || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return;
        _history.RemoveAll(h => string.Equals(h.Url, url, StringComparison.OrdinalIgnoreCase)); // дедуп як у Chrome: сайт піднімається наверх
        _history.Insert(0, new HistEntry(url, title, DateTime.UtcNow.Ticks));
        if (_history.Count > 400) _history.RemoveRange(400, _history.Count - 400);
        MarkDirty();
    }

    private void Core_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; if (!string.IsNullOrEmpty(e.Uri)) NewTab(e.Uri); }

    private void BackBtn_Click(object sender, RoutedEventArgs e) => Core.CoreWebView2?.GoBack();
    private void FwdBtn_Click(object sender, RoutedEventArgs e) => Core.CoreWebView2?.GoForward();
    private void StopBtn_Click(object sender, RoutedEventArgs e) => Core.CoreWebView2?.Stop();

    private void ReloadBtn_Click(object sender, RoutedEventArgs e)
    {
        if (IsOptimaTab && Active != null) { OpenAbout(Active); return; }
        if (_coreReady) { if (Start.Visibility == Visibility.Visible && Active?.Url != null) { ShowWeb(); Core.CoreWebView2?.Navigate(Active.Url); } else Core.CoreWebView2?.Reload(); }
        else if (Active?.Url != null) NavigateTo(Active, Active.Url, addHistory: false);
    }

    private void HomeBtn_Click(object sender, RoutedEventArgs e) { if (Active != null) { Active.Url = null; ActivateTab(Active); } }

    // ---------- omnibox ----------

    private void UrlBox_TextChanged(object sender, TextChangedEventArgs e) { _suggestTimer.Stop(); _suggestTimer.Start(); }

    private void UrlBox_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter: e.Handled = true; SuggPanel.Visibility = Visibility.Collapsed; if (SuggList.SelectedItem is SuggItem sel) NavigateActive(sel.Url); else NavigateActive(UrlBox.Text); break;
            case Key.Escape: e.Handled = true; SuggPanel.Visibility = Visibility.Collapsed; break;
            case Key.Down or Key.Up: e.Handled = true; int items = SuggList.Items.Count; if (items == 0) break; ShowSuggestions(); int idx = SuggList.SelectedIndex; idx = e.Key == Key.Down ? Math.Min(idx + 1, items - 1) : Math.Max(idx - 1, 0); SuggList.SelectedIndex = idx; (SuggList.ItemContainerGenerator.ContainerFromIndex(idx) as ListBoxItem)?.BringIntoView(); break;
        }
    }

    private void UrlBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e) { _suggestTimer.Stop(); SuggPanel.Visibility = Visibility.Collapsed; }
    private void UrlBox_PreviewMouseDown(object sender, MouseButtonEventArgs e) { if (!UrlBox.IsKeyboardFocusWithin) { UrlBox.Focus(); UrlBox.SelectAll(); } }

    private void SuggList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (sender is ItemsControl ic && e.OriginalSource is DependencyObject d && ic.ContainerFromElement(d) is ListBoxItem item && item.DataContext is SuggItem s)
        { e.Handled = true; SuggPanel.Visibility = Visibility.Collapsed; _ach.Unlock("sugg1"); NavigateActive(s.Url); }
    }

    private void BuildSuggestions()
    {
        var q = UrlBox.Text.Trim();
        if (q.Length == 0 || Active == null) { SuggPanel.Visibility = Visibility.Collapsed; return; }
        var items = new List<SuggItem>();
        foreach (var (url, title) in _bookmarks) if (url.Contains(q, StringComparison.OrdinalIgnoreCase) || title.Contains(q, StringComparison.OrdinalIgnoreCase)) items.Add(new SuggItem("\uE734", title, url, url));
        foreach (var h in _history) if (h.Url.Contains(q, StringComparison.OrdinalIgnoreCase) || h.Title.Contains(q, StringComparison.OrdinalIgnoreCase)) if (!items.Any(i => string.Equals(i.Url, h.Url, StringComparison.OrdinalIgnoreCase))) items.Add(new SuggItem("\uE774", h.Title, h.Url, h.Url));
        items.Sort((a, b) => { int Rank(SuggItem s) => s.Url.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : s.Title.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 1 : 2; return Rank(a).CompareTo(Rank(b)); });
        if (items.Count == 0) { SuggPanel.Visibility = Visibility.Collapsed; return; }
        SuggList.ItemsSource = items.Take(10).ToList(); SuggList.SelectedIndex = -1; ShowSuggestions();
    }

    private void ShowSuggestions() { RefreshSuggPos(); SuggPanel.Visibility = Visibility.Visible; }

    private void RefreshSuggPos()
    {
        if (SuggPanel.Visibility != Visibility.Visible) return;
        var p = Omnibox.TranslatePoint(new Point(0, 0), Root);
        SuggPanel.Width = Omnibox.ActualWidth;
        SuggPanel.Margin = new Thickness(p.X, p.Y + Omnibox.ActualHeight + 6, 0, 0);
    }

    private void SyncUrlBox(string url, bool force) { if (UrlBox.IsKeyboardFocused && !force) return; UrlBox.Text = url; }

    private void UpdateSecurityGlyph(string url)
    {
        bool secure = url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        SecGlyph.Text = secure ? "\uE72E" : "\uE774";
        SecGlyph.Foreground = secure ? (Brush)FindResource("BrushAccentEmerald") : (Brush)FindResource("BrushTextLow");
    }

    private void StarBtn_Click(object sender, RoutedEventArgs e)
    {
        var tab = Active; if (tab?.Url == null || _locked) return;
        if (_bookmarks.Remove(tab.Url)) StatusC("Закладку прибрано");
        else { _bookmarks[tab.Url] = tab.Title; StatusC("Додано в закладки"); _ach.Touch("bookmarks"); _ach.TryUnlock("bookmark5", _ach.Value("bookmarks") >= 5); _ach.TryUnlock("bookmark20", _ach.Value("bookmarks") >= 20); }
        MarkDirty(); UpdateStar();
    }

    private void UpdateStar()
    {
        bool isBm = Active?.Url != null && _bookmarks.ContainsKey(Active.Url);
        StarGlyph.Text = isBm ? "\uE735" : "\uE734";
        StarGlyph.Foreground = isBm ? (Brush)FindResource("BrushAccentCyan") : (Brush)FindResource("BrushTextLow");
    }

    // ---------- metrics ----------

    private void SampleMetrics() { try { RamPill.Text = $"RAM: {Process.GetCurrentProcess().WorkingSet64 / 1048576} MB"; } catch { } }

    // ---------- zoom ----------

    private void ZoomIn_Click(object sender, RoutedEventArgs e) { _zoomPct = Math.Min(_zoomPct + 10, 300); ApplyZoom(); }
    private void ZoomOut_Click(object sender, RoutedEventArgs e) { _zoomPct = Math.Max(_zoomPct - 10, 30); ApplyZoom(); }
    private void ZoomPill_Click(object sender, MouseButtonEventArgs e) { _zoomPct = 100; ApplyZoom(); StatusC("Масштаб скинуто: 100%"); }

    private void ApplyZoom()
    {
        _settings.Zoom = _zoomPct; ZoomPill.Text = $"{_zoomPct}%"; SettingsZoomPill.Text = $"{_zoomPct}%";
        if (_zoomPct >= 200) _ach.Unlock("zoom_s");
        if (_coreReady && Core.CoreWebView2 != null) Core.ZoomFactor = _zoomPct / 100.0;
    }

    // ---------- adblock shield ----------

    private void ShieldBtn_Click(object sender, RoutedEventArgs e) { _settings.AdBlock = !_settings.AdBlock; SaveSettings(); UpdateShieldUi(); StatusC(_settings.AdBlock ? "Блокування реклами увімкнено" : "Блокування реклами вимкнено"); }
    private void UpdateShieldUi() { ShieldGlyph.Opacity = _settings.AdBlock ? 1.0 : 0.4; ShieldBtn.ToolTip = _settings.AdBlock ? "Блокування реклами — увімкнено." : "Блокування реклами — вимкнено."; }

    // ---------- more menu ----------

    private void MoreBtn_Click(object sender, RoutedEventArgs e)
    {
        var m = new ContextMenu(); // StaysOpen=true (за замовчуванням): з false підменю Vault закривалось, бо його Popup — окреме вікно
        m.Opened += (_, _) => { m.Focus(); Keyboard.Focus(m); };
        var tabItem = new MenuItem { Header = "Нова вкладка", InputGestureText = "Ctrl+T", Icon = Png("tab") }; tabItem.Click += (_, _) => NewTab();
        var homeItem = new MenuItem { Header = "На головну", Icon = Png("home") }; homeItem.Click += (_, _) => HomeBtn_Click(this, new RoutedEventArgs());
        m.Items.Add(tabItem); m.Items.Add(homeItem);
        var copy = new MenuItem { Header = "Копіювати адресу", InputGestureText = "Ctrl+Shift+C", Icon = Png("copy") };
        copy.Click += (_, _) => CopyAddress();
        m.Items.Add(copy);
        m.Items.Add(new Separator());
        var reader = new MenuItem { Header = "Режим читання", InputGestureText = "Ctrl+Shift+R", Icon = Png("reader") }; reader.Click += (_, _) => ReaderMode();
        var shot = new MenuItem { Header = "Скріншот сторінки", Icon = Png("shot") }; shot.Click += (_, _) => _ = ScreenshotAsync();
        var pdf = new MenuItem { Header = "Зберегти як PDF", Icon = Png("pdf") }; pdf.Click += (_, _) => _ = SavePdfAsync();
        var accent = (Brush)FindResource("BrushAccentCyan");
        var tr = new MenuItem { Header = "Перекласти сторінку", Icon = Png("trans"), ToolTip = "12 мов — вибір запам'ятовується" };
        foreach (var (tcode, tlabel) in TLangs)
        {
            bool cur = tcode == _settings.TranslateLang;
            var li = new MenuItem { Header = tlabel, FontWeight = cur ? FontWeights.SemiBold : FontWeights.Normal, Foreground = cur ? accent : (Brush)FindResource("BrushTextHi") };
            string c = tcode;
            li.Click += (_, _) => TranslatePage(c);
            tr.Items.Add(li);
        }
        var dl = new MenuItem { Header = "Завантаження", InputGestureText = "Ctrl+J", Icon = Png("dl") }; dl.Click += (_, _) => OpenDownloads();
        m.Items.Add(reader); m.Items.Add(shot); m.Items.Add(pdf); m.Items.Add(tr); m.Items.Add(dl);
        m.Items.Add(new Separator());
        var find = new MenuItem { Header = "Пошук на сторінці", InputGestureText = "Ctrl+F", Icon = Png("find") }; find.Click += (_, _) => OpenFind();
        var fs = new MenuItem { Header = "Повний екран", InputGestureText = "F11", Icon = Png("fs") }; fs.Click += (_, _) => ToggleFullscreen();
        var mute = new MenuItem { Header = "Звук (mute)", Icon = Png("mute") }; mute.Click += (_, _) => ToggleMute();
        m.Items.Add(find); m.Items.Add(fs); m.Items.Add(mute);
        m.Items.Add(new Separator());
        var bm = new MenuItem { Header = "Закладки", InputGestureText = "Ctrl+Shift+B", Icon = Png("bm") }; bm.Click += (_, _) => OpenDrawer("bookmarks");
        var hist = new MenuItem { Header = "Історія", InputGestureText = "Ctrl+H", Icon = Png("hist") }; hist.Click += (_, _) => OpenDrawer("history");
        m.Items.Add(bm); m.Items.Add(hist);
        m.Items.Add(new Separator());
        var priv = new MenuItem { Header = "Приватна вкладка", InputGestureText = "Ctrl+Shift+N", Icon = Png("priv") }; priv.Click += (_, _) => NewTab(null, isPrivate: true);
        var set = new MenuItem { Header = "Налаштування", Icon = Png("set") }; set.Click += (_, _) => OpenSettings();
        var achItem = new MenuItem { Header = "Досягнення", Icon = Png("ach") }; achItem.Click += (_, _) => OpenAchievements();
        var wipe = new MenuItem { Header = "Очистити дані браузера", Icon = Png("wipe") }; wipe.Click += (_, _) => _ = ClearDataCore();
        m.Items.Add(priv); m.Items.Add(set); m.Items.Add(achItem); m.Items.Add(wipe);
        m.Items.Add(new Separator());
        var about = new MenuItem { Header = "Про Optima Browser", Icon = Png("about") }; about.Click += (_, _) => { if (Active != null) OpenAbout(Active); };
        m.Items.Add(about);
        var vault = new MenuItem { Header = "Захист даних (Optima Vault)", Icon = Png("vault") };
        if (_vaultActive) { if (_vaultPass == null)
        { var u = new MenuItem { Header = "Розблокувати Vault", Icon = Png("unlock"), Foreground = accent, FontWeight = FontWeights.SemiBold }; u.Click += (_, _) => OpenVault(VaultMode.Unlock); vault.Items.Add(u); }
        else { var lk = new MenuItem { Header = "Заблокувати дані", Icon = Png("lock") }; lk.Click += (_, _) => LockVault(); vault.Items.Add(lk); var ch = new MenuItem { Header = "Змінити пароль", Icon = Png("vault") }; ch.Click += (_, _) => OpenVault(VaultMode.Change); vault.Items.Add(ch); var ds = new MenuItem { Header = "Вимкнути шифрування", Icon = Png("vault") }; ds.Click += (_, _) => OpenVault(VaultMode.Disable); vault.Items.Add(ds); } }
        else { var en = new MenuItem { Header = "Увімкнути шифрування", Icon = Png("unlock"), Foreground = accent, FontWeight = FontWeights.SemiBold }; en.Click += (_, _) => OpenVault(VaultMode.Setup); vault.Items.Add(en); }
        m.Items.Add(vault);
        m.PlacementTarget = MoreBtn; m.IsOpen = true;
    }

    // ---------- reader mode ----------

    private async void ReaderMode()
    {
        if (!_coreReady || Core.Visibility != Visibility.Visible) return;
        try
        {
            var json = await Core.CoreWebView2!.ExecuteScriptAsync("(function(){var el=document.querySelector('article,main,[role=\"main\"],.post,.content')||document.body;var txt=(el.innerText||el.textContent||'').replace(/\\n{3,}/g,'\\n\\n').trim().slice(0,120000);return{title:document.title,url:location.href,text:txt};})()");
            var doc = JsonSerializer.Deserialize<JsonElement>(json); var text = doc.GetProperty("text").GetString() ?? "";
            ReaderTitle.Text = doc.GetProperty("title").GetString() ?? ""; ReaderUrl.Text = doc.GetProperty("url").GetString() ?? "";
            ReaderBody.Text = text.Length == 0 ? "Тут порожньо." : text; StatusC(text.Length == 0 ? "Режим читання: текст не знайдено" : "Режим читання");
            Reader.Visibility = Visibility.Visible; _ach.Unlock("reader1");
        }
        catch (Exception ex) { StatusC("Режим читання: " + ex.Message); }
    }

    private void ReaderSmaller_Click(object sender, RoutedEventArgs e) => SetReaderSize(_readerSize - 2);
    private void ReaderBigger_Click(object sender, RoutedEventArgs e) => SetReaderSize(_readerSize + 2);
    private void SetReaderSize(int s) { _readerSize = Math.Clamp(s, 12, 28); ReaderBody.FontSize = _readerSize; }
    private void ReaderClose_Click(object sender, RoutedEventArgs e) { Reader.Visibility = Visibility.Collapsed; StatusC("Режим читання закрито"); }

    // ---------- screenshot / pdf / translate ----------

    private async Task ScreenshotAsync()
    {
        if (!_coreReady) return;
        try { var dlg = new SaveFileDialog { Filter = "PNG (*.png)|*.png", FileName = SafeFileName(TryHost(Active?.Url ?? "page")) + ".png" }; if (dlg.ShowDialog(this) != true) return; using var fs = File.Create(dlg.FileName); await Core.CoreWebView2!.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs); StatusC("Скріншот збережено"); _ach.Unlock("shot1"); }
        catch (Exception ex) { StatusC("Скріншот: " + ex.Message); }
    }

    private async Task SavePdfAsync()
    {
        if (!_coreReady) return;
        try { var dlg = new SaveFileDialog { Filter = "PDF (*.pdf)|*.pdf", FileName = SafeFileName(TryHost(Active?.Url ?? "page")) + ".pdf" }; if (dlg.ShowDialog(this) != true) return; bool ok = await Core.CoreWebView2!.PrintToPdfAsync(dlg.FileName); StatusC(ok ? "PDF збережено" : "PDF: не вдалося"); if (ok) _ach.Unlock("pdf1"); }
        catch (Exception ex) { StatusC("PDF: " + ex.Message); }
    }

    private void TranslatePage(string lang)
    {
        var url = Active?.Url; if (string.IsNullOrEmpty(url)) return;
        _settings.TranslateLang = lang; SaveSettings(); // запам'ятовуємо вибір мови
        _ach.Touch("translates"); _ach.TryUnlock("trans5", _ach.Value("translates") >= 5);
        _ach.Unlock("translate1");
        NavigateActive("https://translate.google.com/translate?sl=auto&tl=" + lang + "&u=" + Uri.EscapeDataString(url));
    }

    private static string SafeFileName(string s) { foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_'); return s.Length == 0 ? "page" : s; }

    // ---------- drawer ----------

    private void OpenDrawer(string mode) { if (_locked) { StatusC("Vault заблоковано — розблокуй"); return; } _drawerMode = mode; DrawerTitle.Text = mode == "bookmarks" ? "Закладки" : "Історія"; DrawerSearch.Text = ""; DrawerClearBtn.Content = mode == "bookmarks" ? "Видалити всі закладки" : "Очистити історію"; Drawer.Visibility = Visibility.Visible; FillDrawer(""); }
    private void DrawerClose_Click(object sender, RoutedEventArgs e) { Drawer.Visibility = Visibility.Collapsed; StatusC("Готово"); }
    private void DrawerSearch_TextChanged(object sender, TextChangedEventArgs e) => FillDrawer(DrawerSearch.Text.Trim());

    private void FillDrawer(string q)
    {
        var items = new List<DrawerItem>();
        if (_drawerMode == "bookmarks") { foreach (var (url, title) in _bookmarks.OrderBy(kv => kv.Value, StringComparer.CurrentCultureIgnoreCase)) if (q.Length == 0 || title.Contains(q, StringComparison.OrdinalIgnoreCase) || url.Contains(q, StringComparison.OrdinalIgnoreCase)) items.Add(new DrawerItem("\uE734", title.Length == 0 ? TryHost(url) : title, url, url)); }
        else { foreach (var h in _history) if (q.Length == 0 || h.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || h.Url.Contains(q, StringComparison.OrdinalIgnoreCase)) items.Add(new DrawerItem("\uE81C", h.Title.Length == 0 ? TryHost(h.Url) : h.Title, h.Url, h.Url)); }
        DrawerList.ItemsSource = items;
    }

    private void DrawerList_DoubleClick(object sender, MouseButtonEventArgs e) { if (DrawerList.SelectedItem is DrawerItem it) { Drawer.Visibility = Visibility.Collapsed; NavigateActive(it.Url); } }
    private void DrawerItemDelete_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.Tag is string url) { if (_drawerMode == "bookmarks") _bookmarks.Remove(url); else _history.RemoveAll(h => h.Url == url); MarkDirty(); FillDrawer(DrawerSearch.Text.Trim()); } }
    private void DrawerClear_Click(object sender, RoutedEventArgs e) { if (_drawerMode == "bookmarks") { _bookmarks.Clear(); } else { _history.Clear(); } MarkDirty(); FillDrawer(DrawerSearch.Text.Trim()); }

    // ---------- settings ----------

    private void RefreshSettingsUi()
    {
        _uiSyncing = true;
        int si = 0; for (int i = 0; i < Engines.Length; i++) if (Engines[i].Id == _settings.Search) { si = i; break; }
        SearchCmb.SelectedIndex = si;
        AdBlockCb.IsChecked = _settings.AdBlock;
        ForceDarkCb.IsChecked = _settings.ForceDark;
        HistCb.IsChecked = _settings.SaveHistory;
        SessCb.IsChecked = _settings.SaveSession;
        RecentCb.IsChecked = _settings.StartRecent;
        AnimCb.IsChecked = _settings.Animations;
        StatusCb.IsChecked = _settings.ShowStatus;
        if (HomeBox.Text != _settings.Home) HomeBox.Text = _settings.Home;
        int ti = 0; for (int i = 0; i < Themes.Length; i++) if (Themes[i].Id == _settings.Theme) { ti = i; break; }
        ThemeCmb.SelectedIndex = ti;
        _uiSyncing = false;
        ApplyUiPrefs();
    }

    private void ApplyUiPrefs()
    {
        StatusBar.Visibility = _settings.ShowStatus ? Visibility.Visible : Visibility.Collapsed;
        B1.Visibility = B2.Visibility = B3.Visibility = _settings.Animations ? Visibility.Visible : Visibility.Collapsed;
        if (!_settings.Animations) { FxLayer.Children.Clear(); _fx.Clear(); _fxTimer.Stop(); }
    }

    private void SearchCmb_Changed(object sender, SelectionChangedEventArgs e) { if (_uiSyncing || SearchCmb.SelectedIndex < 0) return; SetEngine(Engines[SearchCmb.SelectedIndex].Id); }
    private void AdBlockCb_Changed(object sender, RoutedEventArgs e) { if (_uiSyncing) return; _settings.AdBlock = AdBlockCb.IsChecked == true; SaveSettings(); UpdateShieldUi(); }
    private void ForceDarkCb_Changed(object sender, RoutedEventArgs e) { if (_uiSyncing) return; _settings.ForceDark = ForceDarkCb.IsChecked == true; SaveSettings(); }
    private void HttpsCb_Changed(object sender, RoutedEventArgs e) { if (_uiSyncing) return; _settings.ForceHttps = HttpsCb.IsChecked == true; SaveSettings(); StatusC(_settings.ForceHttps ? "🔒 Примусовий HTTPS увімкнено" : "Примусовий HTTPS вимкнено"); }
    private void HistCb_Changed(object sender, RoutedEventArgs e) { if (_uiSyncing) return; _settings.SaveHistory = HistCb.IsChecked == true; SaveSettings(); }
    private void SessCb_Changed(object sender, RoutedEventArgs e) { if (_uiSyncing) return; _settings.SaveSession = SessCb.IsChecked == true; SaveSettings(); }
    private void RecentCb_Changed(object sender, RoutedEventArgs e) { if (_uiSyncing) return; _settings.StartRecent = RecentCb.IsChecked == true; SaveSettings(); }
    private void AnimCb_Changed(object sender, RoutedEventArgs e) { if (_uiSyncing) return; _settings.Animations = AnimCb.IsChecked == true; SaveSettings(); ApplyUiPrefs(); }
    private void StatusCb_Changed(object sender, RoutedEventArgs e) { if (_uiSyncing) return; _settings.ShowStatus = StatusCb.IsChecked == true; SaveSettings(); ApplyUiPrefs(); }
    private void HomeBox_TextChanged(object sender, TextChangedEventArgs e) { if (_uiSyncing) return; _settings.Home = HomeBox.Text.Trim(); SaveSettings(); }
    private void OpenSettings() { SettingsZoomPill.Text = $"{_zoomPct}%"; SettingsOverlay.Visibility = Visibility.Visible; _ach.Unlock("settings1"); }
    private void SettingsOverlay_MouseDown(object sender, MouseButtonEventArgs e) { if (e.OriginalSource == SettingsOverlay) SettingsOverlay.Visibility = Visibility.Collapsed; }
    private void SettingsClose_Click(object sender, RoutedEventArgs e) => SettingsOverlay.Visibility = Visibility.Collapsed;

    private void CopyAddress()
    {
        try { if (Active?.Url != null) { Clipboard.SetText(Active.Url); StatusC("Адресу скопійовано"); } }
        catch { StatusC("Не вдалося скопіювати"); }
    }
    private void NewTabBtn_Click(object sender, RoutedEventArgs e) => NewTab();

    // ---------- пошук по сторінці (Ctrl+F) ----------

    private void OpenFind()
    {
        if (FindBar.Visibility == Visibility.Collapsed) FindBar.Visibility = Visibility.Visible;
        FindBox.Focus(); FindBox.SelectAll();
    }

    private void FindBox_TextChanged(object sender, TextChangedEventArgs e) { _findIdx = 0; _ = RunFindAsync(FindBox.Text, 0); }

    private void FindBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; _ = RunFindAsync(FindBox.Text, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? _findIdx - 1 : _findIdx + 1); }
        else if (e.Key == Key.Escape) { e.Handled = true; FindClose_Click(this, new RoutedEventArgs()); }
    }

    private void FindNext_Click(object sender, RoutedEventArgs e) => _ = RunFindAsync(FindBox.Text, _findIdx + 1);
    private void FindPrev_Click(object sender, RoutedEventArgs e) => _ = RunFindAsync(FindBox.Text, _findIdx - 1);

    private void FindClose_Click(object sender, RoutedEventArgs e)
    {
        FindBar.Visibility = Visibility.Collapsed;
        _ = RunFindAsync("", 0);
        Core.Focus();
    }

    private async Task RunFindAsync(string q, int idx)
    {
        if (!_coreReady || Core.Visibility != Visibility.Visible) { FindCount.Text = ""; return; }
        try
        {
            var js = FindJs + "(" + JsonSerializer.Serialize(q) + "," + idx + ")";
            var json = await Core.CoreWebView2!.ExecuteScriptAsync(js);
            var d = JsonSerializer.Deserialize<JsonElement>(json);
            int n = d.GetProperty("n").GetInt32(); int i = d.GetProperty("i").GetInt32();
            _findIdx = i;
            FindCount.Text = n == 0 ? "0/0" : $"{i + 1}/{n}";
            if (n > 0) _ach.Unlock("find1");
        }
        catch { }
    }

    // ---------- повний екран (F11) ----------

    private void ToggleFullscreen()
    {
        _fullscreen = !_fullscreen;
        if (_fullscreen)
        {
            TitleBar.Visibility = Toolbar.Visibility = TopLine.Visibility = BarLine.Visibility = Visibility.Collapsed;
            StatusBar.Visibility = Visibility.Collapsed;
            ContentGrid.Margin = new Thickness(0);
            StatusC("Повний екран — F11 або Esc для виходу");
            _ach.Unlock("fs1");
        }
        else
        {
            TitleBar.Visibility = Toolbar.Visibility = TopLine.Visibility = BarLine.Visibility = Visibility.Visible;
            StatusBar.Visibility = _settings.ShowStatus ? Visibility.Visible : Visibility.Collapsed;
            ContentGrid.Margin = new Thickness(0, 104, 0, 26);
            StatusC("Повний екран вимкнено");
        }
    }

    private void ToggleMute()
    {
        if (!_coreReady) return;
        bool muted = !Core.CoreWebView2!.IsMuted;
        Core.CoreWebView2.IsMuted = muted;
        StatusC(muted ? "🔇 Звук вимкнено" : "🔊 Звук увімкнено");
    }

    // ---------- очищення даних ----------

    private async void ClearData_Click(object sender, RoutedEventArgs e) => await ClearDataCore();

    private async Task ClearDataCore()
    {
        SettingsOverlay.Visibility = Visibility.Collapsed;
        try { if (_coreReady) await Core.CoreWebView2!.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile); } catch { }
        _history.Clear(); _bookmarks.Clear(); _blockedCount = 0; BlockedPill.Text = "Заблоковано: 0"; MarkDirty();
        _ach.Unlock("wipe1");
        StatusC("Дані очищено: куки, кеш, історія, закладки");
    }

    // ---------- easter eggs ----------

    private void FxTick(object? sender, EventArgs e)
    {
        for (int i = _fx.Count - 1; i >= 0; i--)
        {
            var f = _fx[i]; Canvas.SetLeft(f.El, Canvas.GetLeft(f.El) + f.Vx); Canvas.SetTop(f.El, Canvas.GetTop(f.El) + f.Vy); f.El.Opacity -= 0.012;
            if (f.El.Opacity <= 0.05 || Canvas.GetTop(f.El) > Root.ActualHeight + 40) { FxLayer.Children.Remove(f.El); _fx.RemoveAt(i); }
        }
        if (_fx.Count == 0) _fxTimer.Stop();
    }

    private void SpawnRain(Color[] colors, int count, string msg)
    {
        StatusC(msg);
        if (!_settings.Animations) return;
        double w = Root.ActualWidth > 0 ? Root.ActualWidth : 1200;
        for (int i = 0; i < count; i++)
        {
            var el = new Ellipse { Width = _rnd.Next(4, 10), Height = _rnd.Next(4, 10), Fill = new SolidColorBrush(colors[_rnd.Next(colors.Length)]), IsHitTestVisible = false };
            Canvas.SetLeft(el, _rnd.NextDouble() * w); Canvas.SetTop(el, -20 - _rnd.Next(60));
            FxLayer.Children.Add(el); _fx.Add(new FX { El = el, Vx = _rnd.NextDouble() * 2 - 1, Vy = 2.5 + _rnd.NextDouble() * 4 });
        }
        _fxTimer.Start();
    }

    private void SpawnEmojiRain(string[] emojis, int count, string msg)
    {
        StatusC(msg);
        if (!_settings.Animations) return;
        double w = Root.ActualWidth > 0 ? Root.ActualWidth : 1200;
        for (int i = 0; i < count; i++)
        {
            var tb = new TextBlock { Text = emojis[_rnd.Next(emojis.Length)], FontSize = _rnd.Next(14, 28), IsHitTestVisible = false };
            Canvas.SetLeft(tb, _rnd.NextDouble() * w); Canvas.SetTop(tb, -20 - _rnd.Next(90));
            FxLayer.Children.Add(tb); _fx.Add(new FX { El = tb, Vx = _rnd.NextDouble() * 2 - 1, Vy = 2.2 + _rnd.NextDouble() * 3.5 });
        }
        _fxTimer.Start();
    }

    private void TriggerKonami() { _konami.Clear(); SpawnRain(new[] { Color.FromRgb(0xFF, 0xD7, 0x00), Color.FromRgb(0xFF, 0xC1, 0x07), Color.FromRgb(0xFF, 0xE0, 0x82), Color.FromRgb(0x35, 0xE2, 0xD0), Color.FromRgb(0xFF, 0xFF, 0xFF) }, 90, "🎉 Пасхалка: Konami-код — золотий дощ активовано"); _ach.Unlock("konami"); }

    private void TriggerFlagRain() { _konami.Clear(); SpawnRain(new[] { Color.FromRgb(0x00, 0x5B, 0xBB), Color.FromRgb(0xFF, 0xD5, 0x00), Color.FromRgb(0xFF, 0xFF, 0xFF) }, 90, "💙💛 Слава Україні! Героям слава!"); _ach.Unlock("flag"); }

    private void LogoBox_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && e.ChangedButton == MouseButton.Left)
        {
            var rt = new RotateTransform(); LogoBox.RenderTransformOrigin = new Point(0.5, 0.5); LogoBox.RenderTransform = rt;
            rt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(700)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            StatusC("Логотип обертається. Це теж пасхалка.");
        }
    }

    // ---------- shortcuts ----------

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers; bool ctrl = mods.HasFlag(ModifierKeys.Control); bool alt = mods.HasFlag(ModifierKeys.Alt); bool shift = mods.HasFlag(ModifierKeys.Shift);
        if (e.Key == Key.Escape)
        {
            if (_fullscreen) { ToggleFullscreen(); e.Handled = true; return; }
            if (FindBar.Visibility == Visibility.Visible) { FindClose_Click(this, new RoutedEventArgs()); e.Handled = true; return; }
            if (Reader.Visibility == Visibility.Visible) { Reader.Visibility = Visibility.Collapsed; e.Handled = true; return; }
            if (SettingsOverlay.Visibility == Visibility.Visible) { SettingsOverlay.Visibility = Visibility.Collapsed; e.Handled = true; return; }
            if (AchOverlay.Visibility == Visibility.Visible) { AchOverlay.Visibility = Visibility.Collapsed; e.Handled = true; return; }
            if (VaultOverlay.Visibility == Visibility.Visible) { VaultCancel_Click(this, new RoutedEventArgs()); e.Handled = true; return; }
            if (Drawer.Visibility == Visibility.Visible) { Drawer.Visibility = Visibility.Collapsed; e.Handled = true; return; }
            if (SuggPanel.Visibility == Visibility.Visible) { SuggPanel.Visibility = Visibility.Collapsed; e.Handled = true; return; }
        }
        if (!ctrl && !alt && !e.IsRepeat) { _konami.Enqueue(e.Key); while (_konami.Count > 10) _konami.Dequeue(); if (_konami.Count == 10 && _konami.SequenceEqual(KonamiSeq)) TriggerKonami(); }

        if (ctrl && shift && e.Key == Key.R) { e.Handled = true; ReaderMode(); }
        else if (ctrl && shift && e.Key == Key.N) { e.Handled = true; NewTab(null, isPrivate: true); }
        else if (ctrl && shift && e.Key == Key.T) { e.Handled = true; ReopenClosedTab(); }
        else if (ctrl && shift && (e.Key == Key.W || e.Key == Key.Q)) { e.Handled = true; Close(); }
        else if (ctrl && shift && e.Key == Key.Delete) { e.Handled = true; SettingsOverlay.Visibility = Visibility.Collapsed; _ = ClearDataCore(); }
        else if (ctrl && e.Key == Key.T) { e.Handled = true; NewTab(); }
        else if (ctrl && e.Key == Key.F) { e.Handled = true; OpenFind(); }
        else if (ctrl && e.Key == Key.W) { e.Handled = true; if (Active != null) CloseTab(Active); }
        else if (ctrl && e.Key == Key.L) { e.Handled = true; UrlBox.Focus(); UrlBox.SelectAll(); }
        else if (ctrl && e.Key == Key.D) { e.Handled = true; StarBtn_Click(this, new RoutedEventArgs()); }
        else if (ctrl && shift && e.Key == Key.C) { e.Handled = true; CopyAddress(); }
        else if (ctrl && shift && e.Key == Key.B) { e.Handled = true; OpenDrawer("bookmarks"); }
        else if (ctrl && e.Key == Key.H) { e.Handled = true; OpenDrawer("history"); }
        else if (ctrl && e.Key == Key.J) { e.Handled = true; OpenDownloads(); }
        else if (e.Key == Key.F11) { e.Handled = true; ToggleFullscreen(); }
        else if (e.Key == Key.F3) { e.Handled = true; if (FindBar.Visibility == Visibility.Collapsed) OpenFind(); else _ = RunFindAsync(FindBox.Text, shift ? _findIdx - 1 : _findIdx + 1); }
        else if ((ctrl && e.Key == Key.R) || e.Key == Key.F5) { e.Handled = true; ReloadBtn_Click(this, new RoutedEventArgs()); }
        else if (alt && e.Key == Key.Left) { e.Handled = true; Core.CoreWebView2?.GoBack(); }
        else if (alt && e.Key == Key.Right) { e.Handled = true; Core.CoreWebView2?.GoForward(); }
        else if (alt && e.Key == Key.Home) { e.Handled = true; HomeBtn_Click(this, new RoutedEventArgs()); }
        else if (ctrl && (e.Key == Key.Add || e.Key == Key.OemPlus)) { e.Handled = true; ZoomIn_Click(this, new RoutedEventArgs()); }
        else if (ctrl && (e.Key == Key.Subtract || e.Key == Key.OemMinus)) { e.Handled = true; ZoomOut_Click(this, new RoutedEventArgs()); }
        else if (ctrl && e.Key == Key.D0) { e.Handled = true; _zoomPct = 100; ApplyZoom(); }
        else if (ctrl && e.Key == Key.Tab && Active != null && _tabs.Count > 1) { e.Handled = true; ActivateTab(_tabs[(_activeIdx + (shift ? -1 : 1) + _tabs.Count) % _tabs.Count]); }
        else if (ctrl && e.Key >= Key.D1 && e.Key <= Key.D9) { int i = e.Key - Key.D1; if (i < _tabs.Count) { e.Handled = true; ActivateTab(_tabs[i]); } }
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (SuggPanel.Visibility == Visibility.Visible && e.OriginalSource is DependencyObject d)
        { bool inside = false; for (var cur = d; cur != null && !inside; cur = VisualTreeHelper.GetParent(cur) ?? LogicalTreeHelper.GetParent(cur)) inside = cur == Omnibox || cur == SuggPanel; if (!inside) SuggPanel.Visibility = Visibility.Collapsed; }
    }

    // ---------- window ----------

    private void MinBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaxBtn_Click(object sender, RoutedEventArgs e) { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; MaxGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922"; }
    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    // ---------- helpers ----------

    private void Start_Requested(object? sender, RequestEventArgs e) { if (string.IsNullOrEmpty(e.Url)) return; if (e.NewTab) NewTab(e.Url); else NavigateActive(e.Url); }
    private void StatusC(string s) => StatusText.Text = s;
    private static string TryHost(string url) { try { return Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Host.Length > 0 ? u.Host : url; } catch { return url; } }
}