using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents.CodeGen;

internal sealed class ContractGenerator
{
    private const string ImportAttribute = "System.Runtime.InteropServices.JavaScript.JSImportAttribute";
    private const string ExportAttribute = "System.Runtime.InteropServices.JavaScript.JSExportAttribute";
    private const string UnionAttribute = "Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents.JSStringUnionAttribute";
    // Reserved words and context-sensitive restrictions for module/strict-mode bindings:
    // https://tc39.es/ecma262/multipage/ecmascript-language-lexical-grammar.html#sec-keywords-and-reserved-words
    // https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Lexical_grammar#reserved_words
    // eval/arguments are not keywords, but strict-mode binding restrictions also apply (ECMA-262 Note 3).
    private static readonly HashSet<string> ReservedParameterNames = new(StringComparer.Ordinal)
    {
        "arguments", "await", "break", "case", "catch", "class", "const", "continue",
        "debugger", "default", "delete", "do", "else", "enum", "eval", "export", "extends",
        "false", "finally", "for", "function", "if", "implements", "import", "in",
        "instanceof", "interface", "let", "new", "null", "package", "private", "protected",
        "public", "return", "static", "super", "switch", "this", "throw", "true", "try",
        "typeof", "var", "void", "while", "with", "yield",
    };
    private readonly SortedDictionary<string, string[]> _unions = new(StringComparer.Ordinal);
    private readonly List<Method> _imports = [];
    private readonly List<Method> _exports = [];

    internal static string Generate(CSharpCompilation compilation)
    {
        var generator = new ContractGenerator();
        generator.ReadMethods(compilation);
        return generator.Emit(compilation.AssemblyName!);
    }

    private void ReadMethods(CSharpCompilation compilation)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var syntax in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(syntax) is not IMethodSymbol method)
                {
                    continue;
                }
                // A source declaration is the contract; generated marshalling implementations
                // are deliberately not inputs to this tool.
                if (method.PartialDefinitionPart is not null)
                {
                    continue;
                }
                // Unresolved attributes must not silently remove a method from the contract.
                var declarationAttributes = syntax.AttributeLists.SelectMany(list =>
                {
                    return list.Attributes;
                }).Concat(syntax.ParameterList.Parameters.SelectMany(parameter =>
                {
                    return parameter.AttributeLists.SelectMany(list =>
                    {
                        return list.Attributes;
                    });
                }));
                foreach (var attributeSyntax in declarationAttributes)
                {
                    if (model.GetSymbolInfo(attributeSyntax).Symbol is not IMethodSymbol)
                    {
                        throw Error(method, $"Unresolved or invalid declaration attribute: {attributeSyntax}");
                    }
                }
                var attributes = method.GetAttributes();
                var import = attributes.SingleOrDefault(attribute =>
                {
                    return attribute.AttributeClass?.ToDisplayString() == ImportAttribute;
                });
                var export = attributes.SingleOrDefault(attribute =>
                {
                    return attribute.AttributeClass?.ToDisplayString() == ExportAttribute;
                });
                if (import is null && export is null)
                {
                    continue;
                }
                if (import is not null && export is not null)
                {
                    throw Error(method, "A method cannot be both an import and an export.");
                }
                if (!method.IsStatic || method.IsGenericMethod || method.ContainingType.IsGenericType
                    || method.ContainingType.ContainingType is not null || method.ReturnsByRef || method.ReturnsByRefReadonly
                    || syntax.Modifiers.Any(SyntaxKind.AsyncKeyword))
                {
                    throw Error(method, "Only static, non-generic, synchronous methods on non-nested types are supported.");
                }
                var name = method.Name;
                string? module = null;
                if (import is not null)
                {
                    if (import.ConstructorArguments.Length != 2
                        || import.ConstructorArguments[0].Value is not string importName
                        || import.ConstructorArguments[1].Value is not string moduleName
                        || string.IsNullOrWhiteSpace(moduleName))
                    {
                        throw Error(method, "Imports require constant function and logical module names.");
                    }
                    name = importName;
                    module = moduleName;
                }
                RequireIdentifier(name, method);
                RequireIdentifier(method.ContainingType.Name, method);
                var parameters = new List<string>();
                var parameterNames = method.Parameters.Select(parameter =>
                {
                    return parameter.Name;
                }).ToHashSet(StringComparer.Ordinal);
                foreach (var parameter in method.Parameters)
                {
                    if (parameter.RefKind != RefKind.None || parameter.IsParams || parameter.IsOptional)
                    {
                        throw Error(parameter, "ref/out/in, params and optional interop parameters are not supported.");
                    }
                    var parameterName = parameter.Name;
                    if (ReservedParameterNames.Contains(parameterName))
                    {
                        do
                        {
                            parameterName += "_";
                        }
                        while (!parameterNames.Add(parameterName));
                    }
                    parameters.Add($"{parameterName}: {MapType(parameter.Type, parameter.GetAttributes(), parameter)}");
                }
                var result = MapType(method.ReturnType, method.GetReturnTypeAttributes(), method);
                var item = new Method(method, name, module, string.Join(", ", parameters), result);
                if (import is not null)
                {
                    _imports.Add(item);
                }
                else
                {
                    _exports.Add(item);
                }
            }
        }
    }

    private string MapType(ITypeSymbol type, IEnumerable<AttributeData> attributes, ISymbol source)
    {
        var all = attributes.ToArray();
        if (all.Any(attribute =>
        {
            return attribute.AttributeClass?.MetadataName == "JSMarshalAsAttribute`1"
                && attribute.AttributeClass.ContainingNamespace.ToDisplayString() == "System.Runtime.InteropServices.JavaScript";
        }))
        {
            throw Error(source, "JSMarshalAs requires an explicit generator mapping; it cannot be inferred from the managed type.");
        }
        var union = all.SingleOrDefault(attribute =>
        {
            return attribute.AttributeClass?.ToDisplayString() == UnionAttribute;
        });
        string result;
        if (union is not null)
        {
            if (type.SpecialType != SpecialType.System_String || union.ConstructorArguments.Length != 1
                || union.ConstructorArguments[0].Value is not INamedTypeSymbol valuesType)
            {
                throw Error(source, "JSStringUnion requires a string boundary and a type containing string constants.");
            }
            RequireIdentifier(valuesType.Name, source);
            var values = valuesType.GetMembers().OfType<IFieldSymbol>().Where(field =>
            {
                return field.IsConst;
            }).Select(field =>
            {
                if (field.ConstantValue is not string value)
                {
                    throw Error(field, "String union constants must be non-null strings.");
                }
                return value;
            }).Order(StringComparer.Ordinal).ToArray();
            if (values.Length == 0 || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            {
                throw Error(source, "String unions require nonempty, distinct string constants.");
            }
            if (_unions.TryGetValue(valuesType.Name, out var previous) && !previous.SequenceEqual(values))
            {
                throw Error(source, $"Conflicting string union name: {valuesType.Name}");
            }
            _unions[valuesType.Name] = values;
            result = valuesType.Name;
        }
        else
        {
            result = type.SpecialType switch
            {
                SpecialType.System_Void => "void",
                SpecialType.System_String => "string",
                SpecialType.System_Boolean => "boolean",
                SpecialType.System_Int32 or SpecialType.System_Double => "number",
                _ => throw Error(source, $"Unsupported interop type '{type.ToDisplayString()}'; no 'any' fallback is generated.")
            };
        }
        if (type.SpecialType == SpecialType.System_String && type.NullableAnnotation == NullableAnnotation.Annotated)
        {
            result += " | null";
        }
        return result;
    }

    private string Emit(string assemblyName)
    {
        var importType = SingleType(_imports, "import");
        var exportType = SingleType(_exports, "export");
        var modules = _imports.Select(method =>
        {
            return method.Module;
        }).Distinct(StringComparer.Ordinal).ToArray();
        if (modules.Length != 1)
        {
            throw new InvalidOperationException("This generator currently supports one logical import module per output.");
        }
        if (_exports.GroupBy(method =>
        {
            return method.Name;
        }).Any(group =>
        {
            return group.Count() != 1;
        }))
        {
            throw new InvalidOperationException("Overloaded JSExports are not supported.");
        }
        var importsName = importType.Name + "Imports";
        var exportsName = exportType.Name + "Exports";
        var assemblyConstant = exportType.Name + "AssemblyName";
        var moduleConstant = importType.Name + "ModuleName";
        if (_unions.Keys.Intersect([importsName, exportsName, assemblyConstant, moduleConstant,
            "resolve" + exportsName, "isRecord", "exportMethods"]).Any())
        {
            throw new InvalidOperationException("String union name conflicts with a generated declaration.");
        }
        var text = new StringBuilder();
        text.AppendLine("// <auto-generated> Wasm.FluentUIWebComponents.CodeGen. Edit the C# interop declarations, not this file. </auto-generated>");
        text.AppendLine("// This describes the wire contract; it does not replace the LLVM marshalling generator.");
        text.AppendLine();
        text.AppendLine($"export const {moduleConstant} = {Quote(modules[0]!)};");
        text.AppendLine($"export const {assemblyConstant} = {Quote(assemblyName + ".dll")};");
        foreach (var (name, values) in _unions)
        {
            text.AppendLine($"export type {name} = {string.Join(" | ", values.Select(Quote))};");
        }
        EmitInterface(text, importsName, _imports);
        EmitInterface(text, exportsName, _exports);
        var names = _exports.Select(method =>
        {
            return method.Name;
        }).Order(StringComparer.Ordinal).ToArray();
        text.AppendLine($"const exportMethods = [{string.Join(", ", names.Select(Quote))}] as const;");
        text.AppendLine();
        text.AppendLine("function isRecord(value: unknown): value is Record<string, unknown> {");
        text.AppendLine("    return typeof value === 'object' && value !== null;");
        text.AppendLine("}");
        text.AppendLine();
        text.AppendLine($"export function resolve{exportsName}(assembly: unknown): {exportsName} {{");
        text.AppendLine("    let value: unknown = assembly;");
        var path = exportType.ToDisplayString().Split('.');
        text.AppendLine($"    for (const name of [{string.Join(", ", path.Select(Quote))}]) {{");
        text.AppendLine("        if (!isRecord(value)) {");
        text.AppendLine($"            throw new Error({Quote("Invalid assembly export path: " + exportType.ToDisplayString())});");
        text.AppendLine("        }");
        text.AppendLine("        value = value[name];");
        text.AppendLine("    }");
        text.AppendLine("    if (!isRecord(value)) {");
        text.AppendLine("        throw new Error('Invalid interop exports object');");
        text.AppendLine("    }");
        text.AppendLine("    for (const name of exportMethods) {");
        text.AppendLine("        if (typeof value[name] !== 'function') {");
        text.AppendLine("            throw new Error(`Missing interop export: ${name}`);");
        text.AppendLine("        }");
        text.AppendLine("    }");
        text.AppendLine("    // Runtime checks establish presence, not argument types or protocol-version compatibility.");
        text.AppendLine($"    return value as unknown as {exportsName};");
        text.AppendLine("}");
        return text.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static INamedTypeSymbol SingleType(List<Method> methods, string direction)
    {
        var types = methods.Select(method =>
        {
            return method.Symbol.ContainingType;
        }).Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default).ToArray();
        if (types.Length != 1)
        {
            throw new InvalidOperationException($"Expected one {direction} declaring type per output; found {types.Length}.");
        }
        return types[0];
    }

    private static void EmitInterface(StringBuilder text, string name, List<Method> methods)
    {
        text.AppendLine();
        text.AppendLine($"export interface {name} {{");
        foreach (var group in methods.GroupBy(method =>
        {
            return method.Name;
        }, StringComparer.Ordinal).OrderBy(group =>
        {
            return group.Key;
        }, StringComparer.Ordinal))
        {
            var overloads = group.OrderBy(method =>
            {
                return method.Parameters;
            }, StringComparer.Ordinal).ToArray();
            if (overloads.Length == 1)
            {
                var method = overloads[0];
                text.AppendLine($"    // {method.Symbol.ContainingType.ToDisplayString()}.{method.Symbol.Name}");
                // Function properties, not method signatures: strictFunctionTypes must reject
                // implementations that cannot handle null or all values of a string union.
                text.AppendLine($"    {method.Name}: ({method.Parameters}) => {method.Result};");
            }
            else
            {
                text.AppendLine($"    {group.Key}: {{");
                foreach (var method in overloads)
                {
                    text.AppendLine($"        // {method.Symbol.ContainingType.ToDisplayString()}.{method.Symbol.Name}");
                    text.AppendLine($"        ({method.Parameters}): {method.Result};");
                }
                text.AppendLine("    };");
            }
        }
        text.AppendLine("}");
        text.AppendLine();
    }

    private static string Quote(string value)
    {
        return JsonSerializer.Serialize(value);
    }

    private static void RequireIdentifier(string name, ISymbol source)
    {
        if (!Regex.IsMatch(name, "^[A-Za-z_$][A-Za-z0-9_$]*$", RegexOptions.CultureInvariant))
        {
            throw Error(source, $"Unsupported JavaScript identifier: {name}");
        }
    }

    private static InvalidOperationException Error(ISymbol symbol, string message)
    {
        var span = symbol.Locations.FirstOrDefault()?.GetLineSpan();
        return new InvalidOperationException($"{span?.Path}({span?.StartLinePosition.Line + 1}): {symbol.Name}: {message}");
    }

    private sealed record Method(IMethodSymbol Symbol, string Name, string? Module, string Parameters, string Result);
}