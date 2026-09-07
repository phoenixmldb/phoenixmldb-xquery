using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.Xdm.Serialization;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;
using PhoenixmlDb.XQuery.Optimizer;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// Try-catch expression.
/// </summary>
public sealed class TryCatchOperator : PhysicalOperator
{
    public required PhysicalOperator TryOperator { get; init; }
    public required IReadOnlyList<CatchClauseOperator> CatchClauses { get; init; }
    public NamespaceId ErrorNamespaceId { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        List<object?> results;
        try
        {
            results = new List<object?>();
            await foreach (var item in TryOperator.ExecuteAsync(context))
                results.Add(item);
        }
        catch (XQueryRuntimeException ex)
        {
            results = await ExecuteCatchAsync(ex, context);
        }
        catch (Functions.XQueryException ex)
        {
            // fn:error() throws XQueryException — wrap and catch
            var wrapped = new XQueryRuntimeException(ex.ErrorCode, ex.Message) { ErrorNamespaceUri = ex.ErrorNamespaceUri, ErrorPrefix = ex.ErrorPrefix, ErrorValue = ex.ErrorValue };
            results = await ExecuteCatchAsync(wrapped, context);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Catch .NET runtime errors (NullRef, InvalidCast, etc.) and wrap as XPDY0002/FOER0000
            var errorCode = ex switch
            {
                NullReferenceException => "XPDY0002",
                InvalidCastException => "XPTY0004",
                ArgumentException => "FOER0000",
                OverflowException => "FOAR0002",
                _ => "FOER0000"
            };
            var wrapped = new XQueryRuntimeException(errorCode, ex.Message);
            results = await ExecuteCatchAsync(wrapped, context);
        }

        foreach (var item in results)
            yield return item;
    }

    private async Task<List<object?>> ExecuteCatchAsync(XQueryRuntimeException ex, QueryExecutionContext context)
    {
        foreach (var clause in CatchClauses)
        {
            if (clause.Matches(ex.ErrorCode, ex.ErrorNamespaceUri))
            {
                var catchResults = new List<object?>();
                context.PushScope();
                // Bind err:* implicit variables per XQuery 3.1 §3.15.1
                // Variable names must use the actual err namespace ID so runtime lookups match
                // the namespace-resolved QNames produced by NamespaceResolver.
                var errNsId = ErrorNamespaceId;
                var errCode = ex.ErrorCode ?? "FOER0000";
                // The value of $err:code is a QName — use the actual namespace URI
                // so fn:namespace-uri-from-QName($err:code) works
                var errNsUri = ex.ErrorNamespaceUri ?? "http://www.w3.org/2005/xqt-errors";
                var errPrefix = ex.ErrorPrefix ?? "err";
                var errCodeQName = new QName(ErrorNamespaceId, errCode, errPrefix)
                    { RuntimeNamespace = errNsUri };
                context.BindVariable(new QName(errNsId, "code", "err"), errCodeQName);
                context.BindVariable(new QName(errNsId, "description", "err"), ex.Message ?? "");
                context.BindVariable(new QName(errNsId, "value", "err"), ex.ErrorValue);
                context.BindVariable(new QName(errNsId, "module", "err"), "");
                context.BindVariable(new QName(errNsId, "line-number", "err"), 0L);
                context.BindVariable(new QName(errNsId, "column-number", "err"), 0L);
                context.BindVariable(new QName(errNsId, "additional", "err"), null);
                try
                {
                    await foreach (var item in clause.ResultOperator.ExecuteAsync(context))
                        catchResults.Add(item);
                }
                finally { context.PopScope(); }
                return catchResults;
            }
        }
        throw ex; // No matching catch clause
    }
}
