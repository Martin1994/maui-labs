# BrowserSample

An interactive README built as a .NET MAUI XAML application using the [Fluent UI Web Components browser backend](../../README.md). Edit a .NET card, collect snapshots on a board, and opt into a short playback sequence while exploring the implementation. This is an experimental development example, not a production-ready application or a complete MAUI compatibility demonstration.

## What the application demonstrates

- A normal MAUI application using a separate browser backend.
- App-owned MAUI resource dictionaries and styles, a responsive Grid, rounded borders and vertical scrolling.
- Window-scoped Page and ViewModel services, plus an injected singleton preset service.
- MVVM with typed XAML bindings: explicit `TwoWay` title/tagline inputs, `OneWay` previews and commands, and command availability notifications.
- Explicit artwork choices and a default/Open Sans font choice, retained with text in immutable board snapshots.
- A snapshot board projected from an observable collection into real MAUI views, preserving existing views when reordered.
- Standard app-head `MauiImage`, `MauiFont` and `MauiAsset` declarations: unchanged
	.NET bot PNG/SVG artwork, registered Open Sans, and Raw packing without a runtime file API.
- Explicitly opt-in playback: awaited opacity animations, a sample-only children-arrange notifier and a dispatcher timer perform five bounded board transitions, with Stop and lifecycle cleanup.
- System / Light / Dark theme selection, Fluent palette resources and app-owned theme colors.
- Navigation-based startup timing, uncompressed Wasm size and footer links to the page source and backend README.

The page is defined in [MainPage.xaml](MainPage.xaml), with snapshot views in [Views/BoardCard.xaml](Views/BoardCard.xaml) and their collection projection in [MainPage.Board.cs](MainPage.Board.cs). `wwwroot/index.html` supplies only the mount point and runtime bootstrap, except for bootstrap-error reporting. There is no Blazor UI or sample-side DOM layout logic.

## Application structure

- **C# and XAML** define the application and page, with `ViewModels/`, `Models/`, `Services/` and `Views/` separating draft state, snapshots, presets and board presentation. MAUI owns layout; the browser-side code does not implement a second UI renderer. XAML is compiled with MAUI's SourceGen inflator.
- **`web/` is the browser source directory.** Edit the TypeScript here for startup and browser-only diagnostics. Backend asset preparation also places its JS bundle and public `.d.ts` files here so imports resolve locally; those supplied files are gitignored and should not be edited.
- **`wwwroot/` contains handwritten static assets only**, including the HTML entry page. TypeScript does not emit here. This directory alone is not the complete runnable application.
- **`Resources/` contains app-declared resources.** Images/fonts can also be linked
	from tracked repository fixtures in the csproj. These are standard MAUI items,
	not handwritten web assets; `Resources/Raw/` contains license and attribution notices only.
- **`artifacts/maui/` is the .NET publish output.** It contains the MAUI module's Wasm, runtime glue, backend JS and declarations, the mandatory `maui-assets.json` and `maui-assets/` files, plus symbols/metadata when emitted by the toolchain. It is the frontend's only input from .NET, whether the backend is a source or package reference.
- **`artifacts/typescript/` contains compiled application JS** emitted from `web/`. Edit the TS sources, not these outputs.
- **`artifacts/wwwroot/` is the complete website.** npm site assembly combines all files from `artifacts/maui/`, `wwwroot/` and `artifacts/typescript/`. Serve or deploy this directory.

The npm-led flow is **publish MAUI → prepare adjacent browser assets and compile TypeScript → assemble the site**. The sample owns its npm dependencies, TypeScript configuration and site assembly. MSBuild does not call the sample frontend or register its files for native deployment; a source backend still builds its own npm bundle as part of the .NET build.

### DI, bindings and state ownership

[MauiProgram.cs](MauiProgram.cs) registers `ICardPresetService` / `CardPresetService`
as a singleton, and `CardStudioViewModel` and `MainPage` as scoped services.
[App.xaml.cs](App.xaml.cs) initializes application resources first, then resolves
`MainPage` from `activationState.Context.Services` in `CreateWindow`. That is the
window service scope, not the root provider. Constructor-injecting the page into
`App` would construct its resource-dependent XAML too early.

[CardStudioViewModel](ViewModels/CardStudioViewModel.cs) receives the preset service
through its constructor and implements `INotifyPropertyChanged`. `MainPage` sets
its injected ViewModel as `BindingContext`; `x:DataType` declares the binding type.
The title and tagline use explicit `Mode=TwoWay`, so browser edits update the
draft and preset/reset commands update the inputs. Preview values, status and
commands use explicit `OneWay` bindings. `ChangeCanExecute` refreshes command
availability when the title or board count changes. `BoardCard` likewise declares
`x:DataType="models:CardDesign"` for its snapshot bindings.

These are the sample's specific binding paths, not a blanket claim that arbitrary
MAUI bindings, reflection-based paths or ViewModels work under NativeAOT. Keep the
SourceGen configuration and validate the actual published application when
changing binding shapes or toolchain versions.

## Theme and color resources

The System/Light/Dark buttons set `Application.UserAppTheme` to `Unspecified`, `Light` or `Dark`. The status label uses MAUI's effective theme and `RequestedThemeChanged`; there is no sample-side theme interop. System mode follows the browser preference. Explicit Light/Dark overrides it, and returning to System uses the latest preference. The choice is not persisted across reloads.

[App.xaml](App.xaml) merges the backend's `FluentThemeResources`, then the sample's
[AppStyles](Resources/Styles/AppStyles.xaml). This standard MAUI `ResourceDictionary`
owns semantic text and surface styles and app branding; it is not universal MAUI
Fluent styling bundled by the backend. Styles use the backend palette through
`DynamicResource`. Most buttons retain Fluent's default colors; artwork/font
selection uses app-owned `DataTrigger` styling, not native toggle controls.

The hero heading and preview's `.NET + MAUI + Fluent` label select the app-owned
`DotNetBrandColor` and `DotNetBrandDarkColor` from `AppStyles` with `AppThemeBinding`.
The distinction is:

| XAML mechanism | Use in this application |
| --- | --- |
| `StaticResource` | Resolve the application-owned brand colors used as binding inputs |
| `DynamicResource` | Keep a property linked to a backend palette key as its value changes |
| `AppThemeBinding` | Choose the light/dark brand input when the effective application theme changes |

Resource values may be declared in XAML or inserted into a `ResourceDictionary` in C#. These mechanisms are built into MAUI; the sample registers no custom resource provider. The backend dictionary reads Fluent presets and updates its entries on effective-theme changes. See the [backend color-resource contract](../../README.md#fluent-color-resources) for the keys and ownership rules.

## Feature sections

The introduction includes four architecture cards: **XAML + C#**, **MAUI layout**,
**Fluent controls** and **NativeAOT**. The **Try it out** section follows, with a
single **Demo - Make a card** card containing the editor, live preview, saved
cards, reordering, playback and implementation notes. Performance metrics and
footer links follow that section.

| Area | Try it | Integration demonstrated |
| --- | --- | --- |
| Card editor and live preview | Edit title/tagline, choose artwork/font, Try a preset, Reset | Typed bindings in both directions and DI-backed commands; Try a preset also focuses and selects its new title |
| Your cards | Add card (or complete an Entry), Move first, Remove last | Command availability, observable state and MAUI child insertion/reordering/removal |
| Playback | Add at least two cards, then Play cards / Stop | Awaited MAUI opacity animations, children-arrange completion and UI dispatcher scheduling |
| Performance metrics of this page | Read Startup and Wasm Size | Navigation timing and decoded module size, not a benchmark score |

Artwork has two explicit choices: **.NET 10** (raster) and **.NET bot** (vector).
Their `DataTrigger` styling marks the selected button with a solid background and
contrasting text; a separate label reads **Selected: .NET 10 submarine** or
**Selected: .NET bot**. **Open Sans: off/on** is another ordinary stateful Button,
with the same selected styling when on. Off uses the platform default font; on
uses registered Open Sans in both preview text labels. The editor inputs always
keep their platform-default font. Neither control supplies true native toggle
semantics. The XAML retains TODOs to replace
the artwork buttons with a Picker and the font button with a native toggle when
their browser handlers are available.

**Try a preset** updates the draft via its command, then its `Clicked` handler
focuses the title Entry and selects the entire new title. There is no separate
**Select title** button. Only the second preset enables Open Sans; the first and
third use the default font. Artwork/font choices do not reset the draft text.

The board holds at most **six** immutable `CardDesign` snapshots in memory for the
current visit. Adding trims the draft title/tagline and requires a nonblank title;
each snapshot retains its artwork and font choice as well as text. Both board
text labels use the snapshot's font, so later draft edits, font changes or Reset
do not change existing cards. Reset only reloads the first preset, including its
default font. Move first sends the first snapshot to the end; Remove last removes
the final snapshot. There is no persistence or Shell navigation.

`MainPage.Board.cs` projects the ViewModel's `ReadOnlyObservableCollection` into
`BoardItems.Children`. Add and Remove affect one view; Move reuses the existing
`BoardCard` rather than rebuilding the board. This preserves the managed View
instance, not its DOM identity: remove/insert disconnects and reconnects that
card's handlers in the current backend. The page connects the collection
subscription on appearance and disconnects it on disappearance. This is explicit
sample code, not a CollectionView or a general collection-binding implementation.

[MainPage.Playback.cs](MainPage.Playback.cs) starts motion **only after Play** and
requires at least two cards. The first transition starts immediately on the click:
the board fades to opacity **0 over 180 ms**, moves its first card to the end,
waits for the next actual MAUI children-arrange pass, then fades back to **1 over
220 ms**. [ArrangedStackLayout](Views/ArrangedStackLayout.cs) is a sample-only
`VerticalStackLayout` whose layout manager signals after `base.ArrangeChildren`
returns. Playback registers the cancellable waiter before moving the card and
keeps the board transparent until it completes; this is not a fixed-delay guess
or a guarantee of browser paint completion. Continuations run asynchronously to
avoid starting animation reentrantly inside layout.

After each fade-in finishes, a one-shot dispatcher timer pauses **650 ms** before
the next transition, preventing overlapping transitions in slow/background tabs.
After five transitions, playback stops and the resulting board order remains.
Stop, restart, page disappearance or dropping below two cards cancels the old run;
generation checks prevent stale continuations from reordering cards or restarting
timers. Cleanup cancels the arrange waiter, stops/unsubscribes the timer, cancels
animation and restores full opacity. Stopping does not undo moves already made.
Playback uses opacity only: no translation mapping is implemented or demonstrated.
This bounded opt-in motion is not a claim of automatic reduced-motion preference
handling.

These are user-facing examples, not a benchmark or proof of real-IME/cross-browser compatibility.

## Packaged resource fixtures

[BrowserSample.csproj](BrowserSample.csproj) declares `MauiImage`, `MauiFont` and
`MauiAsset`. [MauiProgram.cs](MauiProgram.cs) uses standard
`ConfigureFonts(fonts => fonts.AddFont("OpenSans-Regular.ttf", "OpenSansSample"))`.
The card studio needs no special website copies, asset URLs or `@font-face` CSS.

Use the two artwork buttons to choose an image directly, or **Try a preset** to
cycle through the artwork logical names and font choices below. The live
preview image (`draft-artwork`) requests **144 by 160**; each board image requests
**64 by 72**. Both use `AspectFit` and specify both dimensions. Frames remain
subject to MAUI layout constraints; natural dimensions do not determine their
requested size. This page is not a comparison of all four image Aspects.

| Use | Logical source / registration | Behavior |
| --- | --- | --- |
| First preset: Hello, browser. | `sample_bot.png` | Existing raster .NET bot; default font |
| Second preset: Built with .NET | `sample_bot_vector.svg` | Existing vector .NET bot; Open Sans enabled |
| Third preset: Make it yours. | `sample_bot_vector.png` | Alias for the same unchanged SVG, not a generated PNG; default font |
| Draft title/tagline and both board text labels | `FontFamily`: `OpenSansSample` or `null` | Registered alias when enabled, platform default otherwise; board uses the saved choice |
| Title and tagline inputs | No `FontFamily` override | Always use the platform default, independent of the draft's font choice |
| License and attribution notices | `licenses/*` | Packaged files only; the app does not read them |

Open Sans loads asynchronously, initially displaying fallback text. Inspect the
loaded font rather than assuming that text being visible proves font success;
glyphs not present in this font, including Chinese characters, still use fallback.

### Sources and logical names

Paths in this list are relative to the repository root unless stated otherwise:

- **SVG:** `samples/DevFlow.Sample/Resources/Images/dotnet_bot.svg`, linked as
	`Resources/Images/sample_bot_vector.svg`. Both `sample_bot_vector.svg` and
	`sample_bot_vector.png` resolve to this unchanged vector .NET bot artwork.
- **PNG:** `samples/EssentialsAISample/Resources/Images/dotnet_bot.png`, linked as
	`Resources/Images/sample_bot.png`.
- **Font:** `samples/DevFlow.Sample/Resources/Fonts/OpenSans-Regular.ttf`, linked as
	`Resources/Fonts/OpenSans-Regular.ttf`. This font version's embedded metadata
	specifies Apache 2.0 and Google Corporation's 2010–2011 copyright.
- **Raw:** sample-local `Resources/Raw/licenses/` files become `licenses/*`
	through `LogicalName`; the repository MIT license is explicitly linked as
	`licenses/repository.txt`.

The bot images reuse existing repository artwork with verified upstream sources:

- The SVG matches the [.NET brand illustration](https://github.com/dotnet/brand/blob/29878855347e055ff15675471f7043fda3e92cea/dotnet-bot-illustrations/dotnet-bot/dotnet-bot.svg)
	after CRLF/LF normalization, under that revision's **CC0 1.0** dedication.
- The PNG matches the [.NET 10 MAUI template image](https://github.com/dotnet/maui/blob/8493d298df4cfe82547bb46fb33c6d3d88b15d4e/src/Templates/src/templates/maui-mobile/Resources/Images/dotnet_bot.png)
	byte-for-byte, distributed under that repository's **MIT** license. Its notice
	is included as `licenses/maui-MIT.txt`; it is not claimed to be CC0.

Fixed revisions and Git blob identities are recorded in the asset notices.
Neither license grants trademark rights or implies endorsement; consult the
upstream brand guidelines when redistributing.

Image/font logical names use the filename of `Link` (or the original filename).
Raw preserves paths, with `Link` taking precedence over `LogicalName`. All lookups
are case-sensitive. See the [full naming and metadata contract](../../README.md#logical-names-and-metadata).
The app explicitly declares the linked binaries; no referenced app's resource
graph is imported. Keep [asset notices](Resources/Raw/licenses/asset-notices.txt)
and [Apache 2.0](Resources/Raw/licenses/Apache-2.0.txt) with redistributed assets.

### Resource boundaries and failures

Supported images are static PNG/JPG/JPEG/SVG file sources with explicit width and
height and `AspectFit`, `AspectFill`, `Fill` or `Center`. There is no Uri/Stream/
Font image source, image animation, transforms, intrinsic sizing, single-sided
size inference, Resizetizer, SVG rasterization, resizing, tinting or density
generation. Fonts support TTF/OTF through `AddFont` on Label/Button/Entry, not
automatic assembly-embedded font discovery. Raw files are **packed only**:
`FileSystem.OpenAppPackageFileAsync` and other browser FileSystem APIs are not
implemented. Presets are defined in [CardPresetService.cs](Services/CardPresetService.cs).

Build and publish generate the resource catalog/files automatically through the
product's source-build entry point or NuGet's packaged targets. The frontend
copies the complete publish output without interpreting resource declarations.
The app does not hand-author or edit `maui-assets.json`; this generated catalog
is mandatory even with no assets and loads before managed Main;
missing/invalid catalogs are startup failures. Individual image/font failures
instead emit nonfatal `maui-resource-error` on the mount (`event.detail` is a
diagnostic string). A failed image is cleared; a failed font retains fallback.
Other controls and diagnostics continue running. See the
[testing and validation guidance](../../CONTRIBUTING.md#testing-and-validation)
for Node/managed tests and separate real-browser verification requirements.

## Runtime diagnostics

The **Performance metrics of this page** section presents two responsive cards
with shared app-owned heading and caption styles:

- **Startup:** elapsed browser navigation time at the frame following the first MAUI layout. It includes loading and Wasm initialization, unlike a stopwatch started in C# `Main`. This measures a first-render opportunity, not an exact GPU presentation timestamp. Cache, network and background-tab scheduling affect the value; it is recorded once per startup.
- **Wasm Size:** uncompressed bytes of `dotnet.native.wasm`, not transfer size, total page weight or runtime/linear memory size. The sample first reads Resource Timing's `decodedBodySize`, avoiding a second request. If unavailable, C# counts the decoded HTTP response through a stream after startup timing finishes; that fallback may make another request.

The footer's **Explore source** button opens [MainPage.xaml](https://github.com/Martin1994/maui-labs/blob/fluent-web/platforms/Wasm.FluentUIWebComponents/samples/BrowserSample/MainPage.xaml),
and **README** opens the [backend README](https://github.com/Martin1994/maui-labs/blob/fluent-web/platforms/Wasm.FluentUIWebComponents/README.md),
both in a new tab. The two buttons share one row. The README Button is a fallback:
a XAML TODO calls for an inline
hyperlink once Label supports links. `SampleDiagnostics.cs` owns these URLs and
browser interop; `MainPage.xaml.cs` orchestrates and formats the metrics. The
sample-only JS module supplies browser timing/resource information and opens the
URLs. No file-size constant is generated at build time. If runtime asset URLs
change, update the diagnostics URL accordingly.

## Sample regression coverage

Run `npm test` from this sample directory with Node.js 22+, .NET 10 and the backend's
restored portable `net10.0` references available. Restore the backend project first
(see [contributor setup](../../CONTRIBUTING.md#prerequisites-and-commands)); a native
publish, Emscripten and a browser are not required.

The sample owns `scripts/test/` and its `fixtures/`; `npm test` discovers
`*.behaviour.test.mjs`. The app excludes `scripts/test/**/*.cs` from compilation.
Product `npm run test:behaviour` runs backend suites first, then delegates here.

The [card-studio suite](scripts/test/browser-card-studio.behaviour.test.mjs)
compiles the production model, preset service, ViewModel and `ArrangedStackLayout`
against portable MAUI references. It covers commands, DI scopes, notifications,
snapshot/capacity behavior, artwork/font state and arrange/cancellation behavior.
Separate source assertions check typed XAML, explicit TwoWay inputs and SourceGen opt-in.

These tests do not construct `MainPage`, execute generated bindings or playback,
or establish native/browser behavior. After publishing and assembling the site,
check the actual application:

- Resize narrow/wide and switch System/Light/Dark; verify layout and semantic styles.
- Edit both inputs, try presets and Reset; verify TwoWay updates, title focus/selection and command availability.
- Switch artwork and Open Sans; verify selected styling and preview fonts while inputs retain the platform default.
- Add/reorder/remove snapshots; verify the six-card limit and retained artwork/font choices.
- Play/Stop/restart and leave the page; verify bounded playback and cleanup, then check metrics and footer links.

See [contributor validation guidance](../../CONTRIBUTING.md#testing-and-validation)
for broader runtime and distribution checks.

## External package consumption

Use the backend's [NuGet PackageReference instructions](../../README.md#consume-the-backend-package) for an external application. There are **no released packages currently**; use an experimental/local package version actually available from your configured feed.

**The checked-in C# project is still a source-checkout development project.** It contains a backend `ProjectReference`, with no manual targets import, and inherits shared product build properties, app asset targets and root Central Package Management. It also links image/font/license fixtures outside this directory. Copying this directory alone does not make `BrowserSample.csproj` an independent NuGet consumer. Copy/redeclare those licensed resources in the external app as well as configuring its toolchain.

To adapt it for external consumption, replace the backend `ProjectReference` with a `PackageReference`, and supply your application's own browser toolchain properties, package versions and feeds formerly inherited from the checkout. That includes the target framework/`browser-wasm` RID, JS interop and NativeAOT/trimming settings, and matching LLVM runtime/compiler packages for an available compiler host. Retain SourceGen XAML and the backend `UnmanagedEntryPointsAssembly` entry. NuGet restore automatically imports the packaged bridge `Content` and app resource rules. A source `ProjectReference` outside this product does not automatically import those rules. Remove any manual backend import left from an older setup; do not substitute a NuGet-cache path. See the [toolchain requirements](../../README.md#toolchain-configuration) and [inherited configuration details](../../CONTRIBUTING.md#repository-configuration).

The **frontend pipeline is identical for package and source references**: its own npm project and scripts consume app-local `artifacts/maui/`, `web/` and `wwwroot/`, with no backend source, intermediate-output or NuGet-cache paths. Both reference types publish the same three backend asset filenames. Given a matching complete MAUI publish output, frontend preparation, compilation and site assembly need no C# build. This split does not establish that a copied, standalone C# project or its NativeAOT publish has been verified.

## Build and run

See the [backend toolchain configuration](../../README.md#toolchain-configuration) for SDK, MAUI, NativeAOT LLVM and Node requirements. On a supported compiler host, install .NET 10, Node.js 22+ and npm, and initialize Emscripten 3.1.57 before building. Native publishing requires matching runtime/compiler packages; the backend package does not make arbitrary .NET browser SDK configurations work. The current `runtime.osx-arm64.Microsoft.DotNet.ILCompiler.LLVM` package is unavailable, so macOS ARM64 cannot perform the sample's native publish.

For a clean checkout with those prerequisites ready, run from **this sample directory**, `platforms/Wasm.FluentUIWebComponents/samples/BrowserSample`, not the product root:

```bash
npm ci
npm run build
```

This installs the sample's locked frontend dependencies, publishes the MAUI module, compiles TypeScript and assembles `artifacts/wwwroot`. No separate backend build or product-root `npm ci` is a prerequisite: `dotnet publish` follows the source `ProjectReference`, whose backend build installs its own dependencies when product-root `node_modules` is absent. No extra environment variables are prescribed. The commands below are sample-local; the product does not forward sample build commands, but its behavior-test aggregate includes `npm test`.

| Sample-local command | Operation |
| --- | --- |
| `npm run build` | `npm run build:maui-app && npm run build:ts && npm run build:site` |
| `npm run build:maui-app` | `dotnet publish BrowserSample.csproj` → `artifacts/maui/` |
| `npm run prepare:maui-bridge` | `node scripts/prepare-maui-bridge.mjs`: copy three published backend assets into `web/` and clear `artifacts/typescript/` |
| `npm run build:ts` | `npm run prepare:maui-bridge && tsc --project tsconfig.json` → `artifacts/typescript/` |
| `npm run check` | `tsc --project tsconfig.json --noEmit`; requires prepared declarations |
| `npm test` | Sample behavior/source-contract suites; requires restored backend portable references, not native publish |
| `npm run build:site` | `node scripts/build-site.mjs` → complete `artifacts/wwwroot/` |
| `npm run package:site` | `dotnet msbuild BrowserSample.csproj -target:PackageSite`; archive an existing site |

### Frontend assets and compilation

`prepare:maui-bridge` reads `maui-browser.js`, `maui-browser.d.ts` and `dotnet.d.ts` from `artifacts/maui/` and copies them beside the handwritten TS sources in `web/`. These adjacent declarations serve both the editor and compiler. The script validates all three inputs before copying, then clears `artifacts/typescript/` so removed sources cannot leave stale compiled JS. It does not restore, build or publish .NET.

With the sample's npm dependencies installed and a matching MAUI publish already in `artifacts/maui/`, rebuild only the frontend:

```bash
npm run build:ts
npm run check
npm run build:site
```

For editor declarations alone, run `npm run prepare:maui-bridge`; remember that it also clears compiled TS output, so run `npm run build:ts` before assembling the site again. `npm run check` neither prepares inputs nor emits files.

`tsconfig.json` resolves adjacent declarations without `allowJs`, `checkJs`, `rootDirs` or backend source paths. The sample's `.gitignore` ignores supplied `web/*.js` and `web/*.d.ts`, **not handwritten `web/*.ts`**, plus its own `bin`, `obj`, `node_modules` and `artifacts` directories. `wwwroot/` is reserved for handwritten static files. When migrating an older checkout, remove previously emitted `wwwroot/main.js` and `wwwroot/sample-diagnostics.js`; they would collide with the new TypeScript output.

### Module publishing and site assembly

`dotnet publish` defaults to Release for this .NET 10 project and writes the MAUI module to `artifacts/maui/`. It does **not** build the sample TypeScript or produce a complete website, and never calls the sample's npm scripts. The source backend still intentionally builds its own JS resources through npm. Ordinary `dotnet build` performs managed compilation and backend resource generation, not the native Wasm publish. The project uses invariant globalization and enables trimming/AOT diagnostics through its shared configuration.

`build:site` preserves **all files** from `artifacts/maui/`, handwritten `wwwroot/` and compiled `artifacts/typescript/`, including declarations, symbols, maps and metadata when present. It rejects missing required runtime/application assets and path collisions rather than overwriting files. Successful assembly replaces the final site directory, so files removed from the inputs do not remain in the site.

Default paths are **sample-local**, not product-root artifacts:

- MAUI module (`PublishDir`): `artifacts/maui/`.
- Assembled website: `artifacts/wwwroot/`.
- Optional ZIP (`SiteArchive`): `artifacts/BrowserSample-site.zip`.

The npm scripts use these fixed app-local input/output directories. Overriding .NET's `PublishDir` does not redirect frontend preparation or site assembly. `SiteDirectory` and `SiteArchive` only configure the optional ZIP utility and are independent of `PublishDir`.

Host the assembled `artifacts/wwwroot` directory with any static server that serves Wasm correctly; no repository hosting script or .NET server is required. The revised pipeline is not a claim of a newly validated native publish or standalone C# consumer. See the deployment requirements below.

### Contributor-only checkout details

Backend source builds, inherited build/CPM settings and compiler-host lockfiles are covered in [CONTRIBUTING.md](../../CONTRIBUTING.md). They describe this checkout, not the recommended reference mechanism for external applications.

## Static deployment and optional ZIP

The **assembled site directory** can be served or uploaded as-is. PDBs, declarations and runtime metadata can remain. Do not deploy the entire `artifacts` directory, which can contain logs and unrelated build outputs.

From this **sample directory**, this optional command archives the complete sample-local `artifacts/wwwroot` without rebuilding, stripping or filtering files:

```bash
npm run package:site
```

It invokes this project's `PackageSite` target using MSBuild's built-in `ZipDirectory` task and writes `artifacts/BrowserSample-site.zip`. Packaging is not automatic and does not restore, rebuild, publish or assemble the site. For another site or archive location:

```bash
npm run package:site -- -p:SiteDirectory=/path/to/published-site -p:SiteArchive=/path/to/site.zip
```

`SiteDirectory` selects only the ZIP source (default `artifacts/wwwroot`); `SiteArchive` selects only the ZIP destination. Neither changes `PublishDir` or the npm scripts' directories, and a `PublishDir` override does not change the ZIP source. Supply custom paths on each packaging invocation; command-line overrides are not persisted.

If packaging an already published site collected from another machine, pass that static folder as `SiteDirectory`. Packaging uses the supplied/default directory, not a repository collection marker, and needs no backend source-path override.

`index.html` is at the ZIP root. Keep the archive destination outside the source site. You can instead copy or ZIP the published directory using your usual deployment tools.

Extract/upload the contents without changing relative paths. Serve `.js` as JavaScript and `.wasm` as `application/wasm`, and serve the JSON catalog, SVG/PNG/JPEG and TTF/OTF resources with their corresponding content types, not SPA HTML fallbacks. No .NET server is required. Catalog and resource URLs resolve relative to `maui-browser.js`, supporting root or subdirectory hosting independently of the current page route. HTTP gzip/Brotli compression is independent of ZIP compression.

Deploy the app/Wasm, runtime glue, backend JS, `maui-assets.json` and `maui-assets/`
as **one matching publish**, preserving the packaged notices. Replace and invalidate
caches together: resource filenames are not content-hashed, and the catalog's
schema/application checks do not detect every mixture of releases. Never mix
cached assets from different publishes or omit the catalog for an empty app.

## Limitations

The application inherits the [backend's limitations](../../README.md#boundaries-and-known-limitations). It is not a production accessibility, high-contrast, cross-browser, performance or long-running reliability certification. Publishing and browser regression checks must be rerun when the interop protocol or runtime assets change.