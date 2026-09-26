using System.Collections;
using System.Text.Json.Nodes;

namespace Axiom.Runtime;

public static class BuiltInAssertionAggregations
{
    public static IReadOnlyList<IAssertionAggregation> All { get; } =
    [
        new Aggregation("count", Count),
        new Aggregation("sum", value => Numbers("sum", value).Sum()),
        new Aggregation("avg", value => NonEmpty("avg", Numbers("avg", value)).Average()),
        new Aggregation("min", value => NonEmpty("min", Numbers("min", value)).Min()),
        new Aggregation("max", value => NonEmpty("max", Numbers("max", value)).Max()),
    ];

    /// <summary>Items in a list or rows, or properties in an object.</summary>
    private static object Count(object? value) => value switch
    {
        null => throw Missing("count"),
        JsonArray array => array.Count,
        JsonObject obj => obj.Count,
        string or JsonValue => throw NotAList("count"),
        ICollection collection => collection.Count,
        IEnumerable sequence => sequence.Cast<object?>().Count(),
        _ => throw NotAList("count"),
    };

    private static List<decimal> Numbers(string name, object? value)
    {
        var items = value switch
        {
            null => throw Missing(name),
            JsonArray array => array.Select(TemplateResolver.UnwrapJson),
            string or JsonValue or JsonObject or IDictionary => throw NotAList(name),
            IEnumerable sequence => sequence.Cast<object?>(),
            _ => throw NotAList(name),
        };

        var numbers = new List<decimal>();
        foreach (var item in items)
        {
            if (!ValueComparison.TryToDecimal(item, out var number))
            {
                throw new InvalidOperationException($"Cannot {name}: '{item}' is not a number.");
            }

            numbers.Add(number);
        }

        return numbers;
    }

    private static List<decimal> NonEmpty(string name, List<decimal> numbers) =>
        numbers.Count > 0 ? numbers : throw new InvalidOperationException($"Cannot {name} an empty list.");

    private static InvalidOperationException Missing(string name) => new($"Cannot {name}: the value is missing.");

    private static InvalidOperationException NotAList(string name) => new($"Cannot {name}: the value is not a list.");

    private sealed class Aggregation(string name, Func<object?, object> apply) : IAssertionAggregation
    {
        public string Name { get; } = name;

        public object Apply(object? value) => apply(value);
    }
}
