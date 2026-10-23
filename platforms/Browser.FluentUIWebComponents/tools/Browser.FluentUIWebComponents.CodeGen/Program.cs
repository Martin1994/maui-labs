using System.CommandLine;
using Microsoft.Build.Locator;
using Microsoft.Maui.Platforms.Browser.FluentUIWebComponents.CodeGen;

var projectArgument = new Argument<FileInfo>("project")
{
    Description = "C# project containing the browser interop declarations.",
};
projectArgument.AcceptExistingOnly();
var outputArgument = new Argument<FileInfo>("output")
{
    Description = "Generated TypeScript contract file; its directory is created if needed.",
};
var configurationOption = new Option<string>("--configuration", "-c")
{
    Description = "MSBuild configuration used to load the project.",
    DefaultValueFactory = _ =>
    {
        return "Release";
    },
};
var command = new RootCommand("Generate the Fluent browser backend's TypeScript interop contract.")
{
    projectArgument,
    outputArgument,
    configurationOption,
};
command.SetAction(async (parseResult, cancellationToken) =>
{
    return await GenerateAsync(parseResult.GetValue(projectArgument)!, parseResult.GetValue(outputArgument)!,
        parseResult.GetValue(configurationOption)!, cancellationToken);
});
return await command.Parse(args).InvokeAsync();

static async Task<int> GenerateAsync(FileInfo project, FileInfo destination, string configuration,
    CancellationToken cancellationToken)
{
    try
    {
        var projectPath = project.FullName;
        var path = destination.FullName;
        // Discover the SDK relative to the input project's global.json, before loading MSBuild.
        Directory.SetCurrentDirectory(Path.GetDirectoryName(projectPath)!);
        MSBuildLocator.RegisterDefaults();
        var compilation = await ProjectLoader.LoadAsync(projectPath, configuration, cancellationToken);
        var output = ContractGenerator.Generate(compilation);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path) || File.ReadAllText(path) != output)
        {
            // Different configurations share this source contract. An atomic replacement
            // prevents readers from observing a partially written file during parallel builds.
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, output);
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                File.Delete(temporary);
            }
        }
        return 0;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        Console.Error.WriteLine("Browser.FluentUIWebComponents.CodeGen: generation cancelled.");
        return 130;
    }
    catch (Exception error)
    {
        Console.Error.WriteLine($"Browser.FluentUIWebComponents.CodeGen: error WFCG001: {error.Message}");
        return 1;
    }
}