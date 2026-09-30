using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes;

namespace NightsHack.HookRuntime;

internal sealed partial class NativeSnapshots
{
    // Only generated interop field access and read-only BCL dictionary enumeration are invoked here.
    // No game Stats getter (or any lazy PlayerCharacter getter) is called.
    internal IEnumerable<ObservedValue> ReadStatEntries(Il2CppObjectBase instance)
    {
        string instanceType = TargetValidation.Canonical(instance.GetType());
        if (instanceType != "Nivalis.PlayerState" && instanceType != "Nivalis.SkillSystem.SkillLevelController") return Array.Empty<ObservedValue>();
        bool skills = instanceType == "Nivalis.SkillSystem.SkillLevelController";
        string label = skills ? "skills" : "stats";
        string dictionaryType = skills
            ? "System.Collections.Generic.Dictionary`2<System.Type,Nivalis.SkillSystem.SkillLevelController+PlayerSkillExperience>"
            : "System.Collections.Generic.Dictionary`2<Nivalis.PlayerStat,Nivalis.PlayerState+StatValue>";
        var output = new List<ObservedValue>();
        object? enumerator = null;
        try
        {
            object? holder = skills ? ReadGeneratedField(instance, "_playerData", "Nivalis.SkillSystem.SkillLevelController+PlayerExperience") : instance;
            if (holder == null) return output;
            object? dictionary = ReadGeneratedField(holder, skills ? "PerSkillExperience" : "_statValues", dictionaryType);
            if (dictionary == null) return output;
            enumerator = dictionary.GetType().GetMethod("GetEnumerator", Type.EmptyTypes)!.Invoke(dictionary, null);
            if (enumerator == null) throw new InvalidOperationException("No dictionary enumerator.");
            Type enumType = enumerator.GetType();
            var moveNext = enumType.GetMethod("MoveNext", Type.EmptyTypes)!;
            var current = enumType.GetProperty("Current")!;
            int count = 0;
            while (count < 32 && (bool)moveNext.Invoke(enumerator, null)!)
            {
                object pair = current.GetValue(enumerator)!;
                foreach (string member in new[] { "Key", "Value" })
                {
                    object? item = pair.GetType().GetProperty(member)!.GetValue(pair);
                    output.Add(Describe($"{label}[{count}].{member}", item));
                    if (item is Il2CppObjectBase native) output.AddRange(Read(native, $"{label}[{count}].{member}."));
                    else if (item != null && skills && member == "Value")
                    {
                        // Some generator versions project blittable structs as managed structs.
                        foreach (var field in item.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                            if ((field.Name == "Experience" && field.FieldType == typeof(float)) ||
                                (field.Name == "CurrentLevel" && field.FieldType == typeof(int)))
                                output.Add(Describe($"{label}[{count}].Value.{field.Name}", field.GetValue(item)));
                    }
                }
                count++;
            }
            output.Add(new(label + ".enumeration", dictionaryType, "bounded", $"entries={count}; limit=32; full={(count < 32)}"));
        }
        catch (Exception error)
        { output.Add(new(label + ".enumeration", "", "unavailable", error.GetBaseException().Message)); }
        finally
        {
            try { enumerator?.GetType().GetMethod("Dispose", Type.EmptyTypes)?.Invoke(enumerator, null); }
            catch { /* Read-only enumerator cleanup must never escape into game logic. */ }
        }
        return output;
    }

    private object? ReadGeneratedField(object instance, string fieldName, string expected)
    {
        if (!schemas.TryGetValue(instance.GetType(), out Schema? schema) ||
            !schema.Fields.Any(f => f.Spec.Name == fieldName && f.Spec.Type == expected))
            throw new InvalidOperationException($"Native field not validated: {fieldName}");
        var property = instance.GetType().GetProperty(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Generated field property missing: {fieldName}");
        if (TargetValidation.Canonical(property.PropertyType) != expected || property.GetIndexParameters().Length != 0)
            throw new InvalidOperationException($"Generated field projection mismatch: {fieldName}");
        return property.GetValue(instance);
    }

    internal IEnumerable<ObservedValue> ReadOwnedLocks(Il2CppObjectBase instance)
    {
        var output = new List<ObservedValue>();
        if (!schemas.TryGetValue(instance.GetType(), out Schema? schema)) return output;
        foreach (var field in schema.Fields.Where(f => f.Spec.Type is "Nivalis.OverrideableBool" or "Nivalis.OverrideableBool+OverrideLock"))
        {
            try
            {
                if (ReadGeneratedField(instance, field.Spec.Name, field.Spec.Type) is Il2CppObjectBase value)
                    output.AddRange(Read(value, "lock." + field.Spec.Name + "."));
            }
            catch (Exception error) { output.Add(new("lock." + field.Spec.Name, field.Spec.Type, "unavailable", error.GetBaseException().Message)); }
        }
        return output;
    }
}
