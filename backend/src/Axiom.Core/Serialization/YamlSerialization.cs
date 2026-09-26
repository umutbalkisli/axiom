using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Axiom.Serialization;

/// <summary>Single place that defines how collections and test cases are read from and written to YAML.</summary>
public static class YamlSerialization
{
    public static IDeserializer Deserializer { get; } = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static ISerializer Serializer { get; } = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();
}
