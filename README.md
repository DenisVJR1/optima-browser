# Optima Browser

> Liquid Glass Design · Single Executable · Native Traffic Encryption

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4) ![WPF](https://img.shields.io/badge/UI-WPF-4D90FE) ![WebView2](https://img.shields.io/badge/engine-WebView2%20(Edge)-35E2D0) ![License](https://img.shields.io/badge/license-MIT-green)

A native Windows browser built with WPF and WebView2 (Edge Engine). Delivered as a single executable with a stunning liquid glass design. Completely free of Electron and NPM dependencies - pure .NET performance.

<p align="center">
  <img src="logo.svg" alt="Optima Browser Logo" width="200"/>
</p>

## Getting Started

Run `publish\OptimaBrowser.exe` for a fully portable experience. 
**Prerequisite:** The system WebView2 Runtime (Edge) must be installed (standard on modern Windows systems).

## Features & Architecture

### Core Optimization
- **Unified Rendering Engine:** WebView2 is initialized only on the first navigation (lazy init). All tabs intelligently share and switch between a single instance, drastically reducing memory footprint compared to traditional browsers.
- **Engine Suspend Mode:** When the start page is active, the engine is suspended (`TrySuspendAsync`) to free up system memory, and seamlessly resumes upon navigation.
- **Stability First:** Features auto-recovery after engine crashes (`ProcessFailed`), built-in RAM counter, and disables unnecessary telemetry, autofill, and password saving for maximum privacy.

### Optima Vault & Encryption
- **Local Data Protection:** Browsing history, bookmarks, and session data are encrypted using **AES-256-GCM**.
- **Master Password:** The encryption key is derived from your master password (using PBKDF2-SHA256 with 100,000 iterations). Stored securely in `vault.obx`.
- **Data Lock:** You can lock, unlock, or change your password anytime via the Data Protection menu. (Warning: A forgotten password means data is permanently unrecoverable).
- **Traffic Encryption:** Real TLS 1.3 handled natively by the Edge engine. Additionally, **Forced HTTPS** automatically upgrades HTTP requests to HTTPS (excluding local addresses). The status bar prominently displays the security state (TLS or unencrypted) for every page.

### Integrated Search Systems
- Access to 10 built-in search engines: Bing, Google, DuckDuckGo, Ecosia, Brave Search, Qwant, Startpage, Yahoo, Mojeek, and Wikipedia (UA).
- **Embedded Assets:** Genuine high-quality logos for search engines are embedded directly in the executable. They work completely offline and render beautifully on glass-styled tiles.
- **Prefix Search Support:** Instantly search from the address bar using prefixes: `g query` (Google), `w` (Wikipedia), `y` (YouTube), `gh` (GitHub), `m` (Maps), `ddg` (DuckDuckGo), etc.

### Advanced Settings & Customization
- Easily toggle search engines, themes, ad-blocking, forced dark mode, and zoom levels.
- **Custom Home Page:** Configure new tabs to open your preferred address.
- **Session Management:** Toggles for keeping history, restoring the previous session, and displaying recent sites on the start page.
- **Performance Adjustments:** Disable visual effects (like aurora and confetti) to save resources on low-end machines.
- **Deep Dark Mode:** The entire UI, including dropdowns and scrollbars, supports a true dark mode template with zero white flash.

### Exclusive Features
- **Native Ad & Tracker Blocking:** Engine-level blocking for over 100 known tracker and ad domains. Includes a block counter and per-site exception toggles.
- **Tab Management:** Drag and drop tabs freely. Use `Ctrl+Shift+T` to restore up to 20 closed tabs.
- **Bookmark Management:** Full support for importing and exporting HTML bookmarks (Netscape format, compatible with Chrome/Firefox).
- **In-Page Search:** Fast `Ctrl+F` functionality with match highlighting and navigation.
- **Built-in Tools:** Integrated download manager (`Ctrl+J`), full screen mode (`F11`), and address bar calculator (e.g., `(3+5)*7`).
- **Reader Mode:** (`Ctrl+Shift+R`) Strip away clutter and read articles in a clean, glass-style viewer complete with word count and estimated reading time.
- **Translation:** Translate pages into 12 languages on the fly.
- **Memory Management:** Click the RAM counter in the status bar to force Garbage Collection and free up memory instantly.

### Achievements & Easter Eggs
- **Gamification:** Unlock 33 unique achievements (e.g., reaching 10/25 tabs, blocking 50/250/1000 trackers, finding easter eggs). Progress is saved locally in `achievements.json`.
- **Easter Eggs:** Try the Konami code (`Up Up Down Down Left Right Left Right B A`), or type special keywords like `cat`, `42`, or `help` in the address bar.

### User Interface & Design
- **Liquid Glass Aesthetics:** Utilizes native Acrylic (`DWMWA_SYSTEMBACKDROP_TYPE`) on Windows 11. On Windows 10, it falls back to a custom animated aurora background with translucent panels.
- **Dynamic Themes:** Choose from 6 stunning themes (Glass, Ocean, Flame, Forest, Sakura, Midnight) that apply instantly without restarting. Includes an Auto-Night mode based on your local time.
- **Custom Window Controls:** Clean, borderless WindowChrome implementation with custom dragging logic and hover highlights.
- **Attention to Detail:** Gradient icons, glowing focus states, thin glass scrollbars, and smooth animations create a premium feel.

## Build Instructions

To build the project yourself, ensure you have the .NET 8 SDK installed, then run:

```powershell
dotnet publish OptimaBrowser -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

## Data Storage Location

All local data is stored at `%LOCALAPPDATA%\OptimaBrowser`. This includes `achievements.json`, `crash.log`, and the encrypted `vault.obx`.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.