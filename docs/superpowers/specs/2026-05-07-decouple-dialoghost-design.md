# Decouple DialogHost from ShadUI.Window

Date: 2026-05-07
Status: Approved (design)

## Goal

`DialogHost` should have no compile-time dependency on `ShadUI.Window`. It must work identically when hosted in a `ShadUI.Window`, a vanilla `Avalonia.Controls.Window`, a `UserControl`, or any `Panel`. Window-side features (drag, double-tap maximize, snap-layout suppression) activate automatically when an Avalonia `Window` ancestor is present and silently no-op when not.

## Current coupling

Inventoried in `src/ShadUI/Controls/Dialog/DialogHost.axaml.cs`:

| Site | Coupling |
| --- | --- |
| `OwnerProperty` (line 28) | Typed as `ShadUI.Window?`. |
| `OnTitleBarPointerPressed` (line 187) | Calls `desktop.MainWindow.BeginMoveDrag` — assumes the application's MainWindow is the visual host. |
| `OnMaximizeButtonClicked` (line 200) | Reads `Owner.CanMaximize`, sets `Owner.WindowState`. `CanMaximize` is on `Avalonia.Controls.Window`, so this is recoverable through the base type. |
| `CloseDialog` (line 219) | Writes `Owner.HasOpenDialog = false` (internal property on `ShadUI.Window`). |
| `ManagerOnDialogShown` (line 274) | Writes `Owner.HasOpenDialog = true`. |
| `ManagerOnDialogClosed` (line 288) | Writes `Owner.HasOpenDialog = ...`. |

Reverse direction: `ShadUI.Window` does **not** reference `DialogHost` by type today. It exposes a generic `Hosts: Controls` collection and reads its own `internal HasOpenDialog` field (set by DialogHost from across the boundary) inside the Win32 snap-layout WndProc.

After the refactor, the dependency direction flips: `ShadUI.Window` references `DialogHost`; `DialogHost` references only `Avalonia.Controls.Window`.

## Decisions

1. **Decoupling scope**: any container. DialogHost works inside any control tree, with Window-only features auto-activating when a `Window` ancestor exists.
2. **Snap-layout suppression**: dropped for non-`ShadUI.Window` hosts. Preserved for `ShadUI.Window` via Window-side discovery (Window observes DialogHost; DialogHost knows nothing).
3. **Title-bar drag / maximize**: resolved dynamically via `this.FindAncestorOfType<Avalonia.Controls.Window>()`. No-op when no Window ancestor.
4. **`Owner` property**: removed entirely. Breaking change.

## Component boundaries

| Component | Knows about |
| --- | --- |
| `DialogHost` | `Avalonia.Controls.Window` (base only), `DialogManager` |
| `ShadUI.Window` | `DialogHost` (one-way; subscribes to its `HasOpenDialog` changes) |
| `DialogManager` | unchanged |

## Changes — `src/ShadUI/Controls/Dialog/DialogHost.axaml.cs`

1. **Delete `OwnerProperty` and the `Owner` property.** All call sites below stop dereferencing `Owner`.
2. **Cache an ancestor `Avalonia.Controls.Window`.** Override `OnAttachedToVisualTree` to call `this.FindAncestorOfType<Avalonia.Controls.Window>()` and store it in a private field `_ownerWindow`. Clear it in `OnDetachedFromVisualTree`. Re-resolve lazily inside drag/maximize handlers if `_ownerWindow` is null at use time (covers cases where a dialog is shown before the host is attached).
3. **`OnTitleBarPointerPressed`**: replace the `Application.Current?.ApplicationLifetime` MainWindow lookup with `_ownerWindow?.BeginMoveDrag(e)`. No-op when null.
4. **`OnMaximizeButtonClicked`**: read `_ownerWindow?.CanMaximize` and toggle `_ownerWindow.WindowState`. No-op when null. (`CanMaximize` is a member of `Avalonia.Controls.Window`, so no ShadUI types are needed.)
5. **`CloseDialog`, `ManagerOnDialogShown`, `ManagerOnDialogClosed`**: delete the `Owner.HasOpenDialog = ...` writes. DialogHost continues to update its own `HasOpenDialog` styled property; that is now the single source of truth.

DialogHost's `HasOpenDialog` styled property stays `internal` (`ShadUI.Window` lives in the same assembly and can observe it).

## Changes — `src/ShadUI/Controls/Window/Window.axaml.cs`

1. **Subscribe to DialogHosts in `Hosts`**: in `OnLoaded`, iterate `Hosts` for `DialogHost` instances and attach a `PropertyChanged` observer on `DialogHost.HasOpenDialogProperty`. Also subscribe to `Hosts.CollectionChanged` so DialogHosts added/removed at runtime are tracked.
2. **Aggregate state**: when any subscribed DialogHost reports `HasOpenDialog == true`, set `this.HasOpenDialog = true`. Recompute on every change as `Hosts.OfType<DialogHost>().Any(h => h.HasOpenDialog)`.
3. **Cleanup**: detach all subscriptions in `OnUnloaded` to prevent leaks.

`Window.HasOpenDialog` stays `internal`. The Win32 snap-layout WndProc that already reads it (line 468 of `Window.axaml.cs`) is unchanged.

## Changes — `src/ShadUI.Demo/MainWindow.axaml`

Remove the `Owner="..."` binding from the `<shadui:DialogHost>` element inside `<shadui:Window.Hosts>`. Result:

```xml
<shadui:Window.Hosts>
    <shadui:DialogHost
        Manager="{Binding DialogManager}"
        x:Name="PART_DialogHost" />
</shadui:Window.Hosts>
```

## Changes — `src/ShadUI/Controls/Dialog/DialogHost.axaml`

No template changes. `PART_TitleBar` stays; it becomes a no-op when there is no Window ancestor.

## What is preserved

- `ShadUI.Window` UX: drag, maximize, snap-layout suppression, dialog overlay, animations.
- All existing public DialogHost styled properties except `Owner`.
- `DialogManager` API.

## What is lost (acceptable)

- Snap-layout suppression does not work when DialogHost is hosted outside a `ShadUI.Window` (vanilla Window, UserControl, Panel). The Window subclass is the only one that subscribes.
- Drag and double-tap maximize are no-ops when no `Avalonia.Controls.Window` ancestor exists (Panel/UserControl-only hosting).

## Breaking changes

- `DialogHost.Owner` property and `OwnerProperty` are removed.
- Consumers must delete any `Owner="..."` binding on `<shadui:DialogHost>`. XAML with the binding will fail to compile.
- Note this in CHANGELOG when shipping.

## Risks and edge cases

- **Late attachment**: `DialogManager.OnDialogShown` may fire before `OnAttachedToVisualTree`. The drag/maximize handlers re-resolve the ancestor lazily if the cache is null, so the first interaction still works.
- **DialogHost added at runtime**: handled by `Hosts.CollectionChanged` subscription on the Window side.
- **Multiple DialogHosts in one Window**: aggregate `Window.HasOpenDialog` is `Any(h => h.HasOpenDialog)` — correct for the snap-layout suppression intent.
- **DialogHost outside `Hosts` collection but inside the Window's visual tree**: the Window subscribes only to entries in `Hosts`. If a consumer puts DialogHost elsewhere in the tree, snap-layout suppression won't engage. Acceptable — `Hosts` is the documented integration point.

## Test plan

Manual verification in `ShadUI.Demo`:

1. Open the dialog page, trigger each dialog variant — overlay, dismiss, close button, drag header, double-tap header to maximize, escape key. All behave as before.
2. Maximize, then open dialog, then double-tap header — restores to normal as before.
3. On Windows: hover the maximize button while a dialog is open — snap-layout flyout must not appear (HasOpenDialog suppression still works).
4. Build the solution — no references to `DialogHost.Owner` remain.
