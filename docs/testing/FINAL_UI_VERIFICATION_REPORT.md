# FINAL UI VERIFICATION, VISUAL POLISH & REGRESSION HARDENING — GAMERHUB

**Date:** 2026-10-11
**Scope:** Focused verification pass on the existing Claymorphism redesign. No new redesign phase; only demonstrably defective or materially inconsistent elements were changed.
**Companion to:** `CLAYMORPHISM_UI_REDESIGN_REPORT.md` (the redesign record — left intact as a historical document).

---

## 1. Verdict

**VERIFIED WITH LIMITATIONS**

Every reachable page, dialog and shell surface now verified: build is clean (0 warnings / 0 errors), **169/169 tests pass** (110 pre-existing + 3 from the redesign + **56 new**), the published single-file artifact launches and renders all 8 pages at 3 window sizes (24/24 captures, 0 problems), the functional probe scores **12/13** with its one FAIL root-caused to a race in the probe itself (clean shutdown re-measured at 485–791 ms across three runs), the Add Game dialog was opened and captured in its open state, and the palette now meets WCAG AA on every foreground/background pair the views actually use.

The limitations are integration-scope only, all listed in §13: real Windows power-plan activation is `NOT TESTED` at the OS level (not authorized; no system state was touched — independently confirmed in §12), the in-game overlay cannot be exercised because it is unreachable dead code, and no human aesthetic review was available (substituted with measured contrast + pixel analysis, and disclosed rather than claimed).

Four real defects were found and fixed that the redesign's own verification had not reached — including **two template crashes** that only fire when a control instantiates its template, and a **false-negative bug in the existing theme regression lint** that had been masking exactly that class of failure.

---

## 2. Repository baseline and pre-existing modifications

| Item | State |
|---|---|
| Repository root | `E:\Projects\Game Hub` |
| Branch / history | `main`, 1 commit (`544e0c6 first commit`) |
| Working tree | **65 modified + ~50 untracked paths** — all pre-existing uncommitted work from Phases 2–7, the stabilization rounds and the Claymorphism redesign |
| Committed by this pass | **Nothing.** No `git add`, `git commit`, `git reset`, `git stash`, or destructive clean was run. |
| Build configuration | `net8.0-windows`, WPF, .NET 8, `DellGamingControlCenter.sln` (Core / Services / App / Tests) |
| Release artifact | `build/release/DellGamingControlCenter.exe` — self-contained, single-file, `DebugType=none` |
| Test harness | xunit 2.5.3 + Moq 4.20.70; assembly already sets `[assembly: CollectionBehavior(DisableTestParallelization = true)]` (`TestConfig.cs`) |
| Runtime target | 2C/4T laptop, dark-only theme (no light theme exists) |

The only pre-existing test file touched was `ThemeResourceTests.cs`, and only to **strengthen** it (§7.4). No existing test was deleted, weakened, skipped, or bypassed: the 110 tests that passed before this pass still pass unchanged.

---

## 3. Files changed and why

### Production source (2 files)

| File | Change | Why |
|---|---|---|
| `Themes/DarkGamingTheme.xaml` | 5 edits | D1–D3, D8 below |
| `ViewModels/OverlayViewModel.cs` | 1 edit (honesty only) | D5–D7 below |

No view XAML, no command, no data model, no service, no converter, no code-behind, and no security path was modified.

### Tests added (6 files, 56 test cases)

| File | Cases | Covers |
|---|---|---|
| `PerformanceModeUiTests.cs` | 11 | Mode→enum mapping, in-flight honesty, success/failure, unsupported capabilities |
| `PerformanceModeMappingTests.cs` | 12 | Every visible mode → correct scheme GUID through the **real** service |
| `WpfSurfaceRuntimeTests.cs` | 8 | Drop-down popup open state, themed ToolTip, button template sealing, overlay contract |
| `OverlayViewModelTests.cs` | 4 | Overlay telemetry honesty |
| `ClaymorphismContrastTests.cs` | 20 | WCAG AA contrast + clay depth ordering, parsed from the live theme |
| `ThemeResourceTests.cs` | +1 | Whole-app XAML resource-integrity lint |

### QA harness added

| File | Purpose |
|---|---|
| `qa/capture_dialogs.ps1` | Opens the app's modal dialogs and captures their **open state** — the gap `capture_screens.ps1` cannot cover (it only sees the main window rect) |

Scratch tools (`tmp/`) are untracked and gitignored.

---

## 4. Performance-mode UI verification

### Traced path

`PerformanceView` mode card `ACTIVATE` → `PerformanceViewModel.ActivateModeCommand` (`AsyncRelayCommand`) → `ExecuteActivateModeAsync` → `IChangePerformanceModeUseCase.ExecuteAsync` → `AuthorizationService.Authorize` (audit-logged) → `PerformanceModeService.ApplyModeAsync` → `WindowsPowerPlanService.SetPowerSchemeAsync` → `powercfg /setactive <GUID>` → read-back.

### Results

| Criterion | Result | Evidence |
|---|---|---|
| Each visible mode maps to the correct enum + command | **PASS** | `Modes_PresentExactlyTheFiveRealEnumValues_InDisplayOrder` — exactly QUIET→`Quiet`, BATTERY SAVER→`BatterySaver`, BALANCED→`Balanced`, GAMING→`Gaming`, MAXIMUM PERFORMANCE→`MaximumPerformance`, each bound to `ActivateModeCommand` with its own enum as `CommandParameter` |
| Each mode maps to the correct scheme GUID | **PASS** | `EachVisibleMode_RequestsTheCorrectSchemeGuid` (5 cases) drives the **real** `PerformanceModeService` against a scripted `IWindowsPowerPlanService` and asserts the exact GUID handed to Windows |
| Loading state correct | **PASS** | `ActivateMode_InFlight_ShowsApplying_AndDoesNotClaimSuccessOrMoveHighlight` — held mid-flight on a `TaskCompletionSource`; while pending the status reads `Applying Gaming...`, never `Applied`, and the highlight has not moved |
| Success state correct | **PASS** | `ActivateMode_Success_ReportsApplied_AndReReadsActiveMode` — status `Applied` only after the backend confirms, then the active mode is **re-read** |
| Failure states correct | **PASS** | `ActivateMode_Failure_ReportsMessage_AndKeepsPreviousHighlight` (3 cases: `OperationFailed`, `NotAuthorized`, `NotSupported`) — `Failed: <message>`, never `Applied`, highlight stays on the backend-confirmed mode |
| Non-mode parameter ignored | **PASS** | `ActivateMode_NonModeParameter_IsIgnored_WithoutFalseSuccess` — no use-case call, no status |
| **No UI state falsely implies a system change before success** | **PASS** | See in-flight test above |
| **Displayed active mode ≠ requested mode until confirmed** | **PASS** | Highlight moves only in `LoadCurrentMode()`, which runs only after `result.IsSuccess` |
| Unsupported features stay explicitly unsupported | **PASS** | `Capabilities_KeepUnsupportedFeaturesExplicitlyUnsupported` — CPU Frequency, GPU Temperature, Fan Control, Thermal Control, Battery Threshold all render `Unsupported`; no control offered |
| Authorization + audit logging intact | **PASS** | `SecurityPolicyTests` (pre-existing, unchanged) still pass; failure path exercised through `NotAuthorized` |
| Session restoration logic unchanged | **PASS** | `RestorePreviousModeAsync` untouched; `FirstApply_SnapshotsTheOriginalScheme_ForLaterRestore` (5 cases) + the pre-existing `HardwareControlTruthTests` (R7-B) pass |
| No system setting left modified | **PASS** | See §12 — independently confirmed |
| **UI automation inspects the controls without activating** | **PASS** | `tmp/nav_probe.ps1` opens the Performance page over the real published artifact and inspects the mode cards **without clicking any ACTIVATE button**: `PERFORMANCE activate-buttons-present` **PASS**, `PERFORMANCE active-mode-activate-disabled` **PASS** (the backend-confirmed active mode's button is disabled, so the UI does not offer to re-apply it), `PERFORMANCE capability-matrix-present` **PASS**. This is the brief's preferred strategy (3) — safe UI automation with zero system changes. |

### Known limitation (pre-existing, not a defect introduced here)

Windows exposes only **three** power schemes for the **five** UI cards, so two pairs share a GUID: Quiet/BatterySaver → `a1841308-…`, Gaming/MaximumPerformance → `8c5e7fda-…`. The service therefore reports the scheme's canonical mode rather than the alias that was requested. This is documented in `HARDWARE_TUNING_VERIFICATION.md` §5.1 and is inherent to Windows, not to this code. `ModesSharingScheme` locks the contract honestly: the **scheme** is asserted exactly, and the label must name a mode that genuinely runs that scheme.

---

## 5. Was real Windows power-plan activation tested?

**No — `NOT TESTED` at the OS-integration level, by design.**

`powercfg` was never invoked to *change* anything in this pass. The only calls made were read-only (`/getactivescheme`, `/list`), used to confirm non-interference in §11.

Deliberately **not** executed, per the brief's instruction not to switch power plans without specific authorization:

- `qa/audit_functional.ps1` mode-activation steps
- `qa/verify_power_ui.ps1` (its whole purpose is to click ACTIVATE and read back with `powercfg`)
- `PerformanceModeService`'s B6 `powercfg /duplicatescheme` fallback — running it would **create a real Windows power scheme**

What *is* verified, and what it is worth:

- **Mock/simulated:** all of §4. These prove the UI routes correctly, does not lie about state, and requests the right GUID. They do **not** prove Windows accepted a change.
- **Real OS, prior authorized run:** `HARDWARE_TUNING_VERIFICATION.md` and `ROUND7_BASELINE.md` record live end-to-end activation of all four modes with independent `powercfg` read-back, plus restore verification. That evidence is unchanged by this pass and was not re-run.

No registry key, GPU preference, process priority or other system setting was modified.

---

## 6. Dropdown and popup inventory and results

### Inventory (complete)

| Surface type | Count in app | Notes |
|---|---|---|
| `ComboBox` | **0** | No view instantiates one. The themed `ComboBox`/`ComboBoxItem` styles exist as design-system assets only. |
| `Popup` | 1 (in the ComboBox template) | Only reachable via a ComboBox, therefore never shown |
| `ContextMenu` | 1 | WinForms `ContextMenuStrip` on the tray icon — OS chrome, outside the WPF theme |
| `Menu` / `MenuItem` | 0 | — |
| Autocomplete / suggestion panel | 0 | Global search is a plain `TextBox` with a `TextChanged` handler |
| `ToolTip` | 8 | Dashboard ×3, Game Center ×2, Settings ×1, MainWindow ×2 |
| Modal dialogs | 4 declared | GameEditorWindow, ProfileEditorWindow, SessionReportWindow, SmartOptimizerWindow |

**Important correction to the redesign report:** §9 item 4 of that document said the ComboBox popup "was reviewed in code". It is worth stating plainly that **no ComboBox exists anywhere in the application**, so there was no drop-down to capture. The gap was real but smaller than described — and the themed popup turned out to hide a defect (D2) that a screenshot could never have revealed.

### Results

| Check | Result | Evidence |
|---|---|---|
| Drop-down opens, themed surface, no template-resource crash | **PASS** | `ComboBox_DropDownOpens_WithThemedPopupSurface_AndNoTemplateResourceCrash` — real `ComboBox`, template instantiated, `PART_Popup` opens, popup `Border` background resolves to `#29243B`, selected item resolves to `TextPrimaryBrush` |
| ToolTip uses the clay surface, not system colours | **PASS** | `ToolTip_UsesTheClaySurface_NotTheWindowsSystemColours` — background `#29243B`, foreground `#F4F0FF` |
| Background/foreground contrast in popups | **PASS** | §8 contrast table; popup surface is a card tone, all text pairs ≥ 4.58:1 |
| Selected / hover / disabled item styling | **PASS** | ComboBoxItem triggers verified at runtime (highlight → `AppClayHighlight`, selected → `AppAccentDim` + light text, disabled → muted) |
| Popup placement, Z-order, clipping, dimensions, scrolling, keyboard nav | **PASS (structural)** | Popup is `Placement="Bottom"`, `AllowsTransparency`, `MinWidth=ActualWidth`, `MaxHeight=MaxDropDownHeight`, `VerticalScrollBarVisibility="Auto"`, `KeyboardNavigation.DirectionalNavigation="Contained"` — all present in the template and asserted indirectly by the runtime test. Not visually captured, because the control is never instantiated by the app. |
| Dialog open state captured | **PASS** | `qa/capture_dialogs.ps1` → Add Game dialog opened, **55 enabled controls**, captured to `tmp/dialogs/game_editor_open.png`, closed cleanly, shell verified intact afterwards |
| Profile / Session Report dialog open state | **NOT TESTED** | ProfileEditor needs a per-game PROFILE affordance the probe could not reach; SessionReport needs a completed session. Neither is reachable without user data. |

### Defects found and fixed here

**D1 — unthemed ToolTip (confirmed).** WPF paints an unstyled `ToolTip` with **system colours**. On a light Windows theme every one of the 8 tooltips would drop a bright white box onto the obsidian surfaces. The theme defined no `ToolTip` style (verified: zero `TargetType="ToolTip"` / `ContextMenu` / `MenuItem` styles existed). Fixed by adding a global `ToolTip` style in the shared resource system using existing tokens — no one-off overrides, no template, no shadow cost.

**D2 — ComboBox popup forward reference (confirmed, latent).** The popup `Border` referenced `{StaticResource AppBackgroundTertiary}`, a key declared **200 lines later** in the same dictionary. It resolves only because a `ControlTemplate` defers its content — the same fragility the redesign report listed as a crash class. Replaced with `{StaticResource RaisedSurfaceBrush}`, the semantic token for the identical colour (`#29243B`). Zero visual change; forward reference removed.

---

## 7. Overlay verification

### What the overlay actually is

Established by inspection, not inference: `OverlayWindow` is a **regular transparent, click-through, topmost WPF desktop window**. It sets `WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW` in `OnSourceInitialized` and draws nothing but WPF shapes. It is **not** an injected DirectX/Vulkan surface, and the code says so in an explicit comment.

### The decisive finding: it is unreachable

`OverlayWindow` and `OverlayViewModel` are **never instantiated anywhere in the application**. Confirmed by repo-wide search (only their own declarations match) and corroborated by `STABILIZATION_BASELINE.md` B13 and `STABILIZATION_REPORT.md` (line 115), which already recorded them as dead code.

Therefore the overlay **cannot be opened during a gaming session, or at all**, in the shipped application. It is not a case of "needs a game" — there is no call site.

### Results

| Criterion | Result | Evidence |
|---|---|---|
| Overlay creation/closure does not crash the app | **PASS (declared config)** | `OverlayWindow_DeclaresItsDesktopOverlayContract_…` locks `WindowStyle=None`, `AllowsTransparency=True`, `Background=Transparent`, `Topmost=True`, `ShowInTaskbar=False`, `IsHitTestVisible=False`, plus the code-behind's `WS_EX_TRANSPARENT`/`WS_EX_TOOLWINDOW`/`GWL_EXSTYLE`/`SystemParameters.WorkArea` |
| Runtime window instantiation | **BLOCKED** | See note below |
| Uses genuine available data | **PASS (fixed)** | `AvailableTelemetry_SurfacesTheRealReadings` — CPU `42%`, GPU `87%`, RAM `8.0 GB`, temp `61°C`, FPS `144` |
| Missing telemetry stays unavailable, never invented | **PASS (fixed)** | `UnavailableTelemetry_RendersAsNa_NeverAsMinusOneOrZero` |
| Closing does not terminate unrelated services | **PASS (by construction)** | It has no event subscriptions, no service references, no owner, and no code path connects it to session monitoring |
| No leaked subscriptions / orphaned windows / bad dispatcher access | **PASS (by construction)** | `OverlayViewModel` holds no events or timers; `OverlayWindow` is a plain `Window` with no statics |
| Fullscreen / in-game behaviour | **NOT TESTED** | No game session, and no code path exists to show the overlay during one. **No claim is made that it works inside fullscreen DirectX/Vulkan games.** |

> **Why runtime instantiation was not forced:** constructing `OverlayWindow` requires application-scope resources (it resolves `AppAccentSecondary` et al. from `Application.Resources`). Creating a WPF `Application` inside the test host sets the AppDomain-wide `Application.Current`; that leaked into unrelated tests and broke the pre-existing `GameCenterViewModelTests.PlayCommand_RejectsIfSessionAlreadyActive` via `Application.MainWindow` cross-thread access. That was reproduced and then removed. The overlay's resource safety is instead covered deterministically by the whole-app lint (§7.4).

### Defects found and fixed (D5–D7)

`OverlayViewModel.UpdateFromSnapshot` violated the application's own honesty contract in three ways:

| ID | Problem | Fix |
|---|---|---|
| **D5** | `CpuText = $"{UsagePercent}%"` printed **`-1%`** whenever the sampler reported its `-1` "unavailable" sentinel | Sentinel-aware → `N/A` |
| **D6** | `GpuText`/`TempText` were **hardcoded** `"N/A"`, so real GPU usage and temperatures were never shown — the overlay did not use genuine available data | Bound to `GpuMetrics.UsagePercent` and `CpuMetrics.TemperatureCelsius ?? GpuMetrics.TemperatureCelsius`, falling back to `N/A` |
| **D7** | `RamText` had no `TotalBytes > 0` guard, so an unknown system read back as a confident **`0.0 GB`**; `FpsText` was only ever written when a sample arrived, leaving a **stale FPS** on screen | Guard added; FPS now overwritten every snapshot |

`UnavailableText` (`"N/A"`) is now a single named constant so the sentinel is defined once.

---

## 8. Visual review findings and corrections

### Disclosure

**No human visual-inspection facility was available for this pass.** A human aesthetic review was *not* performed and is not claimed. Instead, the measurable parts of visual quality were measured: WCAG 2.1 contrast computed from the live theme tokens, and pixel analysis of the actual captures. The results are objective; the subjective "does it look premium" judgement remains unperformed.

### 8.1 Contrast — measured, then locked down

All 20 foreground/background pairs the views actually render were computed with the WCAG 2.1 relative-luminance formula, reading the hex values straight out of `DarkGamingTheme.xaml` so the numbers cannot drift:

| Foreground | Background | Ratio | Need | |
|---|---|---|---|---|
| `TextPrimary` `#F4F0FF` | app background `#171521` | 16.09 | 4.5 | PASS |
| `TextPrimary` | card `#211E30` | 14.50 | 4.5 | PASS |
| `TextPrimary` | raised `#29243B` | 13.30 | 4.5 | PASS |
| `TextPrimary` | accent fill `#4C3F7A` | 8.16 | 4.5 | PASS |
| `TextSecondary` `#B8B2CC` | background / card / raised | 8.82 / 7.95 / 7.29 | 4.5 | PASS |
| **`TextMuted` (was `#89839F`)** | **raised `#29243B`** | **4.12** | **4.5** | **FAIL** |
| **`TextMuted` (was `#89839F`)** | **card `#211E30`** | **4.49** | **4.5** | **FAIL** |
| `AppBackground` on `PrimaryAccent` `#A78BFA` | primary button label | 6.62 | 4.5 | PASS |
| `AppBackground` on `AccentHover` `#C4B5FD` | button hover | 9.76 | 4.5 | PASS |
| `SecondaryAccent` `#C4B5FD` | background | 9.76 | 4.5 | PASS |
| `Success` / `Warning` / `Error` | card | 11.57 / 11.26 / 8.59 | 4.5 | PASS |
| `Telemetry` `#67E8F9` | inset `#191725` | 12.17 | 4.5 | PASS |
| `Telemetry` | background | 12.43 | 4.5 | PASS |
| Card edge `#3D3552` / lit edge `#4A4066` | background | 1.57 / 1.90 | 1.3 | PASS (decorative) |

**D8 — muted metadata text below AA (confirmed, material).** `AppTextMutedColor` set the 10–11px metadata/footnote text used in **all 9 view files (37 references)** — including the "Power impact:" / "Intent:" labels and the "Unsupported entries are reported…" honesty note on the Performance page. At `#89839F` it measured **4.12:1 on raised cards** and **4.49:1 on cards**, both under AA for normal-size text.

Fix: **`#89839F` → `#918BA7`** — the smallest hue-preserving change (4/255 per channel) that clears AA on all three real surfaces, taking the worst case from 4.12 → **4.58:1**. The token keeps its violet-grey character; the change is documented inline in the theme.

**Borders were measured and deliberately left alone.** Card edges sit at 1.57:1. WCAG 1.4.11 requires 3:1 only for boundaries that are the *sole* means of identifying a control; here every control is identified by its label (all ≥ 4.58:1) and these edges are claymorphic extrusion decoration. Restyling them would be exactly the broad redesign this pass forbids. They are asserted at a 1.3:1 floor so cards can never become visually indistinguishable.

### 8.2 Clay depth ordering — a new invariant

The claymorphism effect is carried entirely by small luminance steps, so a retune could silently invert or flatten it across the whole UI. `SurfaceTones_KeepTheClayDepthOrdering` now locks:

- app background `#171521` < card `#211E30` < raised `#29243B` (extrusion)
- inset `#191725` < card `#211E30` (recession — wells are dark because they sit *inside* cards)
- card↔background and raised↔card steps both < 2.0 (restrained, no harsh banding)

### 8.3 Defects considered and **not** changed

| ID | Page / control | Severity | Problem | Decision |
|---|---|---|---|---|
| **V1** | Game Center library art tile (268×136, `Stretch="UniformToFill"`) | LOW | `D:\…\NFS14.exe` exists and its icon is light-coloured; the small icon is upscaled into the hero tile, producing a bright, soft-edged block. Measured: **10,279 near-white pixels in a 516×167 band**, identical across all three window sizes (a fixed-size element). The sibling profile (`D:\Games\NFS.exe`, missing) correctly falls back to a generated magenta gradient with white initials. | **Not fixed — documented.** This is the game's own icon, so the *content* is honest; only the upscale is poor. It is **pre-existing** (`GameArtService` predates the redesign and was untouched by it) and fixing it means changing which artwork the app chooses to display — a content decision, not a styling bug. **Proposed correction:** in `GameArtService.TryLoadExeIcon`, accept the icon only when it provides adequate resolution (≥64px), otherwise fall through to the generated gradient. One small, presentation-only change. |
| — | Tray context menu | INFO | WinForms, OS-rendered | Out of scope for a WPF theme |
| — | Light theme | INFO | None exists | Unchanged; tokens are structured semantically so a light dictionary can merge without touching views |

No subjective large-scale restyling was performed. The palette, radius scale, spacing and component set are unchanged.

---

## 9. Regression and architecture protection

| Criterion | Result |
|---|---|
| All pages navigable | **PASS** — 8/8 nav clicks succeeded at all 3 sizes |
| MVVM bindings intact | **PASS** — no view XAML was edited in this pass; every binding predates it and all bindings were already verified in the redesign |
| Commands call the correct logic | **PASS** — §4; no command or `CanExecute` modified |
| Authorization checks present | **PASS** — `SecurityPolicyTests` unchanged and passing; failure path exercised via `NotAuthorized` |
| Hardware capability states honest | **PASS** — `Capabilities_KeepUnsupportedFeaturesExplicitlyUnsupported`; `Unsupported` still shown with an explicit "No control is offered for them" note |
| Telemetry from actual providers | **PASS** — `StabilizationRegressionTests` (battery/CPU/storage sentinels) unchanged and passing; D5–D7 made the overlay obey the same sentinel contract |
| Config + session persistence unchanged | **PASS** — `ConfigurationService`, `AppModels` untouched |
| Game launch + session restoration unchanged | **PASS** — `GameLauncher`, `GameSessionManager`, `PolicyEvaluator`, `RestorePreviousModeAsync` untouched |
| No existing test deleted, weakened or bypassed | **PASS** — 110 pre-existing tests unchanged; one lint **strengthened** (§7.4) |
| Shared theme resources load at runtime | **PASS** — 8 runtime WPF tests instantiate real templates on STA threads |
| No new missing or duplicate resource keys | **PASS** — new whole-app lint |

### 7.4 — A real bug in the existing regression lint

`ThemeNever_AssignsColorResources_ToBrushProperties` (from the redesign) had a **false negative**. Its key extraction ran to the attribute's closing quote instead of the markup extension's closing brace, so for the form `Property="Background" Value="{StaticResource XColor}"/>` it compared the string `"XColor}/"` against `EndsWith("Color")` — which never matches. The test passed while **two genuine Color-on-Brush crashes sat in the theme**.

Fixed to stop at `}`, and a new companion test `EveryResourceReferencedByAnyAppXaml_IsDefinedSomewhere` walks every App XAML file and requires each `{StaticResource}`/`{DynamicResource}` key to exist at application scope or locally — reaching the deferred-template references that a dictionary parse cannot see.

**Verified non-vacuous:** with D3 temporarily reverted, both guards fire —

```
DarkGamingTheme.xaml line 408: Background assigned Color resource 'AppInsetSurfaceColor' (needs the matching Brush)
System.ArgumentException : '#FF191725' is not a valid value for the
  'System.Windows.Controls.Panel.Background' property on a Setter.
```

### The two template crashes (D3)

**Both were latent, both are fixed.**

| Location | Setter | Consequence if triggered |
|---|---|---|
| `ClayNavButton` template, `IsPressed` | `TargetName="Pill"` `Background` → `{StaticResource AppInsetSurfaceColor}` (**a `Color`**) | `ArgumentException` on template seal — nav buttons are on every page |
| `ActionButtonStyle` template, `IsPressed` | `TargetName="Root"` `Background` → `{StaticResource AppInsetSurfaceColor}` | `ArgumentException` — this is the primary action button (ACTIVATE, PLAY, SAVE…) |

`Border.Background`/`Panel.Background` are Brush-typed; assigning a `Color` throws when the template seals. Both replaced with `InsetSurfaceBrush` — the same colour (`#191725`), correct type. **Zero visual change.**

The whole-app lint caught this deterministically at build time; `ThemedButtonTemplates_SealAndDrive_WithoutATypeCrash` reproduces it at runtime against the four themed button styles.

---

## 10. Build and test results

```
dotnet build DellGamingControlCenter.sln -c Debug --nologo
  → Build succeeded. 0 Warning(s), 0 Error(s). Exit code 0.

dotnet test tests/.../DellGamingControlCenter.Tests.csproj -c Debug --no-build
  → Passed! Failed: 0, Passed: 169, Skipped: 0, Total: 169. Exit code 0.

dotnet publish src/.../DellGamingControlCenter.App.csproj -c Release -r win-x64
    --self-contained true -p:PublishSingleFile=true -p:DebugType=none -o build/release
  → Exit code 0. Single-file 72.27 MB DellGamingControlCenter.exe (same release shape).
```

**Test accounting:** 110 pre-existing + 3 from the redesign + 56 new = **169**. No test was removed.

| Suite | Cases |
|---|---|
| Pre-existing (unchanged) | 110 |
| `ThemeResourceTests` (redesign) | 3 |
| `ThemeResourceTests` (new lint) | 1 |
| `PerformanceModeUiTests` | 11 |
| `PerformanceModeMappingTests` | 12 |
| `WpfSurfaceRuntimeTests` | 8 |
| `OverlayViewModelTests` | 4 |
| `ClaymorphismContrastTests` | 20 |

---

## 11. Screenshot and capture QA

| Harness | Result |
|---|---|
| `qa/capture_screens.ps1` (existing) | **24/24 captures verified, 0 problems**, exit 0 — 8 pages × 1000×650 / 1200×750 / 1496×776; every nav click succeeded, every page marker found, every capture passed the non-flat paint probe |
| `qa/capture_dialogs.ps1` (**new**) | **1 dialog capture, 0 problems**, exit 0 — Game Center → ADD GAME → `Add Game` dialog opened (**55 enabled controls**), captured to `tmp/dialogs/game_editor_open.png`, closed, shell verified intact |
| Pixel analysis (`tmp/analyse_shots.ps1`) | 26 captures analysed |
| `tmp/nav_probe.ps1` (existing functional probe) | **12/13 PASS** — startup, title, VIEW DETAILS→Diagnostics, **Diagnostics auto-run on entry (CPU read `38%` live, not the `N/A` sentinel)**, findings-or-empty-state, CHANGE→Performance, capability matrix, ACTIVATE buttons present with the active one disabled, Game Center marker, ADD GAME dialog opens **and** cancels |

### Palette conformance — the retuned token is verifiably live

`#918BA7` (D8) appears in **every one of the 24 page captures and both dialog captures** (113–233 sampled pixels per page), proving the published artifact ships the fix rather than a stale theme.

### One finding, resolved as a false positive

The automated "system-white" check flagged the Game Center page at all three sizes (2,553 sampled white pixels). Investigated to a conclusion rather than dismissed:

- All white pixels fall inside a single band, `(242,279)–(758,446)`.
- That band is **47% saturated gradient, 12% white, 6% dark** — i.e. artwork, not a panel.
- The white is two things: the **white `initials`** `GameArtService` deliberately draws in `Brushes.White` on the generated gradient tiles, and the **light-coloured `NFS14.exe` icon** (the file exists, 30 MB) upscaled into its tile — defect **V1** in §8.3.
- **Zero white pixels exist anywhere outside that band.** No unthemed WPF control is present on any page.

All other 23 captures: 0 system-white pixels, 3,225–9,051 unique sampled colours each — consistent with the live obsidian/violet palette plus gradients.

### Clean close — the one probe FAIL, root-caused

`nav_probe.ps1` reported `CLOSE clean-close : FAIL`. Rather than carry the redesign report's earlier conclusion forward untested, shutdown was measured directly on the current artifact with a stopwatch around `CloseMainWindow`, across three independent launches:

| Run | Result | Elapsed |
|---|---|---|
| 1 | exited | **485 ms** |
| 2 | exited | **791 ms** |
| 3 | exited | **552 ms** |

The application shuts down cleanly every time, well inside the probe's 5-second budget. The FAIL is a **race in the probe's own exit check**, not an application defect — the probe polls `$proc.HasExited` against a stale `Process` object captured at launch. Shutdown behaviour is therefore **PASS**; the probe script itself is the thing that is imprecise. (Left unmodified, since `tmp/` is scratch and editing a probe to make it pass would defeat its purpose.)

---

## 12. Was any machine state changed?

**No.** Independently verified, not assumed.

| Check | Result |
|---|---|
| `powercfg /getactivescheme` (read-only) | `a1841308-3541-4fab-bc81-f71556f20b4a (Power saver)` |
| `powercfg /list` scheme count (read-only) | **2** |
| Baseline recorded in `HARDWARE_TUNING_VERIFICATION.md` | `a1841308-… (Power saver)`, **2** schemes |
| **Match?** | **Yes — unchanged** |

`powercfg` was never invoked to modify anything. No registry key, GPU preference, process priority, power plan or other system setting was written. Test doubles were used throughout; the one code path that would create a real power scheme (B6 `duplicatescheme`) was deliberately never exercised. Nothing was committed.

The read-back was repeated **after** the screenshot harness, the dialog harness, the functional probe and three shutdown measurements — all of which launch and drive the real application. Scheme and scheme count were still `a1841308-… (Power saver)` / **2**. Notably `nav_probe.ps1` navigates to the Performance page and inspects the ACTIVATE buttons without ever pressing one, which is precisely why the plan stays undisturbed.

---

## 13. Remaining defects and unverified scenarios

### Confirmed, not fixed

| ID | Item | Severity |
|---|---|---|
| **V1** | Low-resolution exe icon upscaled into the 268×136 Game Center art tile (§8.3) — proposed one-line resolution floor in `GameArtService.TryLoadExeIcon` | LOW |
| — | 5 UI modes share 3 Windows schemes, so Quiet/BatterySaver and Gaming/MaximumPerformance are indistinguishable from the active GUID (§4) | INFO — inherent to Windows, documented |

### Unverified scenarios (explicitly `NOT TESTED` / `BLOCKED`)

| Item | Status | Reason |
|---|---|---|
| Real Windows power-plan activation at OS level | **NOT TESTED** | Not authorized; would change system settings. UI + simulated behaviour verified separately (§4). |
| B6 `powercfg /duplicatescheme` fallback | **NOT TESTED** | Would create a real Windows power scheme |
| In-game / fullscreen overlay behaviour | **BLOCKED** | `OverlayWindow` is never instantiated — unreachable dead code |
| Overlay runtime window instantiation | **BLOCKED** | Requires an app-scope `Application`, which leaks into and breaks unrelated tests (§7) |
| ProfileEditor / SessionReport dialog open state | **NOT TESTED** | Need per-game UI affordances / a completed session that the automated probe cannot produce |
| ToolTip open state in a capture | **PARTIAL** | Cannot be held open reliably via UIA; covered by unit test instead |
| Popup placement near window edges, at all 3 sizes | **PARTIAL** | Structural template properties verified; no ComboBox exists to position |
| Human aesthetic review | **NOT PERFORMED** | No facility available — disclosed, not claimed. Substituted with measured contrast (§8.1) and pixel analysis (§11). |
| Light theme | N/A | None exists to verify |

---

## 14. Summary

This pass did not restyle anything. It instrumented the redesign and, in doing so, found four defects the previous verification could not have reached:

1. **Two template crashes** (nav buttons and primary action buttons) that only fire when a control instantiates its template — invisible to the XAML compiler and to a dictionary parse.
2. **A false-negative in the regression lint** designed to catch exactly that class, which had been reporting clean while the crashes sat in the theme.
3. **An unthemed ToolTip** that would have rendered as a bright system-white box on every one of the 8 tooltips.
4. **Three overlay honesty violations** that invented `-1%` and `0.0 GB`, hid real GPU and temperature data, and left stale FPS on screen.

Plus one measurable accessibility failure — muted metadata text at 4.12:1 against raised cards — corrected to 4.58:1 with the smallest possible hue-preserving token change and locked down by a 20-case contrast suite parsed from the live theme.

All of it verified end-to-end: 169/169 tests, 0 warnings, 0 errors, 24/24 page captures, a newly captured dialog open state, and an independent read-back confirming the machine's power configuration was left exactly as found.
