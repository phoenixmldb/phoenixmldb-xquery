using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>xs:QName($arg) - parses prefixed name into a QName value</summary>
public sealed class QNameConstructorFunction : TypeConstructorFunction
{
    public QNameConstructorFunction() : base("QName") { }

    protected override ValueTask<object?> InvokeCoreAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = AtomizeArg(arguments[0], context);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is QName q) return ValueTask.FromResult<object?>(q);
        // XPTY0004: xs:QName cast only accepts string/untypedAtomic, not numeric types
        if (arg is not string and not Xdm.XsUntypedAtomic)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"Cannot cast {arg.GetType().Name} to xs:QName");
        var s = arg.ToString()!.Trim();
        if (s.Length == 0)
            throw new Execution.XQueryRuntimeException("FORG0001", "Cannot cast empty string to xs:QName");
        var colonIdx = s.IndexOf(':', StringComparison.Ordinal);
        if (colonIdx > 0)
        {
            var prefix = s[..colonIdx];
            var localName = s[(colonIdx + 1)..];
            // Resolve prefix via in-scope namespace bindings
            string? nsUri = null;
            var qec = context as PhoenixmlDb.XQuery.Execution.QueryExecutionContext;
            var bindings = qec?.PrefixNamespaceBindings;
            if (bindings != null)
                bindings.TryGetValue(prefix, out nsUri);
            // Built-in predeclared namespace prefixes (always in static context per XPath 3.1 §2.1.1)
            if (string.IsNullOrEmpty(nsUri))
            {
                nsUri = prefix switch
                {
                    "fn" => "http://www.w3.org/2005/xpath-functions",
                    "xs" => "http://www.w3.org/2001/XMLSchema",
                    "xsi" => "http://www.w3.org/2001/XMLSchema-instance",
                    "math" => "http://www.w3.org/2005/xpath-functions/math",
                    "map" => "http://www.w3.org/2005/xpath-functions/map",
                    "array" => "http://www.w3.org/2005/xpath-functions/array",
                    "err" => "http://www.w3.org/2005/xqt-errors",
                    "local" => "http://www.w3.org/2005/xquery-local-functions",
                    "xml" => "http://www.w3.org/XML/1998/namespace",
                    _ => null
                };
            }
            // FONS0004: prefix must resolve to a statically-known namespace
            if (string.IsNullOrEmpty(nsUri))
                throw new Execution.XQueryRuntimeException("FONS0004",
                    $"No namespace binding for prefix '{prefix}' in xs:QName()");
            var nsId = new NamespaceId((uint)Math.Abs(nsUri.GetHashCode()));
            return ValueTask.FromResult<object?>(new QName(nsId, localName, prefix) { RuntimeNamespace = nsUri });
        }
        // No prefix: use the default element namespace from the static context
        // (XPath/XQuery §19.1 casting to xs:QName — see QT3 K2-SeqExprCast-201).
        {
            string? defaultNs = null;
            var qec = context as PhoenixmlDb.XQuery.Execution.QueryExecutionContext;
            var bindings = qec?.PrefixNamespaceBindings;
            if (bindings != null)
            {
                if (bindings.TryGetValue("", out var defNs))
                {
                    // xmlns="" explicitly undeclares the default namespace — don't fall through to prolog
                    if (!string.IsNullOrEmpty(defNs))
                        defaultNs = defNs;
                }
                else if (bindings.TryGetValue("##default-element", out var prologDefNs) && !string.IsNullOrEmpty(prologDefNs))
                    defaultNs = prologDefNs;
            }
            if (!string.IsNullOrEmpty(defaultNs))
            {
                var nsId = new NamespaceId((uint)Math.Abs(defaultNs.GetHashCode()));
                return ValueTask.FromResult<object?>(new QName(nsId, s, string.Empty) { RuntimeNamespace = defaultNs });
            }
        }
        return ValueTask.FromResult<object?>(new QName(NamespaceId.None, s));
    }
}
