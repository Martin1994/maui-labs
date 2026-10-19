using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;

namespace Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents.CodeGen;

internal static class ProjectLoader
{
    internal static async Task<CSharpCompilation> LoadAsync(string projectPath, string configuration,
        CancellationToken cancellationToken)
    {
        using var workspace = MSBuildWorkspace.Create(new Dictionary<string, string>
        {
            ["Configuration"] = configuration,
            ["DesignTimeBuild"] = "true",
            ["BuildProjectReferences"] = "false",
        });
        var failures = new List<string>();
        workspace.WorkspaceFailed += (_, args) =>
        {
            if (args.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
            {
                failures.Add(args.Diagnostic.Message);
            }
        };
        var project = await workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken);
        if (failures.Count != 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
        }
        // The handwritten declarations define this contract. Do not run the LLVM
        // marshalling generator or other analyzers while inspecting the project.
        var compilation = await project.WithAnalyzerReferences([]).GetCompilationAsync(cancellationToken);
        if (compilation is not CSharpCompilation csharp)
        {
            throw new InvalidOperationException("Expected a C# project.");
        }
        var errors = csharp.SyntaxTrees.SelectMany(tree =>
        {
            return tree.GetDiagnostics();
        }).Where(diagnostic =>
        {
            return diagnostic.Severity == DiagnosticSeverity.Error;
        }).ToArray();
        if (errors.Length != 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors.Select(error =>
            {
                return error.ToString();
            })));
        }
        return csharp;
    }
}