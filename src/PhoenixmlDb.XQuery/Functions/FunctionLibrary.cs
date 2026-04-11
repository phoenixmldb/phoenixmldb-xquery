using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// Registry of built-in and user-defined functions.
/// </summary>
public sealed class FunctionLibrary
{
    private readonly Dictionary<FunctionKey, XQueryFunction> _functions = new();
    private readonly Dictionary<string, NamespaceId> _dynamicUriToNamespace = new();

    private record FunctionKey(NamespaceId Namespace, string LocalName, int Arity);

    /// <summary>
    /// The standard XQuery function library.
    /// </summary>
    public static FunctionLibrary Standard { get; } = CreateStandardLibrary();

    /// <summary>
    /// Creates a per-compilation copy so user-defined registrations don't leak across queries.
    /// </summary>
    public FunctionLibrary Copy()
    {
        var copy = new FunctionLibrary();
        foreach (var kvp in _functions)
            copy._functions[kvp.Key] = kvp.Value;
        foreach (var kvp in _dynamicUriToNamespace)
            copy._dynamicUriToNamespace[kvp.Key] = kvp.Value;
        foreach (var kvp in _prefixToNamespace)
            copy._prefixToNamespace[kvp.Key] = kvp.Value;
        return copy;
    }

    /// <summary>
    /// Registers a function.
    /// </summary>
    public void Register(XQueryFunction function)
    {
        var key = new FunctionKey(function.Name.Namespace, function.Name.LocalName, function.Arity);
        _functions[key] = function;
    }

    /// <summary>
    /// Registers a prefix-to-namespace mapping for resolving prefixed function calls
    /// where the namespace hasn't been resolved at parse time.
    /// </summary>
    public void RegisterPrefix(string prefix, NamespaceId ns)
    {
        _prefixToNamespace[prefix] = ns;
    }

    /// <summary>
    /// Registers a namespace URI to NamespaceId mapping for resolving EQName function calls
    /// where the URI is not a well-known standard namespace (e.g., user-defined function namespaces).
    /// </summary>
    public void RegisterNamespaceUri(string uri, NamespaceId ns)
    {
        _dynamicUriToNamespace[uri] = ns;
    }

    // Well-known prefix → NamespaceId mapping for resolving prefixed function calls
    // where the namespace hasn't been resolved at parse time (XQuery parser uses default NamespaceId).
    private readonly Dictionary<string, NamespaceId> _prefixToNamespace = new()
    {
        ["fn"] = FunctionNamespaces.Fn,
        ["xs"] = FunctionNamespaces.Xs,
        ["math"] = FunctionNamespaces.Math,
        ["map"] = FunctionNamespaces.Map,
        ["array"] = FunctionNamespaces.Array,
        ["local"] = FunctionNamespaces.Local,
    };

    // URI → NamespaceId mapping for resolving EQName function calls (Q{uri}local syntax)
    private static readonly Dictionary<string, NamespaceId> _uriToNamespace = new()
    {
        ["http://www.w3.org/2005/xpath-functions"] = FunctionNamespaces.Fn,
        ["http://www.w3.org/2001/XMLSchema"] = FunctionNamespaces.Xs,
        ["http://www.w3.org/2005/xpath-functions/math"] = FunctionNamespaces.Math,
        ["http://www.w3.org/2005/xpath-functions/map"] = FunctionNamespaces.Map,
        ["http://www.w3.org/2005/xpath-functions/array"] = FunctionNamespaces.Array,
        ["http://www.w3.org/2005/xquery-local-functions"] = FunctionNamespaces.Local,
    };

    /// <summary>
    /// Resolves a function by name and arity.
    /// </summary>
    public XQueryFunction? Resolve(QName name, int arity)
    {
        var ns = name.Namespace;
        var result = ResolveExact(ns, name.LocalName, arity);

        if (result == null)
        {
            // Try URI-based resolution via ResolvedNamespace (covers EQName Q{uri}name syntax
            // and runtime QNames from fn:QName() / xs:QName() which carry RuntimeNamespace)
            var resolvedUri = name.ResolvedNamespace;
            if (resolvedUri != null)
            {
                if (_uriToNamespace.TryGetValue(resolvedUri, out var uriNs))
                {
                    result = ResolveExact(uriNs, name.LocalName, arity);
                }

                // For user-defined functions with custom namespace URIs, try
                // dynamic URI registrations (registered at XSLT compile time)
                if (result == null && _dynamicUriToNamespace.TryGetValue(resolvedUri, out var dynNs))
                {
                    result = ResolveExact(dynNs, name.LocalName, arity);
                }
            }

            if (result == null && ns == default)
            {
                // Try well-known prefix mapping (the XQuery parser doesn't resolve prefixes at parse time)
                if (name.Prefix != null && _prefixToNamespace.TryGetValue(name.Prefix, out var prefixNs))
                {
                    result = ResolveExact(prefixNs, name.LocalName, arity);
                }

                // Per XQuery spec, unprefixed function names default to the fn: namespace
                if (result == null && name.Prefix == null && resolvedUri == null)
                {
                    result = ResolveExact(FunctionNamespaces.Fn, name.LocalName, arity);
                }
            }
        }

        return result;
    }

    private XQueryFunction? ResolveExact(NamespaceId ns, string localName, int arity)
    {
        var key = new FunctionKey(ns, localName, arity);
        var result = _functions.GetValueOrDefault(key);

        // If exact arity not found, check for variadic functions with fewer declared parameters
        if (result == null)
        {
            foreach (var (k, func) in _functions)
            {
                if (k.Namespace == ns && k.LocalName == localName &&
                    func.IsVariadic && arity >= func.Arity && arity <= func.MaxArity)
                {
                    return func;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Gets all registered functions.
    /// </summary>
    public IEnumerable<XQueryFunction> GetAllFunctions() => _functions.Values;

    /// <summary>
    /// Creates the standard XQuery function library.
    /// </summary>
    private static FunctionLibrary CreateStandardLibrary()
    {
        var lib = new FunctionLibrary();

        // String functions
        lib.Register(new StringLengthFunction());
        lib.Register(new StringLength0Function());
        lib.Register(new SubstringFunction());
        lib.Register(new Substring3Function());
        lib.Register(new ConcatFunction());
        lib.Register(new StringJoinFunction());
        lib.Register(new StringJoin1Function());
        lib.Register(new ContainsFunction());
        lib.Register(new StartsWithFunction());
        lib.Register(new EndsWithFunction());
        lib.Register(new NormalizeSpaceFunction());
        lib.Register(new NormalizeSpace0Function());
        lib.Register(new UpperCaseFunction());
        lib.Register(new LowerCaseFunction());
        lib.Register(new TranslateFunction());
        lib.Register(new StringFunction());
        lib.Register(new String0Function());
        lib.Register(new TokenizeFunction());
        lib.Register(new Tokenize3Function());
        lib.Register(new Tokenize1Function());
        lib.Register(new MatchesFunction());
        lib.Register(new Matches3Function());
        lib.Register(new ReplaceFunction());
        lib.Register(new Replace4Function());
        lib.Register(new SubstringBeforeFunction());
        lib.Register(new SubstringAfterFunction());
        lib.Register(new CompareFunction());
        lib.Register(new StringToCodepointsFunction());
        lib.Register(new CodepointsToStringFunction());
        lib.Register(new CodepointEqualFunction());
        lib.Register(new EncodeForUriFunction());
        lib.Register(new EscapeHtmlUriFunction());
        lib.Register(new IriToUriFunction());
        lib.Register(new NormalizeUnicodeFunction());
        lib.Register(new NormalizeUnicode2Function());

        // Numeric functions
        lib.Register(new AbsFunction());
        lib.Register(new CeilingFunction());
        lib.Register(new FloorFunction());
        lib.Register(new RoundFunction());
        lib.Register(new Round2Function());
        lib.Register(new NumberFunction());
        lib.Register(new Number0Function());
        lib.Register(new RoundHalfToEvenFunction());
        lib.Register(new RoundHalfToEven2Function());

        // Aggregate functions
        lib.Register(new SumFunction());
        lib.Register(new AvgFunction());
        lib.Register(new MinFunction());
        lib.Register(new MaxFunction());
        lib.Register(new CountFunction());

        // Boolean functions
        lib.Register(new NotFunction());
        lib.Register(new TrueFunction());
        lib.Register(new FalseFunction());
        lib.Register(new BooleanFunction());

        // Sequence functions
        lib.Register(new EmptyFunction());
        lib.Register(new ExistsFunction());
        lib.Register(new HeadFunction());
        lib.Register(new TailFunction());
        lib.Register(new ReverseFunction());
        lib.Register(new DistinctValuesFunction());
        lib.Register(new SubsequenceFunction());
        lib.Register(new Subsequence3Function());
        lib.Register(new InsertBeforeFunction());
        lib.Register(new RemoveFunction());
        lib.Register(new IndexOfFunction());
        lib.Register(new DeepEqualFunction());
        lib.Register(new ZeroOrOneFunction());
        lib.Register(new OneOrMoreFunction());
        lib.Register(new ExactlyOneFunction());
        lib.Register(new UnorderedFunction());

        // Node functions
        lib.Register(new NameFunction());
        lib.Register(new Name0Function());
        lib.Register(new LocalNameFunction());
        lib.Register(new LocalName0Function());
        lib.Register(new NamespaceUriFunction());
        lib.Register(new NamespaceUri0Function());
        lib.Register(new RootFunction());
        lib.Register(new Root0Function());
        lib.Register(new BaseUriFunction());
        lib.Register(new BaseUri0Function());
        lib.Register(new StaticBaseUriFunction());
        lib.Register(new DocumentUriFunction());
        lib.Register(new NodeNameFunction());
        lib.Register(new NodeName0Function());
        lib.Register(new NamespaceUriForPrefixFunction());
        lib.Register(new NamespaceUriFromQNameFunction());
        lib.Register(new LocalNameFromQNameFunction());
        lib.Register(new PrefixFromQNameFunction());
        lib.Register(new ResolveQNameFunction());
        lib.Register(new InScopePrefixesFunction());
        lib.Register(new HasChildrenFunction());
        lib.Register(new HasChildren0Function());
        lib.Register(new NilledFunction());
        lib.Register(new Nilled0Function());
        lib.Register(new GenerateIdFunction());
        lib.Register(new GenerateId0Function());
        lib.Register(new LangFunction());
        lib.Register(new Lang1Function());
        lib.Register(new OutermostFunction());
        lib.Register(new InnermostFunction());
        lib.Register(new DocumentUri0Function());
        lib.Register(new IdrefFunction());
        lib.Register(new Idref1Function());
        lib.Register(new ElementWithId2Function());
        lib.Register(new ElementWithId1Function());

        // Text/file functions
        lib.Register(new UnparsedTextFunction());
        lib.Register(new UnparsedText2Function());
        lib.Register(new UnparsedTextAvailableFunction());
        lib.Register(new UnparsedTextAvailable2Function());
        lib.Register(new UnparsedTextLinesFunction());
        lib.Register(new UnparsedTextLines2Function());
        lib.Register(new AnalyzeStringFunction());
        lib.Register(new AnalyzeString3Function());
        lib.Register(new ParseIetfDateFunction());
        // CollationKeyFunction already registered in SequenceFunctions section

        // Context functions
        lib.Register(new PositionFunction());
        lib.Register(new LastFunction());
        lib.Register(new CurrentDateTimeFunction());
        lib.Register(new CurrentDateFunction());
        lib.Register(new CurrentTimeFunction());

        // Document functions
        lib.Register(new DocFunction());
        lib.Register(new DocAvailableFunction());
        lib.Register(new CollectionFunction());
        lib.Register(new Collection0Function());
        lib.Register(new UriCollectionFunction());
        lib.Register(new UriCollection0Function());

        // JSON functions (XPath 3.1)
        lib.Register(new ParseJsonFunction());
        lib.Register(new ParseJson2Function());
        lib.Register(new JsonDocFunction());
        lib.Register(new JsonDoc2Function());
        lib.Register(new LoadXQueryModuleFunction());
        lib.Register(new LoadXQueryModule2Function());

        // Data functions
        lib.Register(new DataFunction());
        lib.Register(new Data0Function());

        // Error function
        lib.Register(new ErrorFunction());
        lib.Register(new Error1Function());
        lib.Register(new Error0Function());
        lib.Register(new Error3Function());

        // Trace function
        lib.Register(new TraceFunction());

        // Map functions (XQuery 3.1 + 4.0)
        lib.Register(new MapBuildFunction());
        lib.Register(new MapEmptyFunction());
        lib.Register(new MapEntriesFunction());
        lib.Register(new MapFilterFunction());
        lib.Register(new MapKeysWhereFunction());
        lib.Register(new MapItemsFunction());
        lib.Register(new MapPairFunction());
        lib.Register(new MapOfPairsFunction());
        lib.Register(new MapReplaceFunction());
        lib.Register(new MapGroupByFunction());
        lib.Register(new MapMergeFunction());
        lib.Register(new MapMerge2Function());
        lib.Register(new MapSizeFunction());
        lib.Register(new MapKeysFunction());
        lib.Register(new MapContainsFunction());
        lib.Register(new MapGetFunction());
        lib.Register(new MapPutFunction());
        lib.Register(new MapRemoveFunction());
        lib.Register(new MapEntryFunction());
        lib.Register(new MapForEachFunction());
        lib.Register(new MapFindFunction());

        // Higher-order functions (XQuery 3.1)
        lib.Register(new ForEachFunction());
        lib.Register(new FilterFunction());
        lib.Register(new FoldLeftFunction());
        lib.Register(new FoldRightFunction());
        lib.Register(new ForEachPairFunction());
        lib.Register(new SortFunction());
        lib.Register(new Sort2Function());
        lib.Register(new Sort3Function());
        lib.Register(new ApplyFunction());

        // Math functions (math: namespace, XQuery 3.1 + 4.0)
        lib.Register(new MathEFunction());
        lib.Register(new MathPiFunction());
        lib.Register(new MathExpFunction());
        lib.Register(new MathExp10Function());
        lib.Register(new MathLogFunction());
        lib.Register(new MathLog10Function());
        lib.Register(new MathPowFunction());
        lib.Register(new MathSqrtFunction());
        lib.Register(new MathSinFunction());
        lib.Register(new MathCosFunction());
        lib.Register(new MathTanFunction());
        lib.Register(new MathAsinFunction());
        lib.Register(new MathAcosFunction());
        lib.Register(new MathAtanFunction());
        lib.Register(new MathAtan2Function());

        // Random number generator (XPath 3.1)
        lib.Register(new RandomNumberGeneratorFunction());
        lib.Register(new RandomNumberGenerator0Function());

        // XPath/XQuery 4.0 sequence functions
        lib.Register(new IdentityFunction());
        lib.Register(new ReplicateFunction());
        lib.Register(new FootFunction());
        lib.Register(new TrunkFunction());
        lib.Register(new VoidFunction());
        lib.Register(new IsNaNFunction());
        lib.Register(new CharactersFunction());
        lib.Register(new ItemsAtFunction());
        lib.Register(new SliceFunction());
        lib.Register(new AllEqualFunction());
        lib.Register(new AllDifferentFunction());
        lib.Register(new IndexWhereFunction());
        lib.Register(new ScanLeftFunction());
        lib.Register(new ScanRightFunction());
        lib.Register(new DuplicateValuesFunction());
        lib.Register(new AtomicEqualFunction());
        lib.Register(new ContainsSubsequenceFunction());
        lib.Register(new StartsWithSubsequenceFunction());
        lib.Register(new EndsWithSubsequenceFunction());
        lib.Register(new InsertSeparatorFunction());
        lib.Register(new HighestFunction());
        lib.Register(new LowestFunction());
        lib.Register(new SortWithFunction());
        lib.Register(new TransitiveClosureFunction());
        lib.Register(new PartitionFunction());
        lib.Register(new IterateWhileFunction());
        lib.Register(new UniformFunction());
        lib.Register(new DivideDecimalsFunction());
        lib.Register(new DefaultLanguageFunction());
        lib.Register(new PinFunction());
        lib.Register(new CollationKey1Function());
        lib.Register(new CollationKeyFunction());
        lib.Register(new ParseUriFunction());
        lib.Register(new BuildUriFunction());
        lib.Register(new ContainsTokenFunction());
        lib.Register(new ContainsToken3Function());
        lib.Register(new CharFunction());
        lib.Register(new CodepointFunction());
        lib.Register(new InScopeNamespacesFunction());
        lib.Register(new IntersperseFunction());
        lib.Register(new DistinctOrderedFunction());
        lib.Register(new SortByFunction());
        lib.Register(new ParseHtmlFunction());
        lib.Register(new TypeFunction());
        lib.Register(new GraphemesFunction());
        lib.Register(new SomeFunction());
        lib.Register(new EveryFunction());

        // Function introspection
        lib.Register(new FunctionLookupFunction());
        lib.Register(new FunctionNameFunction());
        lib.Register(new FunctionArityFunction());

        // Database metadata functions (dbxml: namespace)
        lib.Register(new MetadataGetFunction());
        lib.Register(new MetadataAllFunction());

        // Array functions (XQuery 3.1)
        lib.Register(new ArraySizeFunction());
        lib.Register(new ArrayGetFunction());
        lib.Register(new ArrayPutFunction());
        lib.Register(new ArrayAppendFunction());
        lib.Register(new ArraySubarrayFunction());
        lib.Register(new ArraySubarray3Function());
        lib.Register(new ArrayRemoveFunction());
        lib.Register(new ArrayInsertBeforeFunction());
        lib.Register(new ArrayHeadFunction());
        lib.Register(new ArrayTailFunction());
        lib.Register(new ArrayReverseFunction());
        lib.Register(new ArrayJoinFunction());
        lib.Register(new ArrayForEachFunction());
        lib.Register(new ArrayFilterFunction());
        lib.Register(new ArrayFoldLeftFunction());
        lib.Register(new ArrayFoldRightFunction());
        lib.Register(new ArrayForEachPairFunction());
        lib.Register(new ArraySortFunction());
        lib.Register(new ArraySort2Function());
        lib.Register(new ArraySort3Function());
        lib.Register(new ArrayFlattenFunction());

        // Array functions (XPath 4.0)
        lib.Register(new ArrayBuildFunction());
        lib.Register(new ArrayEmptyFunction());
        lib.Register(new ArrayFootFunction());
        lib.Register(new ArrayTrunkFunction());
        lib.Register(new ArrayIndexOfFunction());
        lib.Register(new ArrayIndexWhereFunction());
        lib.Register(new ArraySliceFunction());
        lib.Register(new ArraySortWithFunction());
        lib.Register(new ArraySplitFunction());
        lib.Register(new ArrayItemsFunction());
        lib.Register(new ArrayMembersFunction());
        lib.Register(new ArrayOfMembersFunction());
        lib.Register(new ArraySortByFunction());

        // Additional overloads
        lib.Register(new Sum2Function());
        lib.Register(new Contains3Function());
        lib.Register(new Compare3Function());
        lib.Register(new StartsWith3Function());
        lib.Register(new EndsWith3Function());
        lib.Register(new SubstringBefore3Function());
        lib.Register(new SubstringAfter3Function());
        lib.Register(new DefaultCollationFunction());
        lib.Register(new DeepEqual3Function());
        lib.Register(new IndexOf3Function());
        lib.Register(new DistinctValues2Function());
        lib.Register(new Min2Function());
        lib.Register(new Max2Function());

        // Date/time accessor functions
        lib.Register(new YearFromDateFunction());
        lib.Register(new MonthFromDateFunction());
        lib.Register(new DayFromDateFunction());
        lib.Register(new YearFromDateTimeFunction());
        lib.Register(new MonthFromDateTimeFunction());
        lib.Register(new DayFromDateTimeFunction());
        lib.Register(new HoursFromDateTimeFunction());
        lib.Register(new MinutesFromDateTimeFunction());
        lib.Register(new SecondsFromDateTimeFunction());
        lib.Register(new HoursFromTimeFunction());
        lib.Register(new MinutesFromTimeFunction());
        lib.Register(new SecondsFromTimeFunction());
        lib.Register(new YearsFromDurationFunction());
        lib.Register(new MonthsFromDurationFunction());
        lib.Register(new DaysFromDurationFunction());
        lib.Register(new HoursFromDurationFunction());
        lib.Register(new MinutesFromDurationFunction());
        lib.Register(new SecondsFromDurationFunction());
        lib.Register(new TimezoneFromDateTimeFunction());
        lib.Register(new TimezoneFromDateFunction());
        lib.Register(new TimezoneFromTimeFunction());
        lib.Register(new ImplicitTimezoneFunction());
        lib.Register(new AdjustDateTimeToTimezoneFunction());
        lib.Register(new AdjustDateTimeToTimezone2Function());
        lib.Register(new AdjustDateToTimezoneFunction());
        lib.Register(new AdjustDateToTimezone2Function());
        lib.Register(new AdjustTimeToTimezoneFunction());
        lib.Register(new AdjustTimeToTimezone2Function());
        lib.Register(new DateTimeCombineFunction());

        // XSD type constructor functions (xs: namespace)
        lib.Register(new IntegerConstructorFunction());
        lib.Register(new DecimalConstructorFunction());
        lib.Register(new DoubleConstructorFunction());
        lib.Register(new FloatConstructorFunction());
        lib.Register(new IntConstructorFunction());
        lib.Register(new LongConstructorFunction());
        lib.Register(new ShortConstructorFunction());
        lib.Register(new ByteConstructorFunction());
        lib.Register(new UnsignedLongConstructorFunction());
        lib.Register(new UnsignedIntConstructorFunction());
        lib.Register(new UnsignedShortConstructorFunction());
        lib.Register(new UnsignedByteConstructorFunction());
        lib.Register(new PositiveIntegerConstructorFunction());
        lib.Register(new NonNegativeIntegerConstructorFunction());
        lib.Register(new NegativeIntegerConstructorFunction());
        lib.Register(new NonPositiveIntegerConstructorFunction());
        lib.Register(new StringConstructorFunction());
        lib.Register(new BooleanConstructorFunction());
        lib.Register(new AnyUriConstructorFunction());
        lib.Register(new UntypedAtomicConstructorFunction());
        lib.Register(new NormalizedStringConstructorFunction());
        lib.Register(new TokenConstructorFunction());
        lib.Register(new LanguageConstructorFunction());
        lib.Register(new NameConstructorFunction());
        lib.Register(new NCNameConstructorFunction());
        lib.Register(new NMTokenConstructorFunction());
        lib.Register(new NMTokensConstructorFunction());
        lib.Register(new EntityConstructorFunction());
        lib.Register(new EntitiesConstructorFunction());
        lib.Register(new IDRefsConstructorFunction());
        lib.Register(new DateConstructorFunction());
        lib.Register(new TimeConstructorFunction());
        lib.Register(new DateTimeConstructorFunction());
        lib.Register(new DurationConstructorFunction());
        lib.Register(new DayTimeDurationConstructorFunction());
        lib.Register(new YearMonthDurationConstructorFunction());
        lib.Register(new GYearConstructorFunction());
        lib.Register(new GYearMonthConstructorFunction());
        lib.Register(new GMonthConstructorFunction());
        lib.Register(new GMonthDayConstructorFunction());
        lib.Register(new GDayConstructorFunction());
        lib.Register(new HexBinaryConstructorFunction());
        lib.Register(new Base64BinaryConstructorFunction());
        lib.Register(new QNameConstructorFunction());

        // Formatting functions
        lib.Register(new FormatIntegerFunction());
        lib.Register(new FormatIntegerFunction3());
        lib.Register(new FormatNumberFunction());
        lib.Register(new FormatNumber3Function());
        lib.Register(new FormatDateFunction());
        lib.Register(new FormatDate5Function());
        lib.Register(new FormatDateTimeFunction());
        lib.Register(new FormatDateTime5Function());
        lib.Register(new FormatTimeFunction());
        lib.Register(new FormatTime5Function());
        lib.Register(new SerializeFunction());
        lib.Register(new Serialize2Function());
        lib.Register(new ResolveUriFunction());
        lib.Register(new ResolveUri1Function());
        lib.Register(new QNameFunction());
        lib.Register(new EnvironmentVariableFunction());
        lib.Register(new AvailableEnvironmentVariablesFunction());

        // XML node functions (XPath 3.1 — shared with XSLT)
        lib.Register(new PathFunction());
        lib.Register(new Path0Function());
        lib.Register(new IdFunction());
        lib.Register(new Id2Function());

        // XML conversion functions (XPath 3.1 — shared with XSLT)
        lib.Register(new ParseXmlFunction());
        lib.Register(new ParseXmlFragmentFunction());
        lib.Register(new XmlToJsonFunction());
        lib.Register(new XmlToJson2Function());
        lib.Register(new JsonToXmlFunction());
        lib.Register(new JsonToXml2Function());

        // fn:transform (XPath 3.1 — delegates to ITransformProvider)
        lib.Register(new TransformFunction());

        // Full-text functions (ft: namespace)
        lib.Register(new FullText.FtScoreFunction());
        lib.Register(new FullText.FtTokenizeFunction());
        lib.Register(new FullText.FtTokenize2Function());
        lib.Register(new FullText.FtStemFunction());
        lib.Register(new FullText.FtStem2Function());
        lib.Register(new FullText.FtIsStopWordFunction());
        lib.Register(new FullText.FtThesaurusLookupFunction());

        return lib;
    }
}

/// <summary>
/// Well-known namespace IDs for functions.
/// </summary>
public static class FunctionNamespaces
{
    public static readonly NamespaceId Fn = new(5);    // http://www.w3.org/2005/xpath-functions
    public static readonly NamespaceId Xs = new(2);    // http://www.w3.org/2001/XMLSchema
    public static readonly NamespaceId Math = new(8);  // http://www.w3.org/2005/xpath-functions/math
    public static readonly NamespaceId Map = new(6);   // http://www.w3.org/2005/xpath-functions/map
    public static readonly NamespaceId Array = new(7); // http://www.w3.org/2005/xpath-functions/array
    public static readonly NamespaceId Local = new(4); // http://www.w3.org/2005/xquery-local-functions
    public static readonly NamespaceId Dbxml = new(9); // http://phoenixml.endpointsystems.com/dbxml
    public static readonly NamespaceId Ft = new(10);   // http://www.w3.org/2007/xpath-full-text

    /// <summary>Resolves a well-known function NamespaceId to its namespace string.</summary>
    public static string? ResolveNamespace(NamespaceId ns)
    {
        if (ns == Fn) return "http://www.w3.org/2005/xpath-functions";
        if (ns == Xs) return "http://www.w3.org/2001/XMLSchema";
        if (ns == Math) return "http://www.w3.org/2005/xpath-functions/math";
        if (ns == Map) return "http://www.w3.org/2005/xpath-functions/map";
        if (ns == Array) return "http://www.w3.org/2005/xpath-functions/array";
        if (ns == Local) return "http://www.w3.org/2005/xquery-local-functions";
        if (ns == Dbxml) return "http://phoenixml.endpointsystems.com/dbxml";
        if (ns == Ft) return "http://www.w3.org/2007/xpath-full-text";
        return null;
    }
}
