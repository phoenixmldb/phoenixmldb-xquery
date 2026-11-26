namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// The updating function assertion of a function test (XQUF 3.0 §2.3.2): which functions the test
/// matches. A function test without one matches only simple functions, as in XQuery 3.0 without the
/// Update Facility. Maps and arrays are simple functions.
/// </summary>
internal enum UpdatingAssertion
{
    /// <summary><c>%simple</c> or <c>%updating("prohibited")</c>, and a function test with neither: only simple functions.</summary>
    Prohibited = 0,

    /// <summary><c>%updating</c> or <c>%updating("allowed")</c>: simple functions and updating functions.</summary>
    Allowed,

    /// <summary><c>%updating("required")</c>: only updating functions.</summary>
    Required,
}
