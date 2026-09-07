# UI Redesign Plan for HermesLauncher (WPF)

**Date:** 2026-09-03  
**Scope:** `launcher/` (WPF, .NET). This document describes the target design, UI architecture, references, and a phased implementation plan.

---

## 1. Goals

1. The launcher should look like a modern Windows 11 application on par with the Xbox App, Docker Desktop, or LM Studio: Fluent style, Mica background, smooth navigation, icons, and consistent typography.
2. Every screen has explicit states: empty / loading / success / error / cancelled.
3. Secrets are hidden by default; actions involving them are visually confirmed.
4. The interface scales (DPI 100–200%, window from 980×680 to 4K) and is accessible via keyboard and screen reader.
5. Dark and light themes, following the system theme and the Windows accent color.
6. The UI code is split into reusable controls and pages; `MainWindow.xaml` ceases to be a ~500‑line monolith.

---

## 2. Current UI Audit

| Area | Issue | File |
|---|---|---|
| Window shell | `WindowStyle=None` + custom title bar. Loses Windows 11 Snap Layouts, system menu, window animations, and drop shadow. Window buttons are drawn as text `—`, `▢`, `✕` (different fonts, different glyph heights). | `Views/MainWindow.xaml` |
| Navigation | Tabs implemented as `RadioButton` + `Visibility` via `IntToVisibilityConverter`: all 5 pages exist in the visual tree simultaneously, no transitions, no icons, no back stack. | `Views/MainWindow.xaml`, `Helpers/Converters.cs` |
| Composition | Single XAML file ~500 lines, dozens of inline styles and `Margin` values, duplicated provider blocks (the "Provider" and "Environment" tabs contain identical forms). | `Views/MainWindow.xaml` |
| Code‑behind | ~40 `Click` handlers in `MainWindow.xaml.cs`, manual `PasswordBox` synchronisation, manual `Visibility` toggling for reveal/hide. No `ICommand`. | `Views/MainWindow.xaml.cs` |
| Visual noise | Badges like "NOUS ENGINE", "Engine: TanStack / Node", "LIVE INSTALLATION LOG • REDACTED/COPY-SAFE OUTPUT EXPECTED", tooltips such as "Loading: checks in progress. Success: green badge…" – these are developer‑oriented debug text, not user‑facing. | `Views/MainWindow.xaml`, `Services/ServiceMonitor.cs` |
| Icons | No icons at all except for the emblem. Statuses are conveyed only via a coloured dot and uppercase text. | — |
| Typography | Mix of sizes 9/10/11/12/13/14/16/18/24 without a scale; `SectionEyebrow` 10 px bold in accent color is used everywhere as the section header. | `Themes/HermesTheme.xaml` |
| Color | Palette hardcoded (`#08111F`, `#38BDF8` …), no light theme, no high‑contrast, system accent not used. Obsolete aliases `AccentYellow*` from an old palette remain. | `Themes/HermesTheme.xaml` |
| States | No skeleton/shimmer during loading, no empty‑state illustrations; installation errors are shown as a floating panel over the terminal. | `Views/MainWindow.xaml` |
| Dialogs | Native `MessageBox` for Clean Reinstall / Full Uninstall / Apply Update – they fall out of the application’s style. | `Views/MainWindow.xaml.cs` |
| Notifications | Single toast (`ToastHost`), each new message overwrites the previous one, timers overlap. | `ViewModels/MainViewModel.cs` |
| Terminal | Install log in a `TextBox` with green text; no level‑based highlighting, no filtering, no anchored auto‑scroll, no copy of selection. | `Views/MainWindow.xaml` |
| QR | White card 280×280 with no padding for DPI; no "Save PNG" button, no "Full Screen" for scanning from a distance. | `Views/MainWindow.xaml` |
| Env editor | List of `Key / TextBox`; secrets are masked in code in `Loaded`, but they are effectively not editable. | `Views/MainWindow.xaml.cs` |
| Dashboard | Service cards in a fixed‑size `UniformGrid 2×2`; at wide window sizes it leaves empty space, at narrow sizes buttons get clipped. | `Views/MainWindow.xaml` |
| Animations | Only fade+translate 180 ms when switching tabs; no hover animations, no progress animation, no `ConnectedAnimation`. | `Views/MainWindow.xaml.cs` |

---

## 3. References

### 3.1. Design Systems and Guidelines

- **Fluent 2 Design System** – tokens, typography, elevation: https://fluent2.microsoft.design/
- **Windows 11 design principles** (layout, Mica, iconography, motion): https://learn.microsoft.com/windows/apps/design/
- **Windows 11 Signature experiences** (Mica, rounded corners, Snap Layouts): https://learn.microsoft.com/windows/apps/design/signature-experiences/design-principles
- **Segoe Fluent Icons** (system icon font for Windows 11): https://learn.microsoft.com/windows/apps/design/style/segoe-fluent-icons-font
- **WinUI 3 Gallery** (reference for controls and patterns, installable from Microsoft Store): https://github.com/microsoft/WinUI-Gallery
- **Figma: Windows UI Kit** (official Fluent kit for mockups): https://www.figma.com/community/file/1159947337437047524

### 3.2. Libraries for WPF

| Library | What it provides | Link |
|---|---|---|
| **WPF UI (lepoco)** – recommended | Fluent 2 controls, `NavigationView`, `FluentWindow` with Mica/Acrylic, `SnackbarPresenter`, `ContentDialog`, `InfoBar`, `SymbolIcon` (Segoe Fluent), light/dark theme, system accent | https://github.com/lepoco/wpfui , https://wpfui.lepo.co/ |
| ModernWpf | WinUI‑like styles, lighter than WPF UI, but less active | https://github.com/Kinnara/ModernWpf |
| MaterialDesignInXAML | Material 3, if we decide to move away from Fluent | https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit |
| HandyControl | Many ready‑made controls (Notification, Drawer, StepBar for wizards) | https://github.com/HandyOrg/HandyControl |
| AdonisUI | Lightweight theme with layers/elevation | https://github.com/benruehl/adonis-ui |
| .NET 9 WPF Fluent theme (built‑in) | `ThemeMode="System"` in `App.xaml`, Fluent styles without third‑party packages; requires net9.0-windows | https://learn.microsoft.com/dotnet/desktop/wpf/whats-new/net90 |
| CommunityToolkit.Mvvm | `ObservableObject`, `[RelayCommand]`, `[ObservableProperty]`, messenger – remove code‑behind | https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/ |
| LiveCharts2 | Sparkline for service latency on the card | https://livecharts.dev/ |
| XamlAnimatedGif / Lottie (SkiaSharp) | Animated empty‑state and success illustrations | https://github.com/XamlAnimatedGif/XamlAnimatedGif , https://github.com/mono/SkiaSharp |

### 3.3. Reference Applications (what to look at and why)

- **Xbox App for Windows** – left `NavigationView`, large cards, Mica background, game installation state with progress. https://www.xbox.com/apps/xbox-app-for-windows-pc
- **Docker Desktop** – container dashboard: rows with status, Start/Stop/Restart actions inline, search, Logs/Inspect tabs. https://www.docker.com/products/docker-desktop/
- **LM Studio** – local LLM services: model cards, server tab with port and request log, dark theme with a single accent. https://lmstudio.ai/
- **Jan** – open‑source alternative, clean layout for provider settings and API keys. https://jan.ai/
- **Tailscale for Windows** – minimal tray client, node statuses, copy IP on click. https://tailscale.com/download/windows
- **Windows Terminal → Settings UI** – a reference for settings pages in WPF/WinUI: groups, `SettingsCard`, expander. https://github.com/microsoft/terminal
- **Files App** – open‑source WinUI 3 file manager, good example of Mica, subtle separators and adaptive columns. https://files.community/
- **DevToys** – compact tool cards and tool search. https://devtoys.app/
- **Epic Games Launcher / Battle.net** – pattern of "one big Install/Launch button + progress", hero area at the top.

### 3.4. Galleries for Visual Inspiration

- Dribbble, search "desktop app dark dashboard", "launcher ui", "devops dashboard": https://dribbble.com/search/desktop-app-dark-dashboard
- Behance: https://www.behance.net/search/projects?search=desktop%20dashboard%20dark
- Mobbin (real product screens, includes desktop section): https://mobbin.com/
- Godly (landing pages and product UI): https://godly.website/
- Page Flows (onboarding/setup wizard scenarios): https://pageflows.com/

### 3.5. Colors, Fonts, Icons

- **Radix Colors** – 12‑step scales for dark/light with guaranteed contrast: https://www.radix-ui.com/colors
- **Tailwind palette** (already partially used: `#38BDF8` = sky‑400, `#34D399` = emerald‑400): https://tailwindcss.com/docs/customizing-colors
- **Fluent UI System Icons** (SVG/font, same style as Segoe Fluent): https://github.com/microsoft/fluentui-system-icons
- **Lucide** – alternative outline icon set: https://lucide.dev/
- **Inter** (UI font, if we don’t want to depend on Segoe UI Variable): https://rsms.me/inter/
- **Cascadia Code / JetBrains Mono** for the terminal: https://github.com/microsoft/cascadia-code , https://www.jetbrains.com/lp/mono/
- **WCAG 2.2 contrast** (AA ≥ 4.5:1 for text): https://www.w3.org/WAI/WCAG22/quickref/#contrast-minimum
- Contrast checker: https://webaim.org/resources/contrastchecker/

### 3.6. Tools

- **Snoop** – WPF visual tree inspector: https://github.com/snoopwpf/snoopwpf
- **XAML Styler** – XAML formatting: https://github.com/Xavalon/XamlStyler
- **Accessibility Insights for Windows** – UIA tree and contrast checks: https://accessibilityinsights.io/
- **Hot Reload XAML** in Visual Studio 2022+.

---

## 4. Target Design System

### 4.1. Tokens

Extract into `Themes/Tokens.Dark.xaml`, `Themes/Tokens.Light.xaml`, `Themes/Tokens.HighContrast.xaml`. Below is the dark theme (based on Radix `slate`/`sky`).

```text
Layers
  Layer/Base          #0B1220   window background (replaced by transparent under Mica)
  Layer/Sidebar       #0F172A
  Layer/Card          #111C31
  Layer/CardHover     #172640
  Layer/CardSelected  #1B2D4B
  Layer/Input         #0D1627
  Layer/Terminal      #070C16
  Layer/Overlay       #CC0B1220

Strokes
  Stroke/Subtle       #1E293B
  Stroke/Default      #2B3B55
  Stroke/Focus        = Accent/Default

Accent – defaults to the Windows system accent, fallback sky
  Accent/Default      #38BDF8
  Accent/Hover        #7DD3FC
  Accent/Pressed      #0284C7
  Accent/OnAccent     #03101A
  Accent/Subtle       #12324A   (badge backgrounds, nav highlight)

Text
  Text/Primary        #F1F5F9
  Text/Secondary      #B4C0D3
  Text/Tertiary       #7C8CA3
  Text/Disabled       #526073

Semantic (status only, never decorative)
  Success             #34D399 / background #0F2A22
  Warning             #FBBF24 / background #2A1F0C
  Danger              #F87171 / background #2A1119
  Info                #60A5FA / background #0F1F38
  Cancelled           #A78BFA / background #1E1733
  Typography (Segoe UI Variable, fallback Inter):

Role	Size / Weight	Usage
Display	28 / SemiBold	Hero title on Dashboard
Title	20 / SemiBold	Page title
Subtitle	16 / SemiBold	Card title
Body Strong	14 / SemiBold	Service name, env key
Body	14 / Regular	Main text
Caption	12 / Regular	Descriptions, labels
Overline	11 / SemiBold, tracking +0.06em, Text/Tertiary	Section headers (replaces accent‑coloured SectionEyebrow 10 px)
Code	13 / Cascadia Code	Terminal, URL, IP
Spacing: scale 4‑8‑12‑16‑24‑32‑48. Card internal padding 20, card spacing 16, page margins 32.

Radii: controls 6, cards 12, modal dialogs 16, window – system (Windows 11).

Elevation: card – no shadow, 1 px Stroke/Subtle; flyout panels – DropShadow blur 24, opacity 0.35.

Motion: 120 ms (hover), 200 ms (page transition, CubicEase), 320 ms (dialog appearance). Respect SystemParameters.ClientAreaAnimation and the Windows "Show animations" setting.

4.2. Components (new UserControls / styles)
Component	Replaces	Description
FluentWindow (WPF UI) + TitleBar	Custom —▢✕ title bar	System buttons, Snap Layouts, Mica. Title bar includes: emblem, "Hermes", global status chip, search (Ctrl+K), theme toggle.
NavigationView	RadioButton tabs	Items with icons: Dashboard, Pairing, Install, Console, Settings. At bottom: About/Updates. Collapses to icons when width < 1100.
ServiceCard	Inline DataTemplate	Service icon, name, port chip, status chip with icon (Checkmark, Dismiss, Clock, Warning), latency sparkline (60 s), Start/Stop/Restart/Open buttons via CommandBar style (icon + tooltip), expandable Expander with the latest diagnostic message.
StatusChip	StatusPill	Variants: Success/Warning/Danger/Info/Neutral, icon + text, uniform height 24 px.
SecretField	PasswordBox + TextBox + Show button	Single control: mask, reveal button with eye icon, Copy, "hide after 30 s", warning when revealed.
SettingsCard / SettingsExpander	PleasantOptionRow	Icon, title, description, control on the right (Toggle/ComboBox/Button), like Windows Settings.
StepIndicator	4 RadioButton wizard steps	Horizontal stepper with numbers, checkmarks for completed steps, and progress line.
TerminalView	TextBox log	Virtualised ListView of lines: colour by level (Info/Warning/Error/Step), level filter, search, "stick to bottom" with a ↓ new lines button, copy selection, export to file.
EmptyState	Missing	Illustration (SVG/Path), title, description, primary action. Used for: no services, no logs, not installed.
Skeleton	Missing	Shimmer placeholders for cards during the first probe.
InfoBar (WPF UI)	Floating error panel	Success/Warning/Error bar with actions (Open report, Retry, Send).
ContentDialog (WPF UI)	MessageBox	Dialogs for Clean Reinstall / Full Uninstall with a checkbox "I understand", Apply Update.
SnackbarPresenter (WPF UI)	ToastHost	Notification queue, icon, Undo/Open action.
QrPanel	Image in white frame	QR with logo in the centre, buttons "Full Screen", "Save PNG", "Print", expiration timer ring around the QR.
HeroHeader	"Services Overview" card	Large system status (icon + "All running / Partial / Stopped"), 3 KPIs (online, latency, uptime), primary CTA.
5. UI Architecture
text
launcher/
  App.xaml                     theme, WPF UI resources, ThemeService
  Views/
    MainWindow.xaml            FluentWindow + NavigationView + Frame
    Pages/
      DashboardPage.xaml
      PairingPage.xaml
      InstallPage.xaml          host for the wizard
        Steps/DiagnosticsStep.xaml
        Steps/ComponentsStep.xaml
        Steps/ProviderStep.xaml
        Steps/DeployStep.xaml
      ConsolePage.xaml
      SettingsPage.xaml         Environment, Provider, Autostart, Theme, About
    Controls/
      ServiceCard.xaml, StatusChip.xaml, SecretField.xaml, SettingsCard.xaml,
      StepIndicator.xaml, TerminalView.xaml, EmptyState.xaml, Skeleton.xaml, QrPanel.xaml, HeroHeader.xaml
    Dialogs/
      ConfirmDestructiveDialog.xaml, UpdateDialog.xaml
  ViewModels/
    ShellViewModel.cs           navigation, global status, theme
    DashboardViewModel.cs, PairingViewModel.cs, InstallWizardViewModel.cs,
    ConsoleViewModel.cs, SettingsViewModel.cs, ServiceCardViewModel.cs
  Services/UI/
    INavigationService, IDialogService, ISnackbarService, IThemeService, IClipboardService
  Themes/
    Tokens.Dark.xaml, Tokens.Light.xaml, Tokens.HighContrast.xaml, Typography.xaml, Controls.xaml
Principles:

MVVM via CommunityToolkit.Mvvm: [ObservableProperty], [RelayCommand], IMessenger for installer events. Code‑behind remains only for purely visual concerns (animation, focus).

One page = one ViewModel; the current MainViewModel (~1000 lines) is split by page; common state (IsInstalled, PreferredHost, secrets) goes to ShellViewModel/AppState.

Navigation via Frame + INavigationService with EntranceNavigationTransition animation.

Secrets are never directly bound to Text: only through SecretField, which stores a SecureString/string internally and exposes Reveal/Copy events.

Theme: IThemeService listens to SystemEvents.UserPreferenceChanged, modes System/Dark/Light/HighContrast, accent from SystemParameters.WindowGlassColor (Windows) with fallback to sky.

Localisation: strings in Resources/Strings.ru.resx, Strings.en.resx; currently all UI is in English while the Inno Setup wizard targets a Russian audience.

6. Screens
6.1. Shell
FluentWindow, Mica (Windows 11) / solid Layer/Base (Windows 10).

Title bar: emblem 20 px, "Hermes Workspace", global StatusChip to the right ("All running · 4/4"), "Check for updates" button with indicator dot, theme toggle.

NavigationView on the left: Dashboard, Pairing, Install/Repair, Console, Settings; at the bottom, a network card ("Tailscale 100.x.y.z", click to copy) and launcher version.

Keyboard shortcuts: Ctrl+1..5 pages, Ctrl+K command palette (Start all, Stop all, Open workspace, Copy IP, Regenerate QR), F5 refresh status, Ctrl+L go to Console.

6.2. Dashboard
HeroHeader: large status icon, title "All services are running", subtitle "Gateway, Workspace, Agent Dashboard, Ollama", KPIs: Online 4/4, Latency 12 ms, Uptime 02:14:33. CTAs: "Open Workspace" (primary), "Restart all", "Stop all" (danger, secondary).

Grid of ServiceCard via WrapPanel/UniformGrid with adaptive columns (1/2/3 based on width), card minimum width 320.

Bottom banner "Connect your phone" only if the QR has not yet been scanned (flag in settings), otherwise hidden.

States: skeleton during the first probe; if not installed – EmptyState "Hermes is not installed" with "Start installation" button.

6.3. Pairing
Left: QrPanel 320 px wide: QR with emblem in the centre, expiration ring timer, buttons "Full screen", "Save PNG".

Right: connection steps (1. Tailscale on phone, 2. Open Hermes, 3. Scan) as a numbered list with icons; InfoBar Warning "The code contains credentials"; two SecretFields (Deep link, Raw code); "Expiration" group as a SegmentedControl (24 h / 7 d / 30 d / never) instead of four buttons; "Revoke and issue new" button (danger, via ContentDialog).

If Tailscale is not connected: InfoBar Info with "Open Tailscale" button.

6.4. Install / Repair (wizard)
StepIndicator at the top: Diagnostics → Components → Provider → Deployment.

Diagnostics: list of SettingsCard with tool icon, version and StatusChip; for missing ones – "Install" (winget) button inline; "Re‑check" button.

Components: SettingsExpander for each module (Tailscale, Ollama, MemOS with nested Local/Provider choice and fields, Obsidian + separate Skills toggle, Firewall, "Launch after install", "Auto‑start on login"). Paths with "Browse…" button (FolderBrowserDialog) and validation (exists, not empty/git).

Provider: SegmentedControl for presets, SecretField for the key, "Test connection" button (/models), result shown as StatusChip.

Deployment: TerminalView at full height, progress bar with current step and estimated time above; on completion – full‑screen Success state (illustration, "Open Workspace", "Show QR") / Failure (InfoBar Error, "Open report", "Retry", "Send report" as opt‑in) / Cancelled.

Clean Reinstall / Full Uninstall buttons are moved to Settings → "Maintenance" (danger zone), not adjacent to "Start Deployment".

6.5. Console
Left: list of log files (install.log, gateway-*.log, workspace-*.log) with size and modification time; right: TerminalView with highlighting, level filter, search, "Follow file" (tail -f), "Open in folder".

6.6. Settings
Sections: General (theme, language, launcher auto‑start, check for updates), Model Provider, Environment Variables (table with SecretField for secret keys, Add/Remove buttons, Save with diff of changes), Network (preferred host, firewall profiles), Maintenance (Clean Reinstall, Full Uninstall, Export diagnostics), About (version, OSS licences, links).

7. States and Micro‑interactions
Event	Behaviour
First probe	Skeleton cards for 600 ms, then fade‑in
Service goes online ← offline	Chip colour changes with cross‑fade 200 ms, Checkmark icon appears with scale 0.8→1
Start/Stop	Button becomes spinner inside, card gets a thin animated progress bar at the top
Installation	Progress bar with step indicator, new terminal lines are highlighted for 400 ms
Success	Illustration + confetti particles for 1 s (disabled via setting)
Error	InfoBar slides in from the top of the terminal, not blocking the entire screen
Copy	Snackbar "Copied" + Copy icon changes to Checkmark for 1.2 s
Reveal secret	Field gets Warning outline, auto‑hide after 30 s with countdown in tooltip
Card hover	Lift: stroke → Accent/Default, background → Layer/CardHover, 120 ms
8. Accessibility
All interactive elements: AutomationProperties.Name, visible focus 2 px Accent/Default, Tab order matches visual order.

Text contrast ≥ 4.5:1 (check Text/Tertiary on Layer/Card: current #7C8CA3 on #0E192B ≈ 4.6:1 – keep; Text/Disabled only for disabled).

Statuses are duplicated with both icon and text, not only colour.

High Contrast support: SystemParameters.HighContrast → theme Tokens.HighContrast.xaml with system colours.

Windows text scale (Settings → Accessibility → Text size) is respected: no fixed Height on text containers.

Screen reader: LiveSetting=Polite for status updates, Assertive for installation errors.

9. Resources
Icons: SymbolIcon from WPF UI (Segoe Fluent Icons). Mapping: Dashboard Grid, Pairing QrCode, Install ArrowDownload, Console WindowConsole, Settings Settings, Gateway Server, Workspace Globe, Agent Bot, Ollama BrainCircuit, Start Play, Stop Stop, Restart ArrowSync, Open Open, Copy Copy, Reveal Eye/EyeOff, Regenerate ArrowCounterclockwise.

Illustrations empty/success/error: 3 SVG line‑art illustrations in accent colour (Assets/Illustrations/*.xaml as DrawingImage).

Logo: update hermes-emblem.png to 256×256 with transparency and add .ico with sizes 16/24/32/48/256.

10. Implementation Plan
Phase	Content	Outcome	Estimate
0. Preparation	Update TFM to net8.0‑windows (or net9 for built‑in Fluent), add WPF‑UI, CommunityToolkit.Mvvm; enable XAML Styler; take baseline screenshots of current UI	Build green, no visual change	0.5 day
1. Tokens and theme	Tokens.Dark/Light/HighContrast.xaml, Typography.xaml, IThemeService, theme toggle; remove AccentYellow* aliases	All existing screens use new tokens, light theme works	1.5 days
2. Shell	FluentWindow + TitleBar + NavigationView + Frame; INavigationService; keyboard shortcuts	System window buttons, Snap Layouts, Mica, animated navigation	1.5 days
3. Base controls	StatusChip, SecretField, SettingsCard, EmptyState, Skeleton, SnackbarPresenter, ContentDialog (replace all MessageBoxes)	Control library + DevGalleryPage (Debug only)	2 days
4. Dashboard	HeroHeader, ServiceCard with sparkline, adaptive grid, skeleton/empty	Completed dashboard	2 days
5. Install wizard	StepIndicator, 4 step pages, TerminalView, Success/Failure/Cancelled states, move danger actions to Settings	Completed wizard	3 days
6. Pairing	QrPanel, steps, SegmentedControl for expiry, real secret rotation (see BUG_AUDIT item 15)	Completed page	1.5 days
7. Console + Settings	Log list, TerminalView tail, env editor with SecretField, settings sections, About	Completed pages	2 days
8. Polish	Animations, ru/en localisation, Accessibility Insights run, DPI 100/150/200 tests, screenshots in README	Release candidate	2 days
Total ≈ 16 working days. Phases 1–3 can run in parallel with fixing P0/P1 issues from BUG_AUDIT_RU.md; phases 4–7 depend on splitting MainViewModel (BUG_AUDIT items 12, 13).

11. Definition of Done
□ No MessageBox, no WindowStyle=None, no RadioButton navigation.
□ MainWindow.xaml < 150 lines; no single XAML file > 400 lines.
□ All colours and sizes only via tokens; grep '#[0-9A-Fa-f]{6}' Views/ is empty.
□ Light, dark and high‑contrast themes switch at runtime without restart.
□ Every page has empty/loading/error states, covered by DevGalleryPage.
□ Secrets are displayed only via SecretField; grep 'Text="{Binding .*Key' Views/ is empty.
□ Accessibility Insights: 0 errors at Error level; contrast AA.
□ Window works correctly at 980×680 and 3840×2160, DPI 100–200%.
□ Screenshots of the 5 pages are in docs/screenshots/ and in the README.
□ UI strings are in .resx, Russian and English localisation.
12. Quick Wins (can be done before the major redesign, 1 day)
Replace the text‑based —▢✕ with SymbolIcon or switch to the system title bar.

Remove developer‑oriented labels ("NOUS ENGINE", "REDACTED/COPY-SAFE OUTPUT EXPECTED", state descriptions for developers).

Introduce Overline style instead of SectionEyebrow (11 px, Text/Tertiary, tracking) – accent colour only on active elements.

Add icons to navigation items and status chips via Segoe Fluent Icons (FontFamily="Segoe Fluent Icons", glyphs \uE80F, \uED14, \uE896, \uE756, \uE713).

ItemsControl for cards → WrapPanel with MinWidth=320 to make the grid adapt to width.

Replace MessageBox with ContentDialog from WPF UI.

Toast queue instead of a single ToastHost.