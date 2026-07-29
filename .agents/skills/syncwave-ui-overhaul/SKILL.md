---
name: syncwave-ui-overhaul
description: Use this whenever working on SyncWave's visual styling, main window chrome, system tray behavior, the quick-volume flyout, or theme handling. Trigger for anything mentioning "Acrylic", "Mica", "system tray", "tray icon", "flyout", "Advanced settings", "light/dark theme", "WPF-UI", "H.NotifyIcon", or "Windows 11 native" in this repo. Consult this BEFORE proposing a different tray/UI library or a different flyout-positioning approach — those decisions are already made below and some alternatives have been deliberately rejected.
---

# SyncWave — Windows 11-Native UI Overhaul

## Project context

SyncWave (C#/.NET 8, WPF) currently has a custom-styled dark theme built
from scratch (hand-rolled `CheckBox`/`Slider` styles, fixed dark colors,
no system theme awareness, no tray behavior — closing/minimizing just
minimizes to the taskbar like any default WPF window).

Goal: make the app feel like a native Windows 11 app, not a third-party
skin pretending to be one — Acrylic/Mica backdrop, follows the user's
system light/dark setting, minimizes to the system tray, and offers a
quick-volume flyout from the tray icon similar to Windows' own Quick
Settings/volume popups.

## Decisions already made — do not relitigate

**Component libraries:**
- **WPF-UI** (`lepoco/wpfui`) for the main window — provides
  `FluentWindow` with native `WindowBackdropType.Mica`/`Acrylic`
  support (calls the real DWM APIs, not a hand-rolled approximation)
  and Fluent-styled controls. This is the base for the main
  settings/controls window.
- **H.NotifyIcon.Wpf** (`HavenDV/H.NotifyIcon`) for the system tray
  icon and the quick-volume flyout (`TaskbarIcon` + `TrayPopup`).
  Actively maintained, purpose-built for exactly this use case — do
  not reach for the older `Hardcodet.NotifyIcon.Wpf` or hand-rolled
  `System.Windows.Forms.NotifyIcon` interop instead.

**Theme:** follows the Windows system light/dark setting automatically
— both libraries support this natively (WPF-UI's theme watcher,
`H.NotifyIcon`'s `ContextMenuThemeMode="System"` equivalent for
popups). Do not hardcode dark-only.

**Volume range:** default slider max is **125%**, not 200%. A "Enable
audio boost (up to 200%)" toggle lives in an **Advanced** section of
the main window's settings and raises the max when switched on.

## Tray + flyout behavior

- **Minimizing the main window hides it from the taskbar entirely** —
  only the tray icon remains visible. Restore happens via the tray
  icon (e.g. double-click, or a "Open SyncWave" item in its context
  menu).
- **Clicking the tray icon opens a quick-volume flyout** — a small,
  borderless, Acrylic-backed popup (NOT the full main window) showing
  just the currently synced devices with a name and a volume slider
  each — no latency, no buffer, no advanced settings. Think "Windows'
  own per-app volume mixer flyout," not a shrunken version of the main
  window.
- **Positioning:** anchor the flyout using the standard tray-icon popup
  position (directly above the tray icon / near the click point) —
  this is what `H.NotifyIcon`'s `TrayPopup` gives you out of the box.
  Set the flyout window `Topmost="True"` so it always renders above
  other system surfaces (Action Center, the media-controls widget,
  etc.) regardless of whether they're open.

  **Explicitly rejected:** do NOT attempt to detect whether Windows'
  own Action Center/Quick Settings panel is currently open, or query
  its position, in order to reposition our flyout "above" it. That
  panel is a private system surface (`ShellExperienceHost`) with no
  supported, documented API for querying its state or bounds. The only
  way to attempt this would be enumerating top-level windows by
  undocumented internal class names — fragile, and liable to silently
  break on a future Windows update. `Topmost="True"` + standard
  tray-anchored positioning achieves the actual goal (our popup is
  never hidden behind Windows' own UI) without this fragility.

- **Light-dismiss behavior:** the flyout closes automatically 2 seconds
  after the mouse leaves it (or the window loses focus) — not
  instantly. Implement via a `DispatcherTimer` started on
  `MouseLeave`/`Deactivated`, cancelled if the mouse re-enters within
  that window, closing the popup when the timer elapses.

## Main window structure

- Full device list + controls (as it exists today) stays in the main
  window, restyled with WPF-UI's Fluent controls and Acrylic/Mica
  backdrop.
- Add an **Advanced** settings section containing: the 200% volume
  boost toggle, and (candidate, not yet decided — ask before adding)
  per-device latency/buffer tuning controls discussed earlier in the
  project but deferred to this phase.

## Files this will touch (expect to fill in once work starts)

- `Views/MainWindow.xaml` — restyle to `FluentWindow` w/ backdrop,
  reorganize into base + Advanced sections.
- New: a tray icon host (likely added to `App.xaml`/`App.xaml.cs`) and
  a new lightweight flyout window/view for the quick-volume popup.
- `SyncWave.csproj` — add `WPF-UI` and `H.NotifyIcon.Wpf` package
  references.
- `ViewModels/MainViewModel.cs` — likely needs a way to expose "just
  the synced devices + volume" subset for the flyout's simpler view,
  separate from the full device list used by the main window.

## Files to leave alone

- Everything in `Core/` (capture, output, latency) — this phase is UI
  only, no audio pipeline changes.
