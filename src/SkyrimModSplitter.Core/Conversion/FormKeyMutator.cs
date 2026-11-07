using System.Reflection;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;

namespace SkyrimModSplitter.Core.Conversion;

/// <summary>
/// Mutagen 0.50.2 exposes the FormKey setter as non-public. Renumbering needs it,
/// so this is the single, audited place where reflection touches plugin records.
/// </summary>
internal static class FormKeyMutator
{
    public static void SetFormKey(IMajorRecordGetter record, FormKey formKey)
    {
        var property = record.GetType().GetProperty("FormKey", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("record has no FormKey property: " + record.GetType().FullName);
        var setter = property.GetSetMethod(nonPublic: true)
            ?? throw new InvalidOperationException("record FormKey setter not found: " + record.GetType().FullName);
        setter.Invoke(record, new object[] { formKey });
    }
}
