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
    internal const string AssemblyHash = "0DA6AAC5209F504DA743ABD7926F6F528010E2CA7B884B4A20F5198F42F1A26D";
    internal const string MetadataHash = "5139D6BB87229495DE92FEC78F5F253E31C7D05BFE69A950CAE73C90975747E3";
    internal static void VerifyFile(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        using var hash = SHA256.Create();
        if (!StringComparer.OrdinalIgnoreCase.Equals(Convert.ToHexString(hash.ComputeHash(stream)), expected))
            throw new InvalidOperationException($"Build identity mismatch: {path}. Hook refused.");
    }

    internal static bool TryVerifyFile(string path, string expected, out string message)
    {
        try
        {
            VerifyFile(path, expected);
            message = $"Build identity matches: {path}.";
            return true;
        }
        catch (Exception error)
        {
            message = error.Message;
            return false;
        }
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

    internal static void VerifyByRefMarshalling(MethodInfo method)
    {
        foreach (var parameter in method.GetParameters().Where(p => p.ParameterType.IsByRef))
        {
            Type element = parameter.ParameterType.GetElementType()!;
            // The installed 1.4.6 bridge dereferences byrefs using ldind.i, even for
            // float parameters, and reads out storage before the original initializes it.
            // LoadSaveHeader additionally uses pointer-sized storage for a projected struct.
            // Conservatively quarantine all ref/out signatures until a replacement bridge is tested.
            throw new InvalidOperationException($"Unsupported ref/out parameter: {parameter.Name} ({Canonical(element)}). Current bridge is not ABI-safe for this category; original left unpatched.");
        }
    }

    internal static void VerifyKnownNativeCompatibility(MethodSpec spec)
    {
        // A/B save-load trials reproduce a native crash with this entry installed;
        // leaving only this entry unpatched permits all four plugins to enter the scene.
        // Its 14-byte conditional/tail-jump body suggests relocation trouble, but the
        // precise detour defect is not yet proven. Do not silently claim this hook works.
        if (spec.Type == "Nivalis.HoldableEntity" && spec.Name == "Update" &&
            spec.Parameters.Length == 0 && !spec.IsStatic && spec.ReturnType == "System.Void" &&
            Convert.ToInt64(spec.Rva, 16) == 0x840710)
            throw new InvalidOperationException("Known native hook incompatibility: HoldableEntity.Update at 0x840710 reproduces a save-load crash with the installed detour backend. Original left unpatched pending a verified bridge fix.");
    }

    internal static void VerifyReturnMarshalling(MethodInfo method, Func<Type, bool> isNativeValueType)
    {
        Type result = method.ReturnType;
        // GetValidSaves reproduced invalid generated IL followed by a native failure.
        // Quarantine this projected-struct return category until a correct bridge is tested.
        if (!result.IsValueType && isNativeValueType(result))
            throw new InvalidOperationException($"Unsupported native value-type return projection: {Canonical(result)}. Current Harmony bridge is not validated for this return ABI; original left unpatched.");
    }

    internal static void VerifySmallValueParameters(MethodInfo method, Func<Type, int?> projectedNativeSize)
    {
        foreach (var parameter in method.GetParameters())
        {
            Type type = parameter.ParameterType;
            if (type.IsByRef || type.IsValueType) continue;
            int? size = projectedNativeSize(type);
            // The installed bridge boxes projected structs from a pointer on x64.
            // Windows x64 instead passes 1/2/4/8 byte structs as inline register values.
            if (size is 1 or 2 or 4 or 8)
                throw new InvalidOperationException($"Unsupported inline native value-type parameter: {parameter.Name} ({Canonical(type)}, {size} bytes). Current bridge treats inline bits as a pointer; original left unpatched.");
        }
    }
}
