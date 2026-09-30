using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace NightsHack.HookRuntime;

public sealed record FieldSpec(string Name, string Type, bool ValueType);
public sealed record TypeSpec(string Name, FieldSpec[] Fields);
public sealed record MethodSpec(string Group, string Type, string Name, string ReturnType,
    string[] Parameters, string[] ParameterNames, bool[] OutParameters, string Rva, bool Sampled, bool IsStatic = false, bool SkipAfterInstance = false)
{
    public string Id => $"{Type}.{Name}({string.Join(",", Parameters)})";
}
public sealed record ExcludedSpec(string Type, string Name, string Rva, string Reason, string[] Aliases);

internal sealed record Catalog(TypeSpec[] Types, MethodSpec[] Methods, ExcludedSpec[] Excluded)
{
    internal static Catalog Load(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Embedded catalog missing.");
        var catalog = JsonSerializer.Deserialize<Catalog>(stream)
            ?? throw new InvalidOperationException("Invalid catalog.");
        if (catalog.Methods.Select(m => m.Id).Distinct().Count() != catalog.Methods.Length ||
            catalog.Methods.Select(m => m.Rva).Distinct().Count() != catalog.Methods.Length)
            throw new InvalidOperationException("Duplicate signature/native RVA in catalog.");
        return catalog;
    }
}

internal static class TargetValidation
{
    internal const string AssemblyHash = "9A0E32C2D09A5025F867D29BF39B9BEDD0715B513456617FBFD82C581E1A376D";
    internal const string MetadataHash = "C8BD44F74B47136AEAD259DC2B88F289C12CB01E083FEEECDCD096A6FC1B2CF9";
    internal static void VerifyFile(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        using var hash = SHA256.Create();
        if (!StringComparer.OrdinalIgnoreCase.Equals(Convert.ToHexString(hash.ComputeHash(stream)), expected))
            throw new InvalidOperationException($"Build identity mismatch: {path}. Hook refused.");
    }

    // Canonical names match Cecil metadata, allowing only known interop projection transformations.
    internal static string Canonical(Type type)
    {
        if (type.IsByRef) return Canonical(type.GetElementType()!) + "&";
        if (type.IsArray) return Canonical(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank()-1) + "]";
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition().FullName!;
            if (definition is "Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray`1" or
                "Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1")
                return Canonical(type.GetGenericArguments()[0]) + "[]";
            return Normalize(definition) + "<" + string.Join(",", type.GetGenericArguments().Select(Canonical)) + ">";
        }
        if (type.FullName == "Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray") return "System.String[]";
        return Normalize(type.FullName ?? type.Name);
    }

    private static string Normalize(string name) => name.StartsWith("Il2CppSystem.", StringComparison.Ordinal)
        ? name.Substring(6) : name;

    internal static MethodInfo Resolve(Type type, MethodSpec spec, string? verifiedNativeType = null)
    {
        if ((verifiedNativeType ?? Canonical(type)) != spec.Type) throw new InvalidOperationException("Declaring type mismatch.");
        var matches = type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static).Where(m =>
                m.Name == spec.Name && m.IsStatic == spec.IsStatic && !m.IsGenericMethod &&
                Canonical(m.ReturnType) == spec.ReturnType &&
                m.GetParameters().Select(p => Canonical(p.ParameterType)).SequenceEqual(spec.Parameters) &&
                m.GetParameters().Select(p => p.IsOut).SequenceEqual(spec.OutParameters)).ToArray();
        return matches.Length == 1 ? matches[0] : throw new InvalidOperationException($"Signature not unique/exact: {spec.Id}");
    }
}
