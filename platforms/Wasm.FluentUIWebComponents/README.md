# MAUI / Fluent UI Web Components

An experimental, minimal .NET MAUI browser backend using NativeAOT LLVM, WebAssembly, and Fluent UI Web Components. This is currently a development starting point, not a complete or supported MAUI platform.

## Backend project

`src/Wasm.FluentUIWebComponents` provides the platform host, MAUI handlers, dispatcher, DOM interop, theme resources and browser assets. The backend is independent of any application UI. There is no Blazor renderer, Fluent Blazor, or `IJSRuntime`.

The backend is currently consumed through a project reference. Packaged releases are not yet available. A separate example application has its own [BrowserSample README](samples/BrowserSample/README.md), including application features, publishing and static deployment instructions.

## Application model

A consuming application uses ordinary MAUI startup registration:

```csharp
var builder = MauiApp.CreateBuilder(useDefaults: false);
builder.UseMauiApp<App>()
    .UseFluentUIWebComponents();
return builder.Build();
```

`BrowserApplication.Run(MauiProgram.CreateMauiApp)` supplies the platform host before the factory is called. Core defaults are disabled because native fonts, images and platform services cannot simply be inherited; Controls registration through `UseMauiApp<App>()` remains in place, followed by the browser handler overrides.

Applications using XAML should configure MAUI's **SourceGen inflator** (`Inflator="SourceGen"`) to generate constructors, setters and event subscriptions rather than depend on runtime XAML loading. The backend renders the resulting MAUI objects; it does not implement a separate XAML parser. Theme/resource bindings are supported, but general ViewModel/data-binding compatibility under NativeAOT is not yet established.

## Current capabilities

### Application host and layout

- Application and single Window host, `MauiContext`, scoped services and lifecycle.
- MAUI owns measurement and arrangement; the browser measures leaf content and applies MAUI frames in CSS pixels.
- Grid and stack layouts share MAUI's layout algorithms, including dynamic child updates. Root invalidations are coalesced to browser frames.

### Control handlers

Reuse a matching Fluent component first. Custom elements are reserved for behavior that needs a MAUI-specific browser container; ordinary layout containers do not need a new component at all.

| Integration | MAUI control | Browser implementation | Current scope |
| --- | --- | --- | --- |
| Direct Fluent mapping | Label | `fluent-text` | Text, wrapping, font and horizontal alignment |
| Direct Fluent mapping | Button | `fluent-button` | Click, text, enabled state, color and padding |
| Direct Fluent mapping | Entry | `fluent-text-input` | Text, focus, selection, input state, placeholder and keyboard hints |
| Custom MAUI component | ScrollView | `maui-scroll-view` | Vertical viewport, offsets and scroll notifications |
| Standard DOM container | ContentPage, Grid and stacks | `div` | Content ownership and MAUI layout |
| Standard DOM container | Border | `div` | Solid rectangular/rounded stroke and content padding |

The application/window handlers establish the root and its lifecycle rather than render additional Fluent controls. Individual handler XML documentation describes component mappings and compatibility adaptations; the table does not imply complete MAUI control parity.

### Platform services

- **Themes and resources:** browser system preference, MAUI application overrides and notifications, `AppThemeBinding`, Fluent tokens and dynamic palette resources.
- **Scheduling:** UI-thread dispatch, delayed work and dispatcher timers with cancellation and shutdown cleanup.
- **Animation:** a requestAnimationFrame ticker supplied to MAUI's `AnimationManager`; opacity animation is supported by the current view mapping.

These are selected browser platform services, not a complete Essentials implementation. See [limitations](#boundaries-and-known-limitations) for unsupported behavior and validation boundaries.

## Toolchain configuration

Building the managed backend requires a .NET SDK compatible with the target framework and NativeAOT LLVM packages, and Node/npm for the browser assets. Publishing consuming applications additionally requires a compatible NativeAOT compiler host and Emscripten toolchain. The values below describe the repository's current configuration, **not a minimum-version or exhaustive host-support matrix**.

| Component | Current configuration |
| --- | --- |
| .NET SDK | Stable .NET 10.0 SDK; [global.json](global.json) selects the latest installed 10.0 feature band (`latestFeature`) |
| MAUI | `10.0.41`, using repository central package management |
| NativeAOT LLVM compiler/runtime | `10.0.0-rc.1.26357.1` |
| Node | 22+, as declared in `package.json` |

NativeAOT LLVM's [compilation guide](https://github.com/dotnet/runtimelab/blob/9954350a58ede8b8eaaeb24112ca4f1e78cc527c/docs/using-nativeaot/compiling.md) describes compiler-host support, including Windows x64. The backend is not inherently Linux-only. A consuming application must select a matching compiler host package and native toolchain for publishing. Keep compiler and runtime package versions aligned; upstream support alone does not establish this backend's full host-support matrix.

The application targets `browser-wasm`; browser compatibility is separate from the operating system used to compile it. A complete browser support matrix has not yet been established.

The SDK is not locked to `10.0.112`. SDK selection allows newer stable 10.0 feature bands such as `10.0.401`, but not .NET 11 or preview SDKs. This does not change the pinned NativeAOT LLVM compiler/runtime packages.

NuGet sources are defined in [NuGet.config](NuGet.config), including the experimental compiler feed. NuGet dependencies use repository central package management; JavaScript dependencies and build tools are pinned in `package-lock.json`.

## Build the backend

Run from this directory:

```bash
npm ci --ignore-scripts
MSBuildEnableWorkloadResolver=false dotnet build src/Wasm.FluentUIWebComponents/Wasm.FluentUIWebComponents.csproj -c Release
```

This builds the managed library and its browser JS/declarations, not a runnable website. Assets are copied into consuming applications through the project reference. Publishing a runnable application additionally requires a compatible NativeAOT compiler host and Emscripten toolchain; see the [example application's build instructions](samples/BrowserSample/README.md#build-and-run).

## Browser assets and types

The browser bridge is implemented in TypeScript, with its public entry point in [maui-browser.ts](src/Wasm.FluentUIWebComponents/web/maui-browser.ts). MSBuild invokes the frontend build automatically: TypeScript checks the sources and emits declarations, then esbuild bundles the bridge and Fluent dependencies into a **self-contained ESM module**.

The project reference supplies three assets to the application output and publish directory:

- `maui-browser.js`: the browser bridge and Fluent implementation.
- `maui-browser.d.ts`: `startMaui` and the returned `MauiApplicationHandle`.
- `dotnet.d.ts`: the original declaration from the matching LLVM runtime package.

The application imports its own runtime's `dotnet.js` and passes its builder to `startMaui`. The runtime JS exports live in the backend assembly; consuming applications must include that assembly in `UnmanagedEntryPointsAssembly` to preserve the exports. Use the runtime assets supplied by the configured LLVM toolchain, not SDK/Blazor replacements.

Declarations become available after the first build. A standalone frontend build (`npm run build`) requires the backend's NuGet restore first. Consumers can type-check their bootstrap code against the generated public declarations; private implementation declarations are not distributed.

The backend uses explicit internal interop contracts and validates the managed export object. Runtime declarations may describe APIs beyond the subset supported by NativeAOT LLVM; their presence alone does not guarantee runtime support.

### Web Component contract

C# handlers select an already registered tag. A direct mapping such as `fluent-button` reuses Fluent's implementation; a custom element such as `maui-scroll-view` supplies MAUI-specific container behavior where no suitable Fluent component is used. Prefer reuse over recreating an existing control or adding a wrapper solely for naming consistency.

The internal interop distinguishes JavaScript **properties** (`textContent`, `disabled`), HTML **attributes** (`data-automation-id`, removed when the value is null), and CSS **styles**. Property writes target existing writable properties rather than creating arbitrary fields. This is a trusted handler interface, not a sandbox for untrusted markup or scripts. Managed code and JS assets must be rebuilt and deployed together after protocol changes.

In both cases, MAUI owns layout and the backend owns element lifetime. The bridge manages handles and DOM operations, not a second control-kind mapping. Component-specific input or style adaptations belong beside their handlers; see [BrowserEntryHandler](src/Wasm.FluentUIWebComponents/Handlers/BrowserEntryHandler.cs) for the input policy and the reasons for its adapter.

### Scheduling and animation

`Dispatcher.CreateTimer()` returns an `IDispatcherTimer` with one-shot/repeating behavior, cancellation and restart handling. Changing a running timer's interval restarts its countdown. Active timers are stopped when the dispatcher is disposed. `ITicker` is driven by rAF and supplied to the actual MAUI animation manager, rather than a parallel animation implementation. Browser background throttling still applies: timers are not precision clocks, rAF may pause, and MAUI uses elapsed wall time when animations resume. Worker-thread dispatch and system reduced-motion integration are not provided yet.

### Application themes

Use standard MAUI APIs: `Application.UserAppTheme = AppTheme.Unspecified` follows the browser's `prefers-color-scheme`; `AppTheme.Light` and `AppTheme.Dark` override it. The backend synchronizes the effective MAUI theme with Fluent tokens and supports MAUI's `AppThemeBinding` and `RequestedThemeChanged`. Applications choose how to expose theme controls and whether to use their own theme-bound colors or the backend palette.

MAUI's `AppTheme` enum contains only `Unspecified`, `Light` and `Dark`. High contrast is not an additional enum value. This backend does not yet integrate `forced-colors` or `prefers-contrast`; light/dark support does not imply high-contrast accessibility support.

The internal theme interop uses the explicit string values `"light"` and `"dark"`, not a Boolean or MAUI enum ordinal. This applies to system-theme queries/notifications, effective-theme application and palette reads. MAUI resolves `Unspecified` before sending an effective theme. Both sides reject unsupported values rather than silently treating a new theme as light or dark; adding a theme requires an explicit conversion and preset mapping. This replaces the earlier Boolean protocol, so the managed assembly and JS bundle must be rebuilt and deployed together.

The backend installs the system-theme provider before constructing the application so initial XAML evaluation sees the correct theme. Browser changes update MAUI's platform theme even under an explicit override. MAUI then propagates effective-theme changes to bindings, `RequestedThemeChanged`, and the application handler, which updates Fluent tokens. Selecting System uses the latest browser preference, including when the previous override had the same effective theme. Preferences are not persisted across reloads; applications can restore `UserAppTheme` before their `InitializeComponent()`.

Fluent tokens and CSS `color-scheme` are scoped to a dedicated light-DOM mount element inside `document.body`. Mounting on `html`/`body`, in a detached element, or inside another shadow root is rejected because Fluent's theme API cannot maintain that scope there. Keep the mount in the document, and let the backend own its theme while the app runs; surrounding host-page content is not themed. Disposal removes the media-query listener and the mount's local Fluent theme, restores its previous inline `color-scheme`, and restores the prior AppInfo provider if still owned. Fixed explicit colors remain fixed unless the application uses a theme binding.

**MAUI compatibility shim:** MAUI 10.0.41 reads `AppInfo.RequestedTheme` statically, but `AppInfo.SetCurrent` is internal. `BrowserAppInfo` uses an isolated reflection call with a targeted trimming preservation declaration for that entry point. Review it on MAUI upgrades. This provider implements theme only and delegates unrelated Essentials members to the previous provider; it does not add general browser Essentials support.

### Fluent color resources

Explicitly merge `FluentThemeResources` into application resources after the MAUI application and browser interop are initialized. The type lives in the backend's root namespace and assembly, `Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                    xmlns:fluent="clr-namespace:Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents;assembly=Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents">
    <ResourceDictionary.MergedDictionaries>
        <fluent:FluentThemeResources />
    </ResourceDictionary.MergedDictionaries>
</ResourceDictionary>
```

Consume entries with `DynamicResource`, for example `TextColor="{DynamicResource Fluent.ForegroundColor}"`. No custom resource-provider registration is required: this is an ordinary MAUI resource dictionary. `StaticResource` resolves a value without maintaining a resource-key subscription, so replacing the palette entry will not update that reference.

| Resource key (MAUI `Color`) | Fluent token |
| --- | --- |
| `Fluent.BrandForegroundColor` | `colorBrandForeground1` |
| `Fluent.ForegroundColor` | `colorNeutralForeground1` |
| `Fluent.MutedColor` | `colorNeutralForeground3` |
| `Fluent.CanvasColor` | `colorNeutralBackground3` |
| `Fluent.SurfaceColor` | `colorNeutralBackground1` |
| `Fluent.StrokeColor` | `colorNeutralStroke2` |

The dictionary reads the actual `webLightTheme`/`webDarkTheme` values through the backend's interop and refreshes on MAUI effective-theme changes; it does not maintain a separate C# copy of Fluent's hex values. Initial values use `Application.RequestedTheme`, including preferences set before `InitializeComponent()`. MAUI's weak theme event does not keep a removed dictionary alive.

These are backend-specific resources, not a cross-platform MAUI palette. Treat the dictionary's six entries as backend-owned; keep custom branding in separate application keys or explicit control colors. It exports only these preset colors, not all Fluent tokens or computed CSS overrides, and does not introduce a custom-theme API or the obsolete `ISystemResourcesProvider` interface.

## Boundaries and known limitations

- This is a deliberately small control/property surface, not general MAUI compatibility. Other controls and properties may be unsupported.
- No Shell, navigation stack, full Essentials, embedded font/image pipeline, Resizetizer, hot reload, full graphics, native menus, or multi-window behavior.
- No worker-thread UI dispatch or complete Entry/IME/data-binding compatibility claim yet. Timer/ticker wiring does not establish background timing guarantees or full animation-property coverage.
- Entry does not yet implement clear-button presentation, vertical text alignment, custom keyboard flags, automatic next-field navigation or complete text-transform semantics. Keyboard hints remain browser-dependent; real OS IME, autofill and accessibility behavior require further verification beyond synthetic event tests.
- ScrollView currently supports vertical user scrolling, not horizontal/both orientations or programmatic `ScrollToAsync`; unsupported requests fail explicitly.
- Border supports solid rectangular/rounded strokes, not arbitrary shape/dash/gradient rendering. Advanced text formatting, selection, truncation and full button input semantics are not complete.
- Standard binding infrastructure belongs to MAUI. Support for `AppThemeBinding` and palette `DynamicResource` updates does not establish compatibility for other data-binding scenarios; those need separate NativeAOT verification.
- NativeAOT LLVM loses original C# exception messages at the raw JS export boundary. The backend catches callback failures and reports the original diagnostic through `maui-error` on the host element and `console.error`.
- The JS bootstrap returns an object with `dispose()`; duplicate startup on the same page is rejected before replacing runtime imports. Hidden tabs can defer layout frames.
- The backend has not been validated for production accessibility, cross-browser parity, long-running memory behavior or performance budgets.

Full trimming and both AOT/trimming analyzers are enabled. Unused reflection-based implicit conversions and Shell query-property reflection are disabled through MAUI feature switches, not warning suppression. No entire MAUI assembly is preserved and no MAUI/runtime fork is required.

## Dependency changes

Follow the repository's dependency policies. Run `node ../../eng/scripts/audit-npm.mjs` for npm dependency or lockfile changes before opening a PR, and keep the npm project registered in `.github/dependabot.yml`.