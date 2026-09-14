# SircleToSearch

**[English](README.md)** | [Русский](README.ru.md)

Android's "Circle to Search," brought to Windows — a background tray app that pulls up image search with one hotkey, no browser window, no idle overhead.

## How it works

1. `Win+Shift+Q` — a dimmed overlay covers the screen on top of a frozen screenshot.
2. Drag a rectangle around whatever you want to search — it stays editable: drag the box to move it, grab a corner to resize. The overlay doesn't disappear after one shot.
3. Release — the crop is sent to `google.com/searchbyimage/upload` (the same endpoint mobile Chrome uses for Google Lens), and the result opens in a compact popup docked bottom-right — not a browser tab.
4. `Enter` re-runs the search with the current selection, `Esc` closes everything.

Tray icon → right-click → **Settings** to enable launch-on-startup.

## Download

Grab the latest `SircleToSearch.exe` from the [Releases page](https://github.com/Yagon-Don/SircleToSearch/releases/latest) — it's a single self-contained file, nothing else to install beyond the WebView2 Runtime (preinstalled on Windows 11, and on most Windows 10 machines via Edge). Run it, and it'll sit in the tray.

## Stack

- .NET 8 / WPF — no Electron. Compiles to a single self-contained exe, near-zero footprint while idle.
- Global hotkey via `RegisterHotKey` (WinAPI), no polling.
- Screen capture via GDI (`CopyFromScreen`); cropping via a manual `Graphics.DrawImage` pass — `Bitmap.Clone(rect)` has a known GDI+ bug that corrupts pixels for some rectangles.
- Image search via `Microsoft.Web.WebView2` — the upload itself runs *inside* the WebView2 page via `fetch()`, so the uploaded image and the results page share one cookie/session context instead of a separate HTTP client's session going out of sync with the browser control's.
- The loading spinner is a from-scratch port of the real Material 3 shape-morphing loading indicator (shape data + spring/rotation constants), based on [Aler1x/m3-loading-indicator](https://github.com/Aler1x/m3-loading-indicator) (Apache-2.0).

## Not affiliated with Google

This is an independent tool that automates the public "search by image" upload flow the same way a browser does — there's no official Google Lens API for third-party apps. If Google changes that endpoint, the search step is the one function to fix.

---

made by Kiki 🐾
