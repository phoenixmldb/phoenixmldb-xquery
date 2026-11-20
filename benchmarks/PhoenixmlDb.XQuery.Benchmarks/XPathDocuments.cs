using System.Text;

namespace PhoenixmlDb.XQuery.Benchmarks;

/// <summary>Deterministic input documents for <see cref="XPathPerNodeBenchmarks"/>.</summary>
internal static class XPathDocuments
{
    public const string Namespace = "urn:bench";

    /// <summary>About 5 MB, about 50k elements: fan-out 10 to 30 near the root, depth about 8, 2 to 4 attributes.</summary>
    public static string Large(bool namespaced)
    {
        var random = new Random(7);
        var sb = new StringBuilder(5_400_000);
        var id = 0;
        sb.Append(namespaced ? $"<b:doc xmlns:b=\"{Namespace}\">" : "<doc>");
        while (sb.Length < 5_000_000)
            Element(sb, random, namespaced, 1, ref id);
        sb.Append(namespaced ? "</b:doc>" : "</doc>");
        return sb.ToString();
    }

    private static void Element(StringBuilder sb, Random random, bool namespaced, int depth, ref int id)
    {
        var name = (namespaced ? "b:" : "") + "e" + random.Next(20).ToString(System.Globalization.CultureInfo.InvariantCulture);
        sb.Append('<').Append(name).Append(" id=\"i").Append(id++).Append("\" a=\"v").Append(random.Next(1000)).Append('"');
        if (random.Next(2) == 0) sb.Append(" ref=\"i").Append(random.Next(Math.Max(1, id))).Append('"');
        if (random.Next(3) == 0) sb.Append(" n=\"").Append(random.Next(100)).Append('"');
        sb.Append('>');
        if (depth < 8)
        {
            var children = depth < 3 ? 10 + random.Next(21) : random.Next(4);
            for (var c = 0; c < children; c++)
                Element(sb, random, namespaced, depth + 1, ref id);
        }
        else
        {
            sb.Append("text ").Append(random.Next(100000));
        }
        sb.Append("</").Append(name).Append('>');
    }

    /// <summary>One element with 5,000 children.</summary>
    public static string Wide()
    {
        var sb = new StringBuilder();
        sb.Append("<doc>");
        for (var i = 0; i < 5000; i++)
            sb.Append("<e").Append(i % 20).Append(" id=\"i").Append(i).Append("\" a=\"v").Append(i % 1000).Append("\">t</e").Append(i % 20).Append('>');
        sb.Append("</doc>");
        return sb.ToString();
    }

    /// <summary>A chain of 200 nested elements.</summary>
    public static string Deep()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 200; i++)
            sb.Append("<e").Append(i % 20).Append(" id=\"i").Append(i).Append("\" a=\"v").Append(i).Append("\">");
        sb.Append("leaf");
        for (var i = 199; i >= 0; i--)
            sb.Append("</e").Append(i % 20).Append('>');
        return sb.ToString();
    }
}
