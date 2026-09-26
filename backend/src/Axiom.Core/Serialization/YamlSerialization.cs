using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Axiom.Serialization;

/// <summary>
/// Single place that defines how collections and test cases are read from and written to YAML.
/// </summary>
public static class YamlSerialization
{
    /// <summary>
    /// The lenient deserializer used by the desktop editor: keys it does not know are skipped, so a file with a
    /// typo can still be opened and fixed.
    /// </summary>
    public static IDeserializer Deserializer { get; } = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// The deserializer used to load a collection for a run. A key it does not know is an error: a misspelled
    /// <c>asert:</c> must not turn into a test that passes because it checks nothing.
    /// </summary>
    public static IDeserializer StrictDeserializer { get; } = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .WithDuplicateKeyChecking()
        .Build();

    /// <summary>
    /// The serializer used to write collection, test and shared-steps files.
    /// </summary>
    public static ISerializer Serializer { get; } = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    /// <summary>
    /// The YAML key of a model property, e.g. <c>SaveAs</c> becomes <c>save_as</c>.
    /// </summary>
    public static string KeyOf(string propertyName) => UnderscoredNamingConvention.Instance.Apply(propertyName);
}
