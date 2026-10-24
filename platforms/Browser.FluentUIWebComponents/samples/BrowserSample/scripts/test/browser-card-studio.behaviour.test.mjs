/**
 * @file BrowserSample ViewModel regressions against real MAUI Command and DI.
 * Compiles the production model, preset service/interface, ViewModel and arrange notifier;
 * no page, backend build, browser, native publish or substitute Command is used.
 * Like assets-managed, requires .NET 10 and the backend's restored portable DLLs.
 * The temporary project is isolated from product MSBuild imports and removed.
 *
 * The separate source-contract test checks XAML/SourceGen inputs only. It does
 * not execute the XAML generator or prove generated bindings/browser behavior.
 */
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { basename, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

const sample = fileURLToPath(new URL('../../', import.meta.url));
const root = fileURLToPath(new URL('../../../../', import.meta.url));
const escapeXml = value => value.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;');

test('CardStudio managed behavior: production sources, real MAUI Command and DI', async t => {
    const assetsPath = join(root, 'src/Browser.FluentUIWebComponents/obj/project.assets.json');
    assert.ok(existsSync(assetsPath), 'Restore the backend before running CardStudio managed tests.');
    const assets = JSON.parse(readFileSync(assetsPath, 'utf8'));
    assert.ok(assets.targets['net10.0'], 'The restored backend must expose portable net10.0 references.');
    const references = [];
    for (const [library, target] of Object.entries(assets.targets['net10.0'])) {
        for (const file of Object.keys(target.compile ?? {}).filter(file => file.endsWith('.dll'))) {
            const candidate = Object.keys(assets.packageFolders)
                .map(folder => join(folder, assets.libraries[library].path, file)).find(existsSync);
            assert.ok(candidate, `Missing restored reference ${library}/${file}`);
            references.push(`<Reference Include="${escapeXml(basename(file, '.dll'))}" HintPath="${escapeXml(candidate)}" />`);
        }
    }

    // Execute the actual two service registrations, not a parallel handwritten DI
    // setup. Deliberately exclude MainPage and all native/backend hosting setup.
    const program = readFileSync(join(sample, 'MauiProgram.cs'), 'utf8');
    const registrations = [...program.matchAll(/^\s*builder\.Services\.Add\w+\s*<\s*(?:ICardPresetService|CardStudioViewModel)\b[^;]*;/gm)]
        .map(([statement]) => statement.trim().replace('builder.Services.', 'services.'));
    assert.equal(registrations.length, 2, 'Expected the preset service and ViewModel registrations in MauiProgram.');

    const artifacts = join(sample, 'artifacts');
    mkdirSync(artifacts, { recursive: true });
    const directory = mkdtempSync(join(artifacts, 'card-studio-managed-'));
    t.after(() => rmSync(directory, { recursive: true, force: true }));
    writeFileSync(join(directory, 'Directory.Build.props'), '<Project/>');
    writeFileSync(join(directory, 'Directory.Build.targets'), '<Project/>');
    const sources = [
        'Models/CardDesign.cs', 'Services/ICardPresetService.cs',
        'Services/CardPresetService.cs', 'ViewModels/CardStudioViewModel.cs',
        'ViewModels/ThemeViewModel.cs',
        'Views/ArrangedStackLayout.cs',
    ];
    writeFileSync(join(directory, 'Fixture.csproj'), `<Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType>
        <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
        <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
        <MSBuildEnableWorkloadResolver>false</MSBuildEnableWorkloadResolver>
        <EnableNETAnalyzers>false</EnableNETAnalyzers>
      </PropertyGroup>
      <ItemGroup>
        ${references.join('\n')}
        ${sources.map(file => `<Compile Include="${escapeXml(join(sample, file))}" />`).join('\n')}
        <Compile Include="SampleRegistrations.cs" />
        <Compile Include="${escapeXml(join(sample, 'scripts/test/fixtures/card-studio-managed.cs'))}" />
        <Compile Include="${escapeXml(join(sample, 'scripts/test/fixtures/theme-managed.cs'))}" />
      </ItemGroup>
    </Project>`);
    writeFileSync(join(directory, 'SampleRegistrations.cs'), `
using FluentBrowserSample.Services;
using FluentBrowserSample.ViewModels;
using Microsoft.Extensions.DependencyInjection;

internal static class SampleRegistrations
{
    internal static void Add(IServiceCollection services)
    {
        ${registrations.join('\n        ')}
    }
}
`);
    const build = spawnSync('dotnet', ['build', join(directory, 'Fixture.csproj'), '-m:1', '-nodeReuse:false'], {
        cwd: root, encoding: 'utf8', timeout: 120_000,
    });
    assert.ifError(build.error);
    assert.equal(build.status, 0, build.stdout + build.stderr);
    for (const scenario of ['initial', 'injected-di', 'invalid-service', 'notifications', 'blank-title',
        'snapshots', 'capacity', 'move', 'guards', 'presets', 'artwork', 'fonts', 'arrange', 'themes']) {
        await t.test(scenario, () => {
            const run = spawnSync('dotnet', [join(directory, 'bin/Debug/net10.0/Fixture.dll'), scenario], {
                cwd: root, encoding: 'utf8', timeout: 30_000,
            });
            assert.ifError(run.error);
            assert.equal(run.status, 0, run.stdout + run.stderr);
            assert.match(run.stdout, /CardStudio assertions passed/);
        });
    }
});

test('CardStudio source contract only: typed XAML, explicit TwoWay inputs and SourceGen opt-in', () => {
    const page = readFileSync(join(sample, 'MainPage.xaml'), 'utf8');
    const card = readFileSync(join(sample, 'Views/BoardCard.xaml'), 'utf8');
    const project = readFileSync(join(sample, 'BrowserSample.csproj'), 'utf8');
    const pageRoot = page.match(/<ContentPage\b[^>]*>/)?.[0] ?? '';
    assert.match(pageRoot, /xmlns:vm="clr-namespace:FluentBrowserSample\.ViewModels"/);
    assert.match(pageRoot, /x:DataType="vm:CardStudioViewModel"/);
    const cardRoot = card.match(/<Grid\b[^>]*>/)?.[0] ?? '';
    assert.match(cardRoot, /xmlns:models="clr-namespace:FluentBrowserSample\.Models"/);
    assert.match(cardRoot, /x:DataType="models:CardDesign"/);
    const entries = [...page.matchAll(/<Entry\b[^>]*>/g)].map(([tag]) => tag);
    for (const [id, property] of [['card-title-input', 'Title'], ['card-tagline-input', 'Tagline']]) {
        const matches = entries.filter(tag => tag.includes(`AutomationId="${id}"`));
        assert.equal(matches.length, 1, `Expected one editor Entry for ${id}.`);
        assert.match(matches[0], new RegExp(`Text="\\{Binding\\s+(?:Path=)?${property}\\s*,\\s*Mode=TwoWay\\s*\\}"`));
        assert.doesNotMatch(matches[0], /\bx:DataType=/, 'Editor entries must inherit the ViewModel data type.');
        assert.doesNotMatch(matches[0], /\bFontFamily=/, 'Draft font choices must not change the editor font.');
    }
    const xamlItems = [...project.matchAll(/<MauiXaml\b[^>]*>/g)].map(([tag]) => tag);
    const pickers = [...page.matchAll(/<Picker\b[^>]*>/g)].map(([tag]) => tag);
    const picker = pickers.find(tag => tag.includes('AutomationId="artwork-picker"')) ?? '';
    assert.match(picker, /SelectedIndex="\{Binding SelectedArtworkIndex, Mode=TwoWay\}"/);
    assert.match(picker, /ItemsSource="\{Binding ArtworkChoices, Mode=OneWay\}"/);
    assert.match(picker, /Grid.Row="1"/);
    const themePicker = pickers.find(tag => tag.includes('AutomationId="theme-picker"')) ?? '';
    assert.match(themePicker, /ItemsSource="\{Binding Theme\.Choices, Mode=OneWay\}"/);
    assert.match(themePicker, /SelectedIndex="\{Binding Theme\.SelectedIndex, Mode=TwoWay\}"/);
    assert.doesNotMatch(themePicker, /SelectedIndexChanged=|x:Name=/);
    const pageCode = readFileSync(join(sample, 'MainPage.xaml.cs'), 'utf8');
    const themeCode = readFileSync(join(sample, 'ViewModels/ThemeViewModel.cs'), 'utf8');
    assert.doesNotMatch(pageCode, /ThemePicker|OnThemeSelected|UserAppTheme|RequestedThemeChanged/,
        'Code-behind must only connect/disconnect theme lifecycle, not synchronize the Picker.');
    assert.doesNotMatch(themeCode, /BrowserApplication|SystemThemeChanged|PlatformAppTheme/,
        'Sample theme UI must use standard effective-theme APIs, not a backend-specific event.');
    assert.match(themeCode, /app\.UserAppTheme == AppTheme\.Unspecified\s*\? \$"System \(\{app\.RequestedTheme\}\)"\s*:\s*"System"/);
    assert.doesNotMatch(page, /ThemeButtons|ThemeStatusLabel|On(?:System|Light|Dark)ThemeClicked/);
    assert.match(page, /<Grid ColumnDefinitions="\*,\*"[^>]*AutomationId="artwork-font-options"/);
    assert.match(page, /<HorizontalStackLayout Grid.Row="1" Grid.Column="1"[^>]*>[\s\S]*?<Switch/);
    const control = page.match(/<Switch\b[^>]*>/)?.[0] ?? '';
    assert.match(control, /IsToggled="\{Binding UseCustomFont, Mode=TwoWay\}"/);
    assert.match(control, /AutomationId="font-switch"/);
    assert.doesNotMatch(page, /ToggleButton|xmlns:fluent=/, 'The sample uses only standard MAUI controls for selection.');
    assert.ok(xamlItems.some(tag => /Include="\*\*\/\*\.xaml"/.test(tag) && /Inflator="SourceGen"/.test(tag)),
        'The sample must explicitly opt its XAML files into SourceGen.');
    assert.match(project, /<Compile\b[^>]*\bRemove="scripts\/test\/\*\*\/\*\.cs"/,
        'The sample must exclude managed test fixtures from app compilation.');
});