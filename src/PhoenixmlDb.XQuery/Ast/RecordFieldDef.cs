namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Field definition for record types (XPath 4.0).
/// </summary>
public sealed class RecordFieldDef
{
    /// <summary>Field name.</summary>
    public required string Name { get; init; }
    /// <summary>Field type. Null means any type.</summary>
    public XdmSequenceType? Type { get; init; }
    /// <summary>Whether the field is optional (has ? suffix).</summary>
    public bool Optional { get; init; }
}
