namespace PhoenixmlDb.XQuery.Security;

/// <summary>
/// Categorizes the type of resource access being requested.
/// </summary>
[Flags]
public enum ResourceAccessKind
{
    None = 0,
    ReadDocument = 1,
    ReadText = 2,
    ReadCollection = 4,
    WriteDocument = 8,
    ImportStylesheet = 16,
    AllRead = ReadDocument | ReadText | ReadCollection,
    AllWrite = WriteDocument,
    All = AllRead | AllWrite | ImportStylesheet
}
