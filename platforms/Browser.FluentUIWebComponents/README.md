# MAUI / Fluent UI Web Components

An experimental, minimal .NET MAUI browser backend using NativeAOT LLVM, WebAssembly, and Fluent UI Web Components. This is currently a development starting point, not a complete or supported MAUI platform.

## Consume the backend package

The package provides the platform host, MAUI handlers, dispatcher, DOM interop, theme resources and browser assets. The backend is independent of any application UI. There is no Blazor renderer, Fluent Blazor, or `IJSRuntime`.

There are **no released packages currently**. External consumption uses an experimental/local NuGet package from a feed you configure, not a source-checkout reference. In an application configured for the [required browser toolchain](#toolchain-configuration), add:

```xml
<ItemGroup>
    <PackageReference Include="Microsoft.Maui.Platforms.Browser.FluentUIWebComponents"
                      Version="$(MauiFluentBackendVersion)" />
</ItemGroup>
```

`MauiFluentBackendVersion` is an application-defined MSBuild property: set it to the exact experimental/local package version available from your feed. It is not a published-version claim or a property supplied by the backend. This reference does not by itself configure a complete NativeAOT LLVM application.

The `.nupkg` contains `buildTransitive/Microsoft.Maui.Platforms.Browser.FluentUIWebComponents.targets`. NuGet restore automatically imports it to register the three browser bridge files as `Content` and import the app asset rules. These rules copy declared MAUI resources and generate `maui-assets.json` for application output and publish directories. This is standard NuGet [MSBuild `build`/`buildTransitive` integration](https://learn.microsoft.com/en-us/nuget/concepts/msbuild-props-and-targets).

Inside this product directory, a source `ProjectReference` supplies the bridge files through normal `Content` items, while the product's `Directory.Build.targets` imports the app asset rules. No sample-local backend `<Import>` is needed. **A `ProjectReference` outside this product does not import those rules automatically**; use the experimental/local NuGet package for external applications. Do not use NuGet-cache paths or edit generated imports.

The [BrowserSample README](samples/BrowserSample/README.md) covers application features, publishing and static deployment, and explains its current checkout-dependent C# project. Backend source builds and repository setup belong in [CONTRIBUTING.md](CONTRIBUTING.md).

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
| Direct Fluent mapping | Image | `fluent-image` with an owned `img` | Packaged static files, explicit width and height, four MAUI Aspects |
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

Consuming applications require a .NET SDK compatible with the target framework and NativeAOT LLVM packages. Publishing additionally requires a matching NativeAOT compiler host and Emscripten toolchain; this package is not turnkey support for arbitrary .NET browser SDKs. Node/npm is required for the sample's TypeScript frontend build. The values below describe the current toolchain configuration, **not a minimum-version or exhaustive host-support matrix**.

| Component | Current configuration |
| --- | --- |
| .NET SDK | Stable .NET 10.0 SDK |
| MAUI | `10.0.41` |
| NativeAOT LLVM compiler/runtime | `10.0.0-rc.1.26357.1` |
| Emscripten | `3.1.57` |
| Node (sample frontend) | 22+ |

NativeAOT LLVM's [compilation guide](https://github.com/dotnet/runtimelab/blob/9954350a58ede8b8eaaeb24112ca4f1e78cc527c/docs/using-nativeaot/compiling.md) describes compiler-host support, including Windows x64. The backend is not inherently Linux-only. A consuming application must select a matching compiler host package and native toolchain for publishing. Keep compiler and runtime package versions aligned; upstream support alone does not establish this backend's full host-support matrix.

The application targets `browser-wasm`; browser compatibility is separate from the operating system used to compile it. A complete browser support matrix has not yet been established.

## Browser assets and types

The backend provides a **self-contained ESM module** containing the browser bridge and Fluent dependencies, with TypeScript declarations for its [application startup API](src/Browser.FluentUIWebComponents/web/maui-browser.ts).

The NuGet package supplies three assets to the application output and publish directory:

- `maui-browser.js`: the browser bridge and Fluent implementation.
- `maui-browser.d.ts`: `startMaui` and the returned `MauiApplicationHandle`.
- `dotnet.d.ts`: the original declaration from the matching LLVM runtime package.

The application's **publish directory is the frontend's input**, for both package and in-product source references. It also contains the generated `maui-assets.json` and declared resources under `maui-assets/`, as described below. Frontend tooling must not look into backend intermediate directories or the NuGet cache. The package targets generate/register app resources as well as supply bridge `Content`; they do not prepare application source files, compile application TypeScript or assemble a website.

The sample's npm pipeline publishes the MAUI module to `artifacts/maui`, then copies these three files beside its handwritten `web/` TypeScript for editor and compiler imports. These local copies are gitignored and should not be edited. TypeScript emits to `artifacts/typescript`; site assembly combines that output, handwritten `wwwroot/` assets and the entire MAUI publish directory into `artifacts/wwwroot`. The same frontend pipeline works with either reference type. See the [sample build instructions](samples/BrowserSample/README.md#build-and-run).

Standard `dotnet publish` produces the MAUI module's Wasm, runtime glue, backend JS and declarations, app resource catalog/files, plus symbols/metadata when emitted by the toolchain—not a complete website. It does not call the sample's npm scripts. Building a source-referenced backend still intentionally runs its own npm bundle build. Ordinary `dotnet build` compiles managed code and backend resources and copies app resources/catalog to build output; it does not perform the native Wasm publish.

The application imports its own runtime's `dotnet.js` and passes its builder to `startMaui`. The runtime JS exports live in the backend assembly; consuming applications must include that assembly in `UnmanagedEntryPointsAssembly` to preserve the exports. Use the runtime assets supplied by the configured LLVM toolchain, not SDK/Blazor replacements.

The browser interop contract is generated from the backend's C# declarations when the backend is built. The resulting browser bundle and public type declarations are packaged together; package consumers do not need to configure or run backend code generation. Frontend-owned preparation makes the published declarations available beside application sources to type-check bootstrap code. Private implementation declarations are not distributed.

Code generation is provided by [Browser.FluentUIWebComponents.CodeGen](tools/Browser.FluentUIWebComponents.CodeGen/README.md); its README contains implementation and testing details. Runtime declarations may describe APIs beyond the subset supported by NativeAOT LLVM; their presence alone does not guarantee runtime support.

## Packaged application resources

Use ordinary **app-head** `MauiImage`, `MauiFont` and `MauiAsset` items. Files are
copied unchanged, not embedded in Wasm or processed by Resizetizer. The backend
consumes items declared by the app; it does not add implicit `Resources` globs or
recursively collect assets from referenced projects, assemblies or NuGet libraries.
Explicit linked files declared in the app head are supported.

| Item | Supported files | Consumption |
| --- | --- | --- |
| `MauiImage` | Static PNG, JPG/JPEG, SVG | `Image.Source` file logical name; **both** `WidthRequest` and `HeightRequest` required |
| `MauiFont` | TTF, OTF | `ConfigureFonts` / `AddFont`, then `FontFamily` alias or registered filename on Label, Button and Entry |
| `MauiAsset` | Raw files, unchanged | Packing only; no browser `FileSystem` implementation or `OpenAppPackageFileAsync` support |

[BrowserSample.csproj](samples/BrowserSample/BrowserSample.csproj) shows local and
linked declarations; [MauiProgram.cs](samples/BrowserSample/MauiProgram.cs) registers
Open Sans. The sample uses no resource URLs, manual resource copies into `wwwroot`,
or application `@font-face` CSS.

### Logical names and metadata

- Image and font names are **flat filenames**: the final component of `Link` when
    supplied, otherwise the source filename. For example, an app item linking
    `../Shared/photo.jpg` as `Resources/Images/cover.jpg` is used as `cover.jpg`.
    `Link` may rename a file but cannot change its format/extension.
- An SVG named `sample_bot_vector.svg` has **two** image logical names:
    `sample_bot_vector.svg` and `sample_bot_vector.png`. Both resolve to the same unchanged
    `.svg` file. There is no generated PNG and no runtime extension guessing.
- `MauiFont` registers the file in the catalog; `AddFont("OpenSans-Regular.ttf",
    "OpenSansSample")` registers its application alias at runtime. An item metadata
    alias is not a replacement for `AddFont`.
- Raw names preserve subdirectories. `Link` takes precedence over `LogicalName`;
    without either, the item path is used (absolute paths are reduced to a
    project-relative path where possible, otherwise the filename). To remove
    `Resources/Raw/`, use the usual `MauiAsset` glob with
    `LogicalName="%(RecursiveDir)%(Filename)%(Extension)"`. `LogicalName` does not
    rename images or fonts.
- Lookup is case-sensitive; case-only collisions are rejected at build time.
    Image/font/raw namespaces are separate. Duplicate names, SVG alias collisions
    with real PNGs, unsafe paths and output file/directory conflicts fail the build.
    For example, do not declare both `logo.svg` and `logo.png` in the same app.
- `BaseSize`, `TintColor`, `Color`, `ForegroundFile`, `ForegroundScale` and explicit
    `Resize=true` are unsupported and
    produce build diagnostics. Omit transformation metadata or use `Resize=false`.
    There is no resize, recoloring, compositing, density generation or conversion.

### Image and font limits

Images support `AspectFit`, `AspectFill`, `Fill` and `Center` inside the MAUI frame.
`AspectFit` contains the image, `AspectFill` covers and crops, `Fill` stretches, and
`Center` centers without scaling and clips to the frame. Both requested dimensions
must be explicit; there is no intrinsic sizing or single-dimension inference.
MAUI still applies parent constraints and owns measure/arrange.

Only static packaged file sources are supported: no `UriImageSource`,
`StreamImageSource`, `FontImageSource`, animated GIF/APNG or `IsAnimationPlaying`,
ImageButton, Button image source, image transforms or Resizetizer processing.
SVGs are decoded by the browser, not rasterized by MAUI; native pixel equivalence
is not promised. Use trusted, self-contained SVGs; the catalog is not a sanitizer
and does not package external SVG dependencies.

Registered fonts load on first use, initially using a fallback. Completion requests
layout again so labels, buttons and entries can be remeasured. A failed font keeps
the fallback without an automatic retry loop. Fonts are not subsetted or converted
to webfont formats; assembly-embedded font discovery is not supported.
Explicit embedded-font registration reports a nonfatal unsupported-resource
diagnostic and leaves the fallback; it does not register a packaged-file alias.

### Catalog, deployment and failures

Build/publish automatically generate a versioned `maui-assets.json`, **even for an
app with no resources**. It records the application assembly name and logical-name
maps into `maui-assets/images/`, `maui-assets/fonts/` and `maui-assets/raw/`.
The pipeline tracks its outputs so removed/renamed resources do not remain in its
previous build/publish destinations; unrelated host files must not be overwritten.

`startMaui` loads and validates this mandatory catalog **before managed Main**.
The catalog and resource URLs are relative to the deployed `maui-browser.js`
module directory, not the current page route or site root. Preserve their relative
paths when deploying under a subdirectory. Do not hand-edit the catalog or infer
URLs from `Image.Source`.

Missing/malformed catalogs, unsupported schema versions, application mismatches
and SPA fallbacks returning HTML are startup/deployment failures, not an empty
resource set. A missing image/font entry, network failure or decode failure is
instead **nonfatal**: the mount emits `maui-resource-error` with a string diagnostic
in `event.detail`, and the backend logs it. Image failures clear the broken image;
font failures retain fallback text. They do not use the fatal `maui-error` channel
or stop otherwise working controls. Unknown logical names never fetch guessed URLs.

Deploy the managed/Wasm application, runtime glue, backend JS, catalog and resource
files **in lockstep from one publish**. Catalog schema checks do not prove that
every file belongs to that publish; filenames are not content-hashed. Replace a
deployment as a unit and invalidate caches together, including `maui-assets.json`.
Existing sites upgrading the bridge must also publish the catalog, even if empty.

## Browser integration details

### Web Component contract

C# handlers select an already registered tag. A direct mapping such as `fluent-button` reuses Fluent's implementation; a custom element such as `maui-scroll-view` supplies MAUI-specific container behavior where no suitable Fluent component is used. Prefer reuse over recreating an existing control or adding a wrapper solely for naming consistency.

The internal interop distinguishes JavaScript **properties** (`textContent`, `disabled`), HTML **attributes** (`data-automation-id`, removed when the value is null), and CSS **styles**. Property writes target existing writable properties rather than creating arbitrary fields. This is a trusted handler interface, not a sandbox for untrusted markup or scripts. Managed code and JS assets must be rebuilt and deployed together after protocol changes.

In both cases, MAUI owns layout and the backend owns element lifetime. The bridge manages handles and DOM operations, not a second control-kind mapping. Component-specific input or style adaptations belong beside their handlers; see [EntryHandler](src/Browser.FluentUIWebComponents/Handlers/EntryHandler.cs) for the input policy and the reasons for its adapter.

### Browser mount and scrolling

The host page supplies a dedicated, bounded viewport for the MAUI window, with
`position: relative` and a width and height determined by the host layout, not by
the mounted application's content. Percentage heights need a containing block
with a definite height. Keep padding on an outer wrapper: the MAUI root starts at
the mount's padding edge, and a padded mount is not currently supported.

The bridge uses `ResizeObserver.contentRect` for fractional content-box dimensions
in CSS layout pixels, excluding borders and occupied scrollbar space. It waits
for the initial size notification before the first layout, and retains the latest
observed size for font and managed layout invalidations. It does not round through
`clientWidth`/`clientHeight`, scale by devicePixelRatio, or use transformed visual
bounds. Zero-sized mounts defer layout until a positive size is observed.

The sample uses `overflow: hidden` on its mount and MAUI `ScrollView` for application
scrolling, but hiding mount overflow is not required to prevent resize loops.
A bounded, zero-padding mount can use `overflow: auto` or `overflow: scroll`;
the backend tracks the resulting content-box size without rounding the MAUI root
past the viewport and repeatedly toggling host scrollbars. The surrounding HTML
document and inner MAUI ScrollViews retain their own scrolling behavior. This
does not imply support for content-sized mounts or every nested-scroll layout.
Business XAML should not compensate with fixed widths or scrollbar-size offsets.

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

Explicitly merge `FluentThemeResources` into application resources after the MAUI application and browser interop are initialized. The type lives in the backend's root namespace and assembly, `Microsoft.Maui.Platforms.Browser.FluentUIWebComponents`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                    xmlns:fluent="clr-namespace:Microsoft.Maui.Platforms.Browser.FluentUIWebComponents;assembly=Microsoft.Maui.Platforms.Browser.FluentUIWebComponents">
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

Application styles remain app-owned. BrowserSample merges its standard MAUI
`Resources/Styles/AppStyles.xaml` dictionary after `FluentThemeResources` for semantic
text and surface styles; the backend does not bundle universal MAUI Fluent styling.

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

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for source builds, repository configuration and dependency-change checks.