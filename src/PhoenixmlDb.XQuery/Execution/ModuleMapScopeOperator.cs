namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// Makes the host's module maps (<see cref="CompilationOptions.ExternalModules"/> and
/// <see cref="CompilationOptions.ExternalModuleLocations"/>) visible at run time for the duration
/// of the query. The static analyzer consults them for <c>import module</c>, but
/// <c>fn:load-xquery-module</c> compiles its module at run time with fresh options, so a module
/// the host had mapped was "not resolved from location hints: []" there (QT3
/// fn-load-xquery-module-*).
/// </summary>
internal sealed class ModuleMapScopeOperator : PhysicalOperator
{
    public required PhysicalOperator Inner { get; init; }
    public IReadOnlyDictionary<string, List<string>>? ExternalModules { get; init; }
    public IReadOnlyDictionary<string, string>? ExternalModuleLocations { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var savedModules = context.ExternalModules;
        var savedLocations = context.ExternalModuleLocations;
        context.ExternalModules = ExternalModules ?? savedModules;
        context.ExternalModuleLocations = ExternalModuleLocations ?? savedLocations;
        try
        {
            await foreach (var item in Inner.ExecuteAsync(context).ConfigureAwait(false))
                yield return item;
        }
        finally
        {
            context.ExternalModules = savedModules;
            context.ExternalModuleLocations = savedLocations;
        }
    }
}
