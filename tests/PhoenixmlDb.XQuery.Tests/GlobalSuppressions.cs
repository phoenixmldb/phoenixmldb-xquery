// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.

using System.Diagnostics.CodeAnalysis;

// Test naming convention uses underscores
[assembly: SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores",
    Justification = "Test naming convention")]

// Test internal types don't need to be sealed
[assembly: SuppressMessage("Performance", "CA1852:Seal internal types",
    Justification = "Test helper classes")]

// Test code can use interface return types
[assembly: SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance",
    Justification = "Test helper methods")]

// Suppress other common test warnings
[assembly: SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Test methods")]
[assembly: SuppressMessage("Design", "CA1062:Validate arguments of public methods",
    Justification = "Test code")]
[assembly: SuppressMessage("Globalization", "CA1305:Specify IFormatProvider",
    Justification = "Test code")]
[assembly: SuppressMessage("Globalization", "CA1307:Specify StringComparison",
    Justification = "Test code")]
[assembly: SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments",
    Justification = "Test data")]
[assembly: SuppressMessage("Reliability", "CA2007:Consider calling ConfigureAwait on the awaited task",
    Justification = "Test code")]
[assembly: SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
    Justification = "Test code - disposables are managed by test framework")]
