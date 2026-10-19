namespace Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents;

/// <summary>
/// Refines a string wire type for this backend's CodeGen tool using a type's string constants.
/// This is build-time metadata only; it neither changes marshalling nor validates values.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
internal sealed class JSStringUnionAttribute(Type valuesType) : Attribute
{
    public Type ValuesType { get; } = valuesType;
}