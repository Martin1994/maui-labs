# Contributing to the Fluent UI Web Components backend

These instructions are for **source-checkout development**, not external application setup. External applications should follow the [NuGet PackageReference guidance](README.md#consume-the-backend-package). There are no released packages for this backend currently. Package consumption is experimental/local only.

## Repository configuration

Run backend commands from `platforms/Wasm.FluentUIWebComponents` unless stated otherwise.

- Product [global.json](global.json) selects stable .NET 10.0 with `latestFeature` roll-forward. It is not locked to any patch version, but .NET 11+ and preview SDKs are not tested yet thus not specified in the constraint. This does not change the pinned LLVM packages.
- Product [Directory.Build.props](Directory.Build.props) supplies shared browser build properties, including `net10.0`, `browser-wasm`, unsafe code, JS interop, trimming/AOT analyzers and MAUI feature switches. These settings are not automatically supplied to an external app by referencing the backend package.
- NuGet dependencies use repository [Central Package Management](../../Directory.Packages.props). [NuGet.config](NuGet.config) configures feeds, including the experimental compiler feed. External applications need their own feeds, package versions and browser toolchain configuration rather than relying on these ancestor files.
- The backend and sample packages each have their own `package.json`, `package-lock.json` and npm installation. They shouldn't be shared because a sample package should be considered as an external consumer which does not have access to the source code of the backend.

See the [toolchain requirements](README.md#toolchain-configuration). Native publishing needs a compatible compiler host and initialized Emscripten environment, not just a compatible .NET SDK.

## Build from source

With .NET 10, Node.js 22+ and npm installed, run from the product directory:

```bash
dotnet build src/Wasm.FluentUIWebComponents/Wasm.FluentUIWebComponents.csproj -c Release
```

This restores/builds the managed backend and its browser bundle/declarations. No Wasm output will be generated in this backend package. Native Wasm and a runnable website will be generated in the consumer packages.

Build instruction:

```
dotnet build [-c Debug|Release]
```

`dotnet build` runs `RestoreBrowserDependencies` (`npm ci`) -> `GenerateBrowserInterop` -> `BuildBrowserAssets` (`npm run build`) in addition to the regular C# build. Dependency restoration runs `npm ci` automatically at the product root when `node_modules` is absent. These targets skip design-time builds, and clean operations retain the shared npm dependencies. After dependency or lockfile changes, run `npm ci` explicitly to refresh an existing installation.

A direct backend-root `npm run build` is a lower-level backend bundle command: it requires a finished `dotnet build`, specifically `GenerateBrowserInterop` target, and it generates the JavaScript bundle required by this backend that contains the bridge to operate DOM nodes and provide browser platform features.

To compile the full solution (including .NET part of the sample projects) on a compatible compiler host (see toolchain requirements), run from the product directory:

```bash
dotnet build Wasm.FluentUIWebComponents.slnx -c Release
```

Please be aware that this does not compile the sample website. For a complete sample build on a supported host with initialized Emscripten, run `npm ci` followed by `npm run build` in the **sample directory**.  See the [sample README](samples/BrowserSample/README.md#build-and-run).

## Sample reference and host setup

The provided `BrowserSample` can be used as a reference of consuming this backend, except two differences: It uses a source `ProjectReference` instead of `PackageReference` from a NuGet feed. It also inherits the product build properties and repository package versions. Therefire, copying its directory alone therefore does **not** produce an independent NuGet-consuming C# project without any modification, but other parts can be referenced as is.


Host-specific `packages.<SDK-host-RID>.lock.json` files are included for `linux-arm64`, `linux-x64` and `win-x64` (macOS is not yet supported due to lack of NuGet package). Restore support does not imply native publishing has been tested on every host. Adding another supported host requires generating and reviewing its lockfile. From the sample directory, verify locked restore with:

```bash
dotnet restore BrowserSample.csproj -p:RestoreLockedMode=true
```

The frontend directory/script split is not verification of an independent, copied C# application or a new standalone NativeAOT publish.

## Dependency changes

Follow the repository's dependency policies. From the product directory, run `node ../../eng/scripts/audit-npm.mjs` after npm dependency or lockfile changes before opening a PR. Keep both npm projects registered in `.github/dependabot.yml`; do not treat an unavailable advisory database as a clean audit.