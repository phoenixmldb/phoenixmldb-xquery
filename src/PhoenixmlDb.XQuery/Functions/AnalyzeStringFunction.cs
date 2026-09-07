using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:analyze-string($input as xs:string?, $pattern as xs:string) as element(fn:analyze-string-result)</summary>
public sealed class AnalyzeStringFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "analyze-string");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.OptionalString },
         new() { Name = new QName(NamespaceId.None, "pattern"), Type = XdmSequenceType.String }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        return InvokeWithFlags(arguments[0]?.ToString() ?? "", arguments[1]?.ToString() ?? "", null, context);
    }

    /// <summary>
    /// Determines the parent group number for each capturing group in the regex pattern.
    /// Returns an array where parentGroup[g] is the parent capturing group of group g (1-based),
    /// or 0 if the group is at the top level.
    /// </summary>
    private static int[] BuildGroupParentMap(string pattern, Ast.ExecutionContext? context = null)
    {
        int groupCount = XQueryRegexHelper.CountCapturingGroups(pattern);
        var parentGroup = new int[groupCount + 1]; // 1-based indexing
        var groupStack = new Stack<int>(); // stack of capturing group numbers; 0 = non-capturing
        bool inCharClass = false;

        int currentGroup = 0;
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length) { i++; continue; }
            if (inCharClass) { if (c == ']') inCharClass = false; continue; }
            if (c == '[') { inCharClass = true; continue; }
            if (c == '(')
            {
                bool isNonCapturing = i + 1 < pattern.Length && pattern[i + 1] == '?';
                if (isNonCapturing)
                {
                    groupStack.Push(0); // sentinel
                }
                else
                {
                    currentGroup++;
                    // Find nearest capturing ancestor on the stack
                    int parent = 0;
                    foreach (var s in groupStack)
                    {
                        if (s > 0) { parent = s; break; }
                    }
                    parentGroup[currentGroup] = parent;
                    groupStack.Push(currentGroup);
                }
            }
            else if (c == ')' && groupStack.Count > 0)
            {
                groupStack.Pop();
            }
        }
        return parentGroup;
    }

    /// <summary>
    /// Emits the XML content for a match element, with properly nested groups.
    /// Groups are nested based on the regex structure (child groups inside parent groups).
    /// Non-participating groups (from alternation) are omitted entirely.
    /// </summary>
    private static void EmitMatchContent(
        System.Text.StringBuilder sb,
        System.Text.RegularExpressions.Match match,
        int[] parentGroup,
        int groupCount, Ast.ExecutionContext? context = null)
    {
        if (groupCount == 0)
        {
            sb.Append(System.Security.SecurityElement.Escape(match.Value));
            return;
        }

        // Build children lists: childGroups[p] = list of child group numbers of parent p
        // Parent 0 means "directly inside <fn:match>"
        var childGroups = new List<int>[groupCount + 1];
        for (int i = 0; i <= groupCount; i++)
            childGroups[i] = new List<int>();
        for (int g = 1; g <= groupCount; g++)
            childGroups[parentGroup[g]].Add(g);

        // Recursively emit group content for a given parent context
        EmitGroupChildren(sb, match, parentGroup, childGroups, 0, match.Index, match.Index + match.Length);
    }

    /// <summary>
    /// Emits group content within a parent span [spanStart, spanEnd) (absolute positions in input).
    /// parentGroupNr=0 means the fn:match level.
    /// </summary>
    private static void EmitGroupChildren(
        System.Text.StringBuilder sb,
        System.Text.RegularExpressions.Match match,
        int[] parentGroup,
        List<int>[] childGroups,
        int parentGroupNr,
        int spanStart,
        int spanEnd, Ast.ExecutionContext? context = null)
    {
        // Collect participating child groups, sorted by position
        var children = new List<(int groupNr, int start, int end)>();
        foreach (int g in childGroups[parentGroupNr])
        {
            var grp = match.Groups[g];
            if (!grp.Success) continue; // Non-participating groups omitted
            children.Add((g, grp.Index, grp.Index + grp.Length));
        }
        children.Sort((a, b) => a.start.CompareTo(b.start));

        int cursor = spanStart;
        foreach (var (groupNr, start, end) in children)
        {
            // Emit text before this group
            if (start > cursor)
                sb.Append(System.Security.SecurityElement.Escape(
                    match.Value[(cursor - match.Index)..(start - match.Index)]));

            sb.Append($"<fn:group nr=\"{groupNr}\">");
            // Recursively emit children of this group
            EmitGroupChildren(sb, match, parentGroup, childGroups, groupNr, start, end);
            sb.Append("</fn:group>");
            cursor = end;
        }
        // Emit trailing text after last child group
        if (cursor < spanEnd)
            sb.Append(System.Security.SecurityElement.Escape(
                match.Value[(cursor - match.Index)..(spanEnd - match.Index)]));
    }

    internal static ValueTask<object?> InvokeWithFlags(string input, string pattern, string? flags, Ast.ExecutionContext context)
    {
        // Validate and parse flags using shared helper
        var options = System.Text.RegularExpressions.RegexOptions.None;
        bool isLiteral = false;
        if (flags != null)
        {
            options = XQueryRegexHelper.ParseFlags(flags);
            isLiteral = flags.Contains('q');
        }

        // XPath 'x' flag: strip whitespace from pattern before any processing
        if (!isLiteral && flags?.Contains('x') == true)
            pattern = XQueryRegexHelper.StripXModeWhitespace(pattern);

        // Apply 'q' flag: escape pattern so it's treated as a literal string
        string effectivePattern = isLiteral
            ? System.Text.RegularExpressions.Regex.Escape(pattern)
            : pattern;

        // Apply XPath-to-.NET regex transformations (same as fn:matches, fn:replace, fn:tokenize)
        if (!isLiteral)
        {
            XQueryRegexHelper.ValidateXsdRegex(effectivePattern);
            effectivePattern = XQueryRegexHelper.ConvertXPathPatternToNet(effectivePattern);
            effectivePattern = XQueryRegexHelper.ConvertXsdEscapesToNet(effectivePattern);
        }
        bool isSingleLine = flags?.Contains('s') == true;
        effectivePattern = XQueryRegexHelper.FixDotForSurrogatePairs(effectivePattern, isSingleLine);

        var regex = new System.Text.RegularExpressions.Regex(effectivePattern, options);

        // FORX0003: pattern must not match zero-length string
        if (regex.IsMatch(""))
            throw context.Error("FORX0003",
                "The supplied regular expression matches a zero-length string");

        // Determine group nesting structure from the original pattern
        int groupCount = isLiteral ? 0 : XQueryRegexHelper.CountCapturingGroups(pattern);
        int[] parentGroup = groupCount > 0 ? BuildGroupParentMap(pattern) : Array.Empty<int>();

        // Build XML result per XQuery spec
        var sb = new System.Text.StringBuilder();
        sb.Append("<fn:analyze-string-result xmlns:fn=\"http://www.w3.org/2005/xpath-functions\">");

        int pos = 0;
        foreach (System.Text.RegularExpressions.Match match in regex.Matches(input))
        {
            if (match.Index > pos)
                sb.Append("<fn:non-match>").Append(System.Security.SecurityElement.Escape(input[pos..match.Index])).Append("</fn:non-match>");
            sb.Append("<fn:match>");
            EmitMatchContent(sb, match, parentGroup, groupCount);
            sb.Append("</fn:match>");
            pos = match.Index + match.Length;
        }
        if (pos < input.Length)
            sb.Append("<fn:non-match>").Append(System.Security.SecurityElement.Escape(input[pos..])).Append("</fn:non-match>");

        sb.Append("</fn:analyze-string-result>");

        // Parse the result and convert to proper XDM nodes for XPath navigation
        var xmlDoc = new System.Xml.XmlDocument();
        xmlDoc.LoadXml(sb.ToString());
        if (context.NodeStore is INodeBuilder builder)
        {
            var xdmDoc = ParseXmlFunction.ConvertToXdm(xmlDoc, builder, documentUri: null);
            xdmDoc.DocumentUri = null;
            xdmDoc.BaseUri = context.StaticBaseUri;
            // Return the document element (the analyze-string-result element)
            if (xdmDoc.DocumentElement is { } docElemId)
                return ValueTask.FromResult<object?>(builder.GetNode(docElemId));
            return ValueTask.FromResult<object?>(xdmDoc);
        }
        // Fallback
        return ValueTask.FromResult<object?>(xmlDoc.DocumentElement);
    }
}
