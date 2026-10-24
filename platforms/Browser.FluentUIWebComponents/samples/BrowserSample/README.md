# BrowserSample

A source-checkout example for developing against the experimental
[Fluent UI Web Components browser backend](../../README.md). Its purpose is to
exercise ordinary MAUI application patterns through a browser implementation,
not to define a separate web UI framework or certify full MAUI compatibility.

Run the sample to explore the UI. This document explains the design decisions,
build boundaries and constraints that are less apparent from running it.

## Build and run

Use .NET 10, Node.js 22+ and the matching NativeAOT LLVM/Emscripten toolchain.
Follow the [backend toolchain requirements](../../README.md#toolchain-configuration)
before building; installing an arbitrary .NET browser workload is not equivalent.
Repository-specific SDK selection and compiler-host lockfiles are described in
[CONTRIBUTING.md](../../CONTRIBUTING.md).

From **this sample directory**, with the native toolchain initialized:

```bash
npm ci
npm run build
```

Serve **`artifacts/wwwroot/`** with a static server; it is the complete website.
Neither `wwwroot/` nor the .NET publish directory is sufficient on its own.
The source `ProjectReference` builds the backend and installs its npm dependencies
if absent, so a separate backend build is not a prerequisite.

### Why publishing and site assembly are separate

.NET owns the compiled application and its declared resources; the frontend owns
TypeScript compilation and website assembly. The frontend consumes the app's
publish output, not backend source paths, intermediate directories or the NuGet
cache. This keeps the same frontend pipeline usable with source and package references.

| Directory | Ownership and purpose |
| --- | --- |
| `web/` | Handwritten TypeScript plus generated adjacent backend JS/declarations; edit only the handwritten sources |
| `wwwroot/` | Handwritten static files, including the HTML mount/bootstrap page |
| `artifacts/maui/` | .NET publish output: Wasm, runtime glue, backend assets and packaged app resources |
| `artifacts/typescript/` | Compiled application scripts |
| `artifacts/wwwroot/` | Assembled deployment output |

`npm run build` performs **.NET publish → TypeScript preparation/compilation → site
assembly**. In contrast, `dotnet build` does not produce native Wasm, and
`dotnet publish` does not compile the sample TypeScript or assemble the website.
Building the source-referenced backend still builds its own JavaScript bundle.

For focused changes:

| Command | Use and caveat |
| --- | --- |
| `npm run build:maui-app` | Republish C#/XAML/resources to `artifacts/maui/` |
| `npm run build:ts` | Prepare the published backend assets beside the TS sources, then compile; requires an existing matching publish |
| `npm run check` | Type-check only; neither prepares declarations nor emits scripts |
| `npm run build:site` | Assemble the current inputs without rebuilding them |

Preparation clears compiled TS output to prevent deleted sources from surviving.
Site assembly rejects path collisions rather than overwriting runtime files and
replaces the previous site after successful assembly. Do not put generated JS in
`wwwroot/`: it will collide with the compiled scripts.

These npm paths are sample-local and fixed. Overriding .NET's `PublishDir` does not
redirect the frontend scripts. After C#, XAML or resource changes, prefer the full
`npm run build` rather than accidentally assembling an old module with new scripts.

## Design decisions

### Keep MAUI responsible for the application UI

[MainPage.xaml](MainPage.xaml) uses standard MAUI controls and layout containers.
MAUI computes frames; the backend renders real Fluent components. Responsive
layout therefore belongs in MAUI, not in a parallel sample-side CSS layout engine.
The host HTML is limited to mounting, bootstrapping and startup-error reporting;
there is no Blazor renderer.

The sample should adapt to the supported MAUI surface, not require backend APIs
invented for its presentation. For control coverage and implementation limits,
use the [backend support matrix](../../README.md#control-handlers) rather than
treating this page as an exhaustive compatibility test.

### Initialize resources before resolving window-scoped views

[App.xaml.cs](App.xaml.cs) initializes resources before resolving the page from
`activationState.Context.Services` in `CreateWindow`. Constructor-injecting the
page into `App` would instantiate its resource-dependent XAML too early.

[MauiProgram.cs](MauiProgram.cs) gives the page and ViewModel window-scoped
lifetimes, while the immutable preset service is shared. Resolve scoped services
from the window scope, not the root provider.

### Make state synchronization explicit and AOT-compatible

XAML uses MAUI's **SourceGen inflator**, `x:DataType` and explicit binding modes.
This avoids relying on runtime XAML loading and makes the binding paths visible
to the compiler. Successful managed compilation alone is not proof that a new
binding shape works after native compilation; verify the published application.

[CardStudioViewModel](ViewModels/CardStudioViewModel.cs) owns mutable draft state;
[CardDesign](Models/CardDesign.cs) captures immutable snapshots so subsequent edits
cannot change saved values through shared mutable references. Command availability
is notified explicitly, and command execution also checks its preconditions.

[ThemeViewModel](ViewModels/ThemeViewModel.cs) uses only standard MAUI theme APIs.
`RequestedThemeChanged` reports **effective-theme changes**, not every system
preference change. Consequently, it displays a theme suffix only in System mode
and explicitly refreshes after changing `UserAppTheme`: switching preferences can
leave the effective theme unchanged and raise no event. Refresh also guards against
transient selection writes caused by replacing a bound choice label. The page only
connects/disconnects the ViewModel's subscription with its own lifecycle.

### Distinguish view identity, layout completion and browser paint

[MainPage.Board.cs](MainPage.Board.cs) projects an observable collection into MAUI
children because this sample does not use a CollectionView implementation. Moving
an item preserves its managed view, **not necessarily its DOM node**: the current
backend disconnects/reconnects handlers during removal and insertion. Do not infer
general virtualization or collection-binding support from this projection.

[MainPage.Playback.cs](MainPage.Playback.cs) waits for a real children-arrange pass
after reordering instead of guessing a delay. The sample-only
[ArrangedStackLayout](Views/ArrangedStackLayout.cs) registers the waiter before the
mutation and completes it after layout, with asynchronous continuations to avoid
reentering layout. This is an arrange barrier, not a browser paint guarantee.

Timers are one-shot so transitions cannot overlap when the browser is slow or
backgrounded. Cancellation and run-identity checks prevent an old continuation
from restarting a stopped sequence. Page disappearance releases subscriptions,
timers and animation work. Explicitly starting motion is not a substitute for
automatic reduced-motion support, which is not implemented here.

### Separate application styling from backend theme resources

[AppStyles.xaml](Resources/Styles/AppStyles.xaml) owns presentation and branding;
the backend's `FluentThemeResources` supplies palette values, not a universal app
stylesheet. Use `DynamicResource` for palette values that change and
`AppThemeBinding` for app-owned light/dark choices. Leave Fluent defaults intact
unless the application deliberately overrides them. See the
[color-resource contract](../../README.md#fluent-color-resources) for the distinction.

## Packaged resource fixtures

Resources are declared as `MauiImage`, `MauiFont` and `MauiAsset` in
[BrowserSample.csproj](BrowserSample.csproj), not copied by a second website-specific
resource pipeline. Some binaries are linked from other repository samples without
referencing those applications; copying this sample directory alone will omit them.

Important constraints when changing fixtures:

- Image/font logical names are case-sensitive. `sample_bot_vector.png` deliberately
  aliases the unchanged SVG; it does **not** prove PNG generation or Resizetizer support.
- Images currently require explicit width and height. Do not rely on intrinsic
  sizing, URI/stream sources or automatic image processing; consult the
  [resource contract](../../README.md#packaged-application-resources).
- Fonts load asynchronously. Visible text may still be fallback text, and missing
  glyphs may continue to use fallback after loading. Check the actual loaded font
  when validating font behavior, not just whether text appeared.
- Raw files are packed, but the browser FileSystem API for reading them is not
  implemented. Packing notices is not evidence of a working runtime Raw-file API.
- `maui-assets.json` is generated and mandatory, even for apps without resources.
  A missing/invalid catalog is a startup failure; individual image/font failures
  instead produce nonfatal `maui-resource-error` diagnostics on the mount.

Keep the [asset notices](Resources/Raw/licenses/asset-notices.txt) and associated
license files with redistributed resources. They record the exact upstream sources:
the SVG is CC0, the PNG is MIT, and this bundled Open Sans version is Apache 2.0.
These licenses do not grant trademark rights or imply endorsement.

## Runtime diagnostics

[SampleDiagnostics.cs](SampleDiagnostics.cs) and its
[browser module](web/sample-diagnostics.ts) exist because C# startup alone cannot
measure the full browser navigation or HTTP resource loading.

- **Startup time** includes download and runtime initialization up to a frame
  opportunity following the first MAUI layout. It is not an exact GPU presentation
  timestamp. Compare only under equivalent network, cache and tab-visibility conditions.
- **Wasm size** is decoded module bytes, not transferred bytes, total page weight
  or runtime memory. If Resource Timing cannot supply it, the fallback streams a
  second decoded response after startup timing finishes.

Treat these values as diagnostics, not benchmark results. HTTP compression and
cache policy can change observed startup substantially without any code change.

## Sample regression coverage

Run `npm test` from this directory after restoring the backend's portable
`net10.0` references. The tests need .NET 10 and the sample's npm dependencies,
but not Emscripten or a running browser. Run them separately from the backend tests.

The [suite](scripts/test/browser-card-studio.behaviour.test.mjs) exercises production
ViewModels, real MAUI commands/bindings and DI, with platform boundaries supplied
by test fixtures. XAML assertions check source contracts only; the tests do not
construct the generated page or establish NativeAOT rendering correctness.

After changing bindings, resources or lifecycle code, publish the full site and
check the relevant failure modes in a real browser: updates in both binding
directions, same-effective-theme preference changes, keyboard/focus behavior,
constrained layouts, asynchronous font completion and cancellation during layout
or animation. A passing functional check is not a cross-browser, accessibility,
performance or long-running retention certification. Broader backend validation
belongs in the [contributor guide](../../CONTRIBUTING.md#testing-and-validation).

## External package consumption

This project intentionally inherits repository build properties, central package
versions, feeds and linked resources. It is **not a standalone project template**.
Replacing its `ProjectReference` alone does not reproduce that environment.

For an independent application, follow the
[package setup instructions](../../README.md#consume-the-backend-package), provide
your own toolchain configuration and resource declarations, and retain SourceGen
XAML and the backend `UnmanagedEntryPointsAssembly` entry. There are no released
backend packages currently; use an available experimental/local package. NuGet's
packaged targets supply the resource rules that an external source reference does
not automatically import. Do not hardcode NuGet-cache paths.

The frontend still consumes only app-local publish output. Keeping that boundary
does not eliminate the need to verify the independent application's native publish.

## Static deployment and optional ZIP

Deploy the contents of `artifacts/wwwroot/`, not the entire `artifacts/` directory.
No .NET server is required. Preserve relative paths and serve `.wasm` as
`application/wasm`, JavaScript as JavaScript, and catalog/resources with their correct
content types rather than SPA HTML fallbacks. Resource URLs resolve relative to the
backend module, allowing root or subdirectory hosting.

Deploy Wasm, runtime glue, backend JS, catalog and resources as **one matching
publish** and invalidate caches together. Filenames are not content-hashed, and
catalog validation cannot detect every mixture of releases. Preserve packaged
notices; symbols, declarations and metadata may remain in the deployed output.

`npm run package:site` optionally archives an existing site as
`artifacts/BrowserSample-site.zip`, with `index.html` at the ZIP root. It does not
build or assemble anything. `SiteDirectory` and `SiteArchive` can override the ZIP
input/output through MSBuild properties; neither redirects .NET publishing or npm
assembly. Keep the archive outside its source directory. ZIP compression is
independent of the server's HTTP gzip/Brotli configuration.