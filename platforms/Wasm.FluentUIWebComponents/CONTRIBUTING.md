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

From the product directory:

```bash
npm ci --ignore-scripts
MSBuildEnableWorkloadResolver=false dotnet build src/Wasm.FluentUIWebComponents/Wasm.FluentUIWebComponents.csproj -c Release
```

This restores/builds the managed backend and its browser bundle/declarations, not a runnable website. The environment-variable syntax above is for POSIX shells; on other shells, set `MSBuildEnableWorkloadResolver=false` using the shell's environment syntax before running `dotnet build`.

Install the sample's separate npm dependencies from `samples/BrowserSample` with `npm ci --ignore-scripts`. To build the full solution on a compatible compiler host, run from the product directory:

```bash
MSBuildEnableWorkloadResolver=false dotnet build Wasm.FluentUIWebComponents.slnx -c Release
```

Publishing and frontend commands are documented in the [sample README](samples/BrowserSample/README.md#build-and-run) and run from the **sample directory**.

## Checked-in sample reference and host setup

The checked-in `BrowserSample.csproj` intentionally uses a source `ProjectReference` plus an explicit import of the backend's `buildTransitive/Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents.targets`. Source project references do not get NuGet's automatic targets import. The sample also inherits the product build properties and repository package versions. Copying its directory alone therefore does **not** produce an independent NuGet-consuming C# project.

For source-only asset preparation, restore/build the backend as above first. The sample's `prepare:browser` and `build:browser` targets can then prepare assets without restoring the sample's native compiler host, but still require the backend's source-build prerequisites. A package consumer instead restores its application package references and uses the automatically imported targets; remove both the backend `ProjectReference` and explicit import when converting the sample.

The sample selects `runtime.$(NETCoreSdkPortableRuntimeIdentifier).Microsoft.DotNet.ILCompiler.LLVM` using the **SDK host RID**, not the application's `browser-wasm` RID. Its compiler package versions are aligned through repository Central Package Management. A selected package must actually exist for that host; automatic selection does not add support for an unavailable host such as the current macOS ARM64 configuration. Initialize compatible Emscripten mentioned in the toolchain configuration. Bash is not required for the sample's npm/MSBuild commands.

Host-specific `packages.<SDK-host-RID>.lock.json` files are included for `linux-arm64`, `linux-x64` and `win-x64`. Restore support does not imply native publishing has been tested on every host. Adding another supported host requires generating and reviewing its lockfile. From the sample directory, verify locked restore with:

```bash
dotnet restore BrowserSample.csproj -p:RestoreLockedMode=true
```

The frontend directory/script split is not verification of an independent, copied C# application or a new standalone NativeAOT publish.

## Dependency changes

Follow the repository's dependency policies. From the product directory, run `node ../../eng/scripts/audit-npm.mjs` after npm dependency or lockfile changes before opening a PR. Keep both npm projects registered in `.github/dependabot.yml`; do not treat an unavailable advisory database as a clean audit.