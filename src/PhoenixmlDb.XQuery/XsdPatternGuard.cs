using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Schema;

namespace PhoenixmlDb.XQuery;

/// <summary>
/// Bounds the regular expressions behind XSD <c>pattern</c> facets. System.Xml builds them with
/// no match timeout of its own, so a pattern that backtracks catastrophically runs for as long as
/// the value makes it, in a cast to a schema type, in validation, and while the schema compiles
/// (which checks the schema's own enumeration, default and fixed values against its patterns).
/// </summary>
/// <remarks>
/// <para>
/// A compiled schema set keeps each type's patterns as <see cref="Regex"/> objects in a list that
/// has no public accessor. <see cref="Bound"/> replaces each with the same expression and a match
/// timeout, which is the only way to reach the matches a validating reader makes: it sees a value
/// and its type together, and nothing outside it does before the match runs.
/// </para>
/// <para>
/// That reads System.Xml's private state, so it fails closed: where the expected shape is missing
/// and the schema set declares a pattern, <see cref="Bound"/> throws rather than report a bound it
/// did not apply.
/// </para>
/// </remarks>
internal static class XsdPatternGuard
{
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly Type? FacetsType =
        typeof(XmlSchemaDatatype).Assembly.GetType("System.Xml.Schema.RestrictionFacets");
    private static readonly FieldInfo? PatternsField = FacetsType?.GetField("Patterns", AnyInstance);

    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> SchemaObjectProperties = new();
    private static readonly ConcurrentDictionary<Type, FieldInfo[]> DatatypeFields = new();

    /// <summary>
    /// Gives every pattern facet of a compiled schema set the match timeout
    /// <paramref name="timeout"/>. Returns how many patterns the set's types carry.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// The set declares a pattern facet and none could be reached on this runtime.
    /// </exception>
    public static int Bound(XmlSchemaSet set, TimeSpan timeout)
    {
        var objects = Walk(set);
        var seenDatatypes = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var replacements = new Dictionary<Regex, Regex>(ReferenceEqualityComparer.Instance);
        var patterns = 0;
        foreach (var obj in objects)
            if (obj is XmlSchemaType { Datatype: { } datatype })
                patterns += BoundDatatype(datatype, timeout, seenDatatypes, replacements);

        if (patterns == 0 && objects.Any(o => o is XmlSchemaPatternFacet))
            throw new NotSupportedException(
                "The schema declares pattern facets, and this runtime's System.Xml does not keep them where " +
                "PhoenixmlDb can give them a match timeout. Set the process-wide default instead, before any " +
                "regular expression is used: AppDomain.CurrentDomain.SetData(\"REGEX_DEFAULT_MATCH_TIMEOUT\", …).");
        return patterns;
    }

    private static int BoundDatatype(object datatype, TimeSpan timeout, HashSet<object> seen,
        Dictionary<Regex, Regex> replacements)
    {
        if (!seen.Add(datatype))
            return 0;
        var patterns = 0;
        var fields = DatatypeFields.GetOrAdd(datatype.GetType(), static type =>
        {
            var all = new List<FieldInfo>();
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
                all.AddRange(t.GetFields(AnyInstance | BindingFlags.DeclaredOnly));
            return all.ToArray();
        });
        foreach (var field in fields)
        {
            switch (field.GetValue(datatype))
            {
                case null:
                    break;
                case var facets when FacetsType is not null && FacetsType.IsInstanceOfType(facets):
                    if (PatternsField?.GetValue(facets) is not IList list)
                        break;
                    for (var i = 0; i < list.Count; i++)
                    {
                        if (list[i] is not Regex regex)
                            continue;
                        patterns++;
                        if (regex.MatchTimeout == timeout)
                            continue;
                        if (!replacements.TryGetValue(regex, out var bounded))
                            replacements[regex] = bounded = new Regex(regex.ToString(), regex.Options, timeout);
                        list[i] = bounded;
                    }
                    break;
                // A list's item type and a union's member types: reached through the schema
                // objects as well, but a datatype derived from one keeps its own reference.
                case XmlSchemaDatatype inner:
                    patterns += BoundDatatype(inner, timeout, seen, replacements);
                    break;
                case XmlSchemaType { Datatype: { } inner }:
                    patterns += BoundDatatype(inner, timeout, seen, replacements);
                    break;
                case XmlSchemaType?[] types:
                    foreach (var type in types)
                        if (type?.Datatype is { } member)
                            patterns += BoundDatatype(member, timeout, seen, replacements);
                    break;
            }
        }
        return patterns;
    }

    /// <summary>
    /// Runs every pattern facet of the set against every value the schemas themselves supply
    /// (other facets' values, defaults and fixed values), each match under
    /// <paramref name="timeout"/>, before the set is compiled. Compiling makes those matches with
    /// no timeout. Pairs already checked are remembered in <paramref name="checkedPatterns"/> and
    /// <paramref name="checkedLiterals"/>.
    /// </summary>
    /// <exception cref="SchemaException">A match ran past the timeout (XQST0059).</exception>
    public static void CheckSchemaLiterals(XmlSchemaSet set, TimeSpan timeout,
        HashSet<string> checkedPatterns, HashSet<string> checkedLiterals)
    {
        var newPatterns = new HashSet<string>(StringComparer.Ordinal);
        var newLiterals = new HashSet<string>(StringComparer.Ordinal);
        foreach (var obj in Walk(set))
        {
            switch (obj)
            {
                case XmlSchemaPatternFacet { Value: { } pattern }:
                    if (!checkedPatterns.Contains(pattern)) newPatterns.Add(pattern);
                    break;
                case XmlSchemaFacet { Value: { } value }:
                    AddLiteral(value);
                    break;
                case XmlSchemaElement element:
                    AddLiteral(element.DefaultValue);
                    AddLiteral(element.FixedValue);
                    break;
                case XmlSchemaAttribute attribute:
                    AddLiteral(attribute.DefaultValue);
                    AddLiteral(attribute.FixedValue);
                    break;
            }
        }

        foreach (var pattern in newPatterns)
            Check(pattern, checkedLiterals.Concat(newLiterals));
        foreach (var pattern in checkedPatterns)
            Check(pattern, newLiterals);
        checkedPatterns.UnionWith(newPatterns);
        checkedLiterals.UnionWith(newLiterals);

        void AddLiteral(string? value)
        {
            if (value is null)
                return;
            // A value is matched as its type's whitespace rule leaves it, and a list's items one
            // at a time; which rule applies is not known before compiling, so every form is tried.
            foreach (var form in LiteralForms(value))
                if (!checkedLiterals.Contains(form)) newLiterals.Add(form);
        }

        void Check(string pattern, IEnumerable<string> literals)
        {
            Regex regex;
            try
            {
                regex = new Regex(ToNetPattern(pattern), RegexOptions.None, timeout);
            }
            catch (ArgumentException)
            {
                return; // not a pattern System.Xml can build either; compiling reports it
            }
            foreach (var literal in literals)
            {
                try
                {
                    regex.IsMatch(literal);
                }
                catch (RegexMatchTimeoutException ex)
                {
                    throw new SchemaException("XQST0059",
                        $"The pattern facet '{Abbreviate(pattern)}' ran past the match time limit of " +
                        $"{timeout.TotalSeconds:0.###} s on a value the schema itself supplies. " +
                        "The pattern may backtrack catastrophically.", ex);
                }
            }
        }
    }

    private static IEnumerable<string> LiteralForms(string value)
    {
        yield return value;
        var replaced = value.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
        if (!ReferenceEquals(replaced, value) && replaced != value)
            yield return replaced;
        var tokens = replaced.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var collapsed = string.Join(' ', tokens);
        if (collapsed != replaced)
            yield return collapsed;
        if (tokens.Length > 1)
            foreach (var token in tokens)
                yield return token;
    }

    private static string Abbreviate(string pattern) =>
        pattern.Length <= 60 ? pattern : string.Concat(pattern.AsSpan(0, 57), "...");

    /// <summary>
    /// The .NET expression System.Xml builds for one pattern facet: anchored, grouped, with the
    /// XSD class escapes rewritten to the classes .NET keeps for them.
    /// </summary>
    internal static string ToNetPattern(string xsdPattern)
    {
        var source = "(" + xsdPattern + ")";
        if (source.Contains('|', StringComparison.Ordinal))
            source = "(" + source + ")";
        var result = new StringBuilder(source.Length + 8).Append('^');
        var copied = 0;
        for (var i = 0; i < source.Length - 2; i++)
        {
            if (source[i] != '\\')
                continue;
            if (source[i + 1] == '\\')
            {
                i++;
                continue;
            }
            var replacement = source[i + 1] switch
            {
                'c' => @"\p{_xmlC}",
                'C' => @"\P{_xmlC}",
                'd' => @"\p{_xmlD}",
                'D' => @"\P{_xmlD}",
                'i' => @"\p{_xmlI}",
                'I' => @"\P{_xmlI}",
                'w' => @"\p{_xmlW}",
                'W' => @"\P{_xmlW}",
                _ => null,
            };
            if (replacement is null)
                continue;
            result.Append(source, copied, i - copied).Append(replacement);
            i++;
            copied = i + 1;
        }
        return result.Append(source, copied, source.Length - copied).Append('$').ToString();
    }

    /// <summary>
    /// Every schema object reachable from the set: the schema documents with what they include,
    /// import and redefine, and — once compiled — the types, elements and attributes the
    /// compilation resolved, anonymous ones included.
    /// </summary>
    private static HashSet<object> Walk(XmlSchemaSet set)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<object>();
        foreach (XmlSchema schema in set.Schemas())
            pending.Push(schema);
        pending.Push(set.GlobalTypes);
        pending.Push(set.GlobalElements);
        pending.Push(set.GlobalAttributes);

        while (pending.TryPop(out var current))
        {
            switch (current)
            {
                case XmlSchemaObjectTable table:
                    foreach (var value in table.Values)
                        if (value is not null) pending.Push(value);
                    continue;
                case XmlSchemaObjectCollection collection:
                    foreach (var item in collection)
                        if (item is not null) pending.Push(item);
                    continue;
                case Array array:
                    foreach (var item in array)
                        if (item is not null) pending.Push(item);
                    continue;
            }
            if (current is not XmlSchemaObject || !seen.Add(current))
                continue;
            var properties = SchemaObjectProperties.GetOrAdd(current.GetType(), static type => type
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead && HoldsSchemaObjects(p.PropertyType))
                .ToArray());
            foreach (var property in properties)
            {
                object? value;
                try
                {
                    value = property.GetValue(current);
                }
                catch (TargetInvocationException)
                {
                    continue;
                }
                if (value is not null)
                    pending.Push(value);
            }
        }
        return seen;
    }

    private static bool HoldsSchemaObjects(Type type) =>
        typeof(XmlSchemaObject).IsAssignableFrom(type)
        || type == typeof(XmlSchemaObjectTable)
        || type == typeof(XmlSchemaObjectCollection)
        || (type.IsArray && typeof(XmlSchemaObject).IsAssignableFrom(type.GetElementType()));
}
