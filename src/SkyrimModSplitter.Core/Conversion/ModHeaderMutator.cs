using System.Reflection;
using Mutagen.Bethesda.Skyrim;

namespace SkyrimModSplitter.Core.Conversion;

internal static class ModHeaderMutator
{
    public static void SetNextFormId(SkyrimMod mod, uint nextFormId)
    {
        var stats = mod.ModHeader.Stats;
        var property = stats.GetType().GetProperty("NextFormID", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("ModStats.NextFormID property not found");
        var setter = property.GetSetMethod(nonPublic: true)
            ?? throw new InvalidOperationException("ModStats.NextFormID setter not found");
        setter.Invoke(stats, new object[] { nextFormId });
    }
}
