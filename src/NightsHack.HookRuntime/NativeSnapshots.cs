using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;

namespace NightsHack.HookRuntime;

internal sealed partial class NativeSnapshots
{
    private sealed record FieldBinding(FieldSpec Spec, IntPtr Field, bool IsValueType, bool IsEnum, int Size);
    private sealed record Schema(Type Type, FieldBinding[] Fields);
    private readonly Dictionary<Type, Schema> schemas = new();
    internal IReadOnlyList<string> Diagnostics => diagnostics.AsReadOnly();
    private readonly List<string> diagnostics = new();

    internal NativeSnapshots(Assembly assembly, IEnumerable<TypeSpec> specs)
    {
        foreach (var spec in specs)
        {
            try
            {
                Type type = ResolveType(assembly, spec.Name);
                IntPtr klass = Il2CppClassPointerStore.GetNativeClassPointer(type);
                if (klass == IntPtr.Zero) throw new InvalidOperationException("No native class.");
                var fields = new List<FieldBinding>();
                foreach (var field in spec.Fields)
                {
                    try
                    {
                        IntPtr native = IL2CPP.il2cpp_class_get_field_from_name(klass, field.Name);
                        if (native == IntPtr.Zero || (IL2CPP.il2cpp_field_get_flags(native) & 0x10) != 0)
                            throw new InvalidOperationException("Missing or static field.");
                        IntPtr fieldClass = IL2CPP.il2cpp_class_from_type(IL2CPP.il2cpp_field_get_type(native));
                        if (fieldClass == IntPtr.Zero) throw new InvalidOperationException("No field class.");
                        string nativeName = NativeName(fieldClass);
                        string expectedName = field.Type.Split('<')[0];
                        // A reference collection's generic arguments are not traversed by raw reads.
                        if (nativeName != expectedName)
                            throw new InvalidOperationException($"Native field type mismatch: {nativeName} != {expectedName}");
                        bool valueType = IL2CPP.il2cpp_class_is_valuetype(fieldClass);
                        uint alignment = 0;
                        int size = valueType ? IL2CPP.il2cpp_class_value_size(fieldClass, ref alignment) : IntPtr.Size;
                        if (size < 1 || size > 256) throw new InvalidOperationException($"Unsupported native value size: {size}");
                        fields.Add(new FieldBinding(field, native, valueType, IL2CPP.il2cpp_class_is_enum(fieldClass), size));
                    }
                    catch (Exception error) { diagnostics.Add($"{spec.Name}.{field.Name}: {error.Message}"); }
                }
                schemas.Add(type, new Schema(type, fields.ToArray()));
            }
            catch (Exception error) { diagnostics.Add($"{spec.Name}: {error.Message}"); }
        }
    }

    internal static Type ResolveType(Assembly assembly, string name)
    {
        Type? direct = assembly.GetType(name, false);
        if (direct != null) return direct;
        int nested = name.LastIndexOf('+');
        if (nested < 0) throw new TypeLoadException(name);
        Type parent = ResolveType(assembly, name.Substring(0, nested));
        // Interop may sanitize iterator names. Verify native identity instead of guessing spelling.
        var matches = parent.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Where(t => !t.ContainsGenericParameters)
            .Where(t => {
                IntPtr klass = Il2CppClassPointerStore.GetNativeClassPointer(t);
                return klass != IntPtr.Zero && NativeName(klass) == name;
            }).ToArray();
        return matches.Length == 1 ? matches[0] : throw new TypeLoadException("Native nested type not unique: " + name);
    }

    internal static string NativeName(IntPtr klass)
    {
        string name = Marshal.PtrToStringAnsi(IL2CPP.il2cpp_class_get_name(klass)) ?? "";
        // IL2CPP versions may include constructed generic arguments in the class name.
        int argumentStart = name.IndexOf('<');
        // Compiler-generated iterator names begin with '<'; these are not generic arguments.
        if (argumentStart > 0) name = name.Substring(0, argumentStart);
        IntPtr declaring = IL2CPP.il2cpp_class_get_declaring_type(klass);
        if (declaring != IntPtr.Zero) return NativeName(declaring) + "+" + name;
        string ns = Marshal.PtrToStringAnsi(IL2CPP.il2cpp_class_get_namespace(klass)) ?? "";
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    internal IEnumerable<ObservedValue> Read(Il2CppObjectBase instance, string prefix = "field.")
    {
        if (instance.Pointer == IntPtr.Zero) throw new InvalidOperationException("Null native instance.");
        Type? type = instance.GetType();
        var output = new List<ObservedValue>();
        while (type != null)
        {
            if (schemas.TryGetValue(type, out Schema? schema))
                output.AddRange(schema.Fields.Select(field => ReadField(instance, field, prefix + schema.Type.Name + ".")));
            type = type.BaseType;
        }
        return output;
    }

    private static unsafe ObservedValue ReadField(Il2CppObjectBase instance, FieldBinding field, string prefix)
    {
        // A size checked buffer, populated only through the official field API; no fixed object offsets.
        byte* buffer = stackalloc byte[256];
        IL2CPP.il2cpp_field_get_value(instance.Pointer, field.Field, buffer);
        var bytes = new ReadOnlySpan<byte>(buffer, field.Size);
        string kind, value;
        if (!field.IsValueType && field.Spec.Type == "System.String")
        {
            IntPtr textPointer = new(BitConverter.ToInt64(bytes));
            if (textPointer == IntPtr.Zero) { kind = "null"; value = ""; }
            else
            {
                int length = IL2CPP.il2cpp_string_length(textPointer);
                if (length < 0) throw new InvalidOperationException("Invalid IL2CPP string length.");
                kind = length > 160 ? "string-truncated" : "string";
                value = new string(IL2CPP.il2cpp_string_chars(textPointer), 0, Math.Min(160, length));
            }
        }
        else if (!field.IsValueType) { kind = "reference"; value = $"0x{BitConverter.ToInt64(bytes):X}"; }
        else if (field.IsEnum && field.Size == 4)
        { kind = "enum-int32"; value = BitConverter.ToInt32(bytes).ToString(CultureInfo.InvariantCulture); }
        else if (field.Spec.Type == "Nivalis.PlayerManager+PlayerKnowledge" && field.Size == 9)
        {
            kind = "knowledge";
            string[] names = { "fishing", "trading", "navigation", "map", "inspiration", "storage", "farming", "renting", "hiring" };
            var flags = new string[9];
            for (int i=0; i<9; i++) flags[i] = names[i] + "=" + (bytes[i] != 0);
            value = string.Join(",", flags);
        }
        else
        {
            kind = "scalar";
            value = field.Spec.Type switch
            {
                "System.Boolean" when bytes.Length == 1 => (bytes[0] != 0).ToString(),
                "System.Int32" when bytes.Length == 4 => BitConverter.ToInt32(bytes).ToString(CultureInfo.InvariantCulture),
                "System.UInt32" when bytes.Length == 4 => BitConverter.ToUInt32(bytes).ToString(CultureInfo.InvariantCulture),
                "System.Single" when bytes.Length == 4 => BitConverter.ToSingle(bytes).ToString("R", CultureInfo.InvariantCulture),
                "System.Int64" when bytes.Length == 8 => BitConverter.ToInt64(bytes).ToString(CultureInfo.InvariantCulture),
                "System.Double" when bytes.Length == 8 => BitConverter.ToDouble(bytes).ToString("R", CultureInfo.InvariantCulture),
                _ => ""
            };
            if (value.Length == 0)
            {
                if ((field.Spec.Type == "UnityEngine.Vector2" && bytes.Length == 8) ||
                    (field.Spec.Type == "UnityEngine.Vector3" && bytes.Length == 12) ||
                    (field.Spec.Type == "UnityEngine.Quaternion" && bytes.Length == 16))
                {
                    kind = "vector";
                    var components = new string[bytes.Length/4];
                    for (int i = 0; i < components.Length; i++)
                        components[i] = BitConverter.ToSingle(bytes.Slice(i*4,4)).ToString("R", CultureInfo.InvariantCulture);
                    value = string.Join(",", components);
                }
                else { kind = "native-value-bytes"; value = Convert.ToHexString(bytes); }
            }
        }
        return new ObservedValue(prefix + field.Spec.Name, field.Spec.Type, kind, value);
    }

    internal IEnumerable<ObservedValue> Arguments(object[] args, MethodSpec spec, bool before, bool playerDetails = false)
    {
        var output = new List<ObservedValue>();
        for (int i = 0; i < args.Length && i < spec.Parameters.Length; i++)
        {
            string name = "arg." + spec.ParameterNames[i];
            if (before && spec.OutParameters[i])
            { output.Add(new(name, spec.Parameters[i], "out-uninitialized", "")); continue; }
            output.Add(Describe(name, args[i]));
            if (playerDetails && args[i] is Il2CppObjectBase native && spec.Parameters[i] == "Nivalis.PlayerStat")
                output.AddRange(Read(native, name + "."));
        }
        return output;
    }

    internal static ObservedValue Describe(string name, object? value)
    {
        if (value == null) return new(name, "", "null", "");
        var type = value.GetType();
        string canonical = TargetValidation.Canonical(type);
        if (value is Il2CppObjectBase native) return new(name, canonical, "reference", $"0x{native.Pointer.ToInt64():X}");
        if (value is string text) return new(name, canonical, "string", text.Length > 160 ? text.Substring(0,160) + "…" : text);
        if (type.IsPrimitive || type.IsEnum || value is decimal)
            return new(name, canonical, "scalar", Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
        // Do not call user-defined ToString, properties or business methods on arbitrary arguments.
        if (type.IsValueType && canonical is "UnityEngine.Vector2" or "UnityEngine.Vector3" or "UnityEngine.Quaternion")
        {
            var components = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(float)).Select(f => $"{f.Name}={Convert.ToString(f.GetValue(value), CultureInfo.InvariantCulture)}");
            return new(name, canonical, "vector", string.Join(",", components));
        }
        return new(name, canonical, "opaque", "Not expanded");
    }

}
