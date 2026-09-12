using System.IO;
using System.Text.Json;

namespace OptimaBrowser;

public sealed class Achievements
{
    public sealed record Def(string Id, string Title, string Desc, string Glyph);

    public static readonly Def[] All =
    {
        new("first_launch", "Давно пора",          "Запустити Optima Browser",                          "🚀"),
        new("first_page",   "Шлях прокладено",     "Відкрити першу сторінку",                           "🌐"),
        new("tabs10",       "Мультитаскер",        "Відкрити 10 вкладок",                               "🗂"),
        new("tabs25",       "Архітектор вкладок",  "Відкрити 25 вкладок",                               "🏗"),
        new("block50",      "Початок бучі",        "Заблокувати 50 запитів",                            "🛡"),
        new("block250",     "Серійний блокувальник","Заблокувати 250 запитів",                          "⚔"),
        new("block1000",    "Тисяча банерів",       "Заблокувати 1 000 запитів",                        "☠"),
        new("konami",       "30 життів",            "Ввести Konami-код",                                "🕹"),
        new("flag",         "Слава Україні",        "Секретний прапорець",                              "🇺🇦"),
        new("bookmark5",    "Бібліотекар",          "Додати 5 закладок",                                "⭐"),
        new("bookmark20",   "Архіваріус",           "Додати 20 закладок",                               "📚"),
        new("private1",     "Інкогніто",            "Перша приватна вкладка",                            "🕶"),
        new("reader1",      "Читач",                "Скористатися режимом читання",                      "📖"),
        new("shot1",        "Фотограф",             "Зробити скріншот",                                 "📸"),
        new("pdf1",         "Друкар",               "Зберегти сторінку як PDF",                          "🖨"),
        new("translate1",   "Поліглот",             "Перекласти сторінку",                               "🌍"),
        new("vault1",       "Сейф",                 "Увімкнути Optima Vault",                            "🔐"),
        new("a42",          "Глибока дума",         "Ввести 42 в адресний рядок",                        "🧠"),
        new("settings1",    "Тюнер",                "Відкрити налаштування",                             "🔧"),
        new("about1",       "Дослідник",            "Відкрити сторінку «Про Optima»",                   "🔍"),
        new("zoom_s",       "Зум-майстер",          "Масштаб сторінки 200 %+",                           "🔎"),
        new("middle1",      "Середній палець",      "Закрити вкладку середньою кнопкою миші",            "🖱"),
        new("sugg1",        "Браузер знає",         "Обрати підказку зі списку",                         "💡"),
        new("find1",        "Лупа",                 "Знайти текст на сторінці (Ctrl+F)",               "🔍"),
        new("fs1",          "Кінорежим",            "Увімкнути повний екран (F11)",                     "🖥"),
        new("calc1",        "Калькулятор",          "Порахувати вираз у адресному рядку",                "🧮"),
        new("dl1",          "Перший файл",          "Завантажити файл",                                  "📥"),
        new("https1",       "TLS-апгрейд",          "Примусовий HTTPS підняв http:// до https://",      "🔒"),
        new("wipe1",        "Чиста лисина",         "Очистити дані браузера",                            "🧹"),
        new("trans5",       "Поліглот",             "Перекласти сторінку 5 разів",                      "🗣"),
    };

    public event Action<Def>? OnUnlocked;
    public bool WasFresh { get; }

    private readonly string _path;
    private readonly HashSet<string> _unlocked = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);

    public int UnlockedCount => _unlocked.Count;
    public bool IsUnlocked(string id) => _unlocked.Contains(id);

    public Achievements(string path)
    {
        _path = path;
        bool exists = File.Exists(path);
        WasFresh = !exists;
        if (!exists) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("unlocked", out var arr))
                foreach (var e in arr.EnumerateArray())
                    if (e.GetString() is string s) _unlocked.Add(s);
            if (doc.RootElement.TryGetProperty("counts", out var c))
                foreach (var p in c.EnumerateObject())
                    _counts[p.Name] = p.Value.GetInt32();
        }
        catch { }
    }

    public int Value(string id) => _counts.TryGetValue(id, out var v) ? v : 0;

    public void Touch(string id)
    {
        _counts.TryGetValue(id, out var v);
        _counts[id] = v + 1;
    }

    public void Unlock(string id)
    {
        if (!_unlocked.Add(id)) return;
        Save();
        var def = All.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
        if (def != null) OnUnlocked?.Invoke(def);
    }

    public void TryUnlock(string id, bool cond) { if (cond) Unlock(id); }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllText(_path, JsonSerializer.Serialize(
                new { unlocked = _unlocked.OrderBy(x => x).ToArray(), counts = _counts },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}