using Axiom.Parsing;
using Axiom.Validation;

namespace Axiom.Tests.Parsing;

public class VariableNameTests
{
    [Theory]
    [InlineData("base_url")]
    [InlineData("todo_id")]
    [InlineData("_private")]
    [InlineData("A")]
    public void Letters_and_underscores_are_valid(string name) => Assert.Null(VariableName.Check(name));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Empty_names_are_invalid(string? name) => Assert.Contains("empty", VariableName.Check(name));

    [Theory]
    [InlineData("token2")]
    [InlineData("my-var")]
    [InlineData("a.b")]
    [InlineData("has space")]
    [InlineData("çay")]
    public void Anything_else_is_invalid(string name) => Assert.Contains("invalid", VariableName.Check(name));

    [Theory]
    [InlineData("secret")]
    [InlineData("SECRET")]
    public void The_secret_name_is_reserved(string name) => Assert.Contains("reserved", VariableName.Check(name));
}

public class TestFileNamesTests
{
    [Theory]
    [InlineData("Get one todo", "get-one-todo")]
    [InlineData("  Spaces   and---dashes  ", "spaces-and-dashes")]
    [InlineData("Şifre Değiştir: ığüöç / #1", "sifre-degistir-iguoc-1")]
    [InlineData("Ünïcödé Ñame", "unicode-name")]
    [InlineData("İstanbul", "istanbul")]
    [InlineData("under_score kept", "under_score-kept")]
    [InlineData("already.test.yaml", "already")]
    [InlineData("name.shared.yaml", "name")]
    [InlineData("???", "untitled")]
    [InlineData("", "untitled")]
    [InlineData(null, "untitled")]
    public void Slug_is_ascii_lowercase_and_readable(string? text, string expected) => Assert.Equal(expected, TestFileNames.Slug(text));

    [Fact]
    public void Slug_of_a_long_name_is_cut_on_a_word_boundary()
    {
        var slug = TestFileNames.Slug("Verify that a very long description used as a name never produces an unwieldy file name");
        Assert.True(slug.Length <= TestFileNames.MaxLength);
        Assert.Equal("verify-that-a-very-long-description-used-as-a", slug);
        Assert.False(slug.EndsWith('-'));
    }

    [Fact]
    public void Slug_of_one_very_long_word_is_cut_hard()
    {
        Assert.Equal(TestFileNames.MaxLength, TestFileNames.Slug(new string('a', 200)).Length);
    }

    [Theory]
    [InlineData("get-one-todo", "Get one todo", true)]
    [InlineData("get-one-todo-2", "Get one todo", true)]      // a collision suffix still counts as generated
    [InlineData("get-one-todo-x", "Get one todo", false)]
    [InlineData("my-custom-name", "Get one todo", false)]      // renamed by hand
    [InlineData("get-one-todo", "Something else", false)]
    public void IsGeneratedFrom_recognises_names_Axiom_chose(string fileId, string name, bool generated) =>
        Assert.Equal(generated, TestFileNames.IsGeneratedFrom(fileId, name));
}

public class CollectionPathsTests
{
    [Fact]
    public void File_names_and_ids_convert_both_ways()
    {
        Assert.Equal("a.test.yaml", CollectionPaths.ToTestFileName("a"));
        Assert.Equal("a.test.yaml", CollectionPaths.ToTestFileName("a.test.yaml"));
        Assert.Equal("a", CollectionPaths.ToTestId("a.test.yaml"));
        Assert.Equal("a", CollectionPaths.ToTestId("a"));
        Assert.Equal("b.shared.yaml", CollectionPaths.ToFileName(CollectionPaths.Shared, "b"));
        Assert.Equal("b", CollectionPaths.ToId(CollectionPaths.Shared, "b.SHARED.yaml"));
    }

    [Fact]
    public void Paths_are_built_inside_the_collection_folder()
    {
        using var folder = new TempFolder();
        Assert.Equal(Path.Combine(folder.Path, "collection.yaml"), CollectionPaths.CollectionFile(folder.Path));
        Assert.Equal(Path.Combine(folder.Path, "tests"), CollectionPaths.TestsDirectory(folder.Path));
        Assert.Equal(Path.Combine(folder.Path, "shared"), CollectionPaths.Directory(folder.Path, CollectionPaths.Shared));
        Assert.Equal(Path.Combine(folder.Path, "tests", "x.test.yaml"), CollectionPaths.TestFile(folder.Path, "x"));
        Assert.Equal("*.test.yaml", CollectionPaths.Tests.Pattern);
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("sub/dir")]
    [InlineData("..\\evil")]
    public void Names_that_could_leave_the_folder_are_rejected(string name)
    {
        using var folder = new TempFolder();
        // A backslash is only a separator on Windows; elsewhere it is an ordinary (if odd) file name character.
        if (name.Contains('\\') && !OperatingSystem.IsWindows())
        {
            Assert.StartsWith(folder.Path, CollectionPaths.TestFile(folder.Path, name));
            return;
        }

        Assert.Throws<ArgumentException>(() => CollectionPaths.TestFile(folder.Path, name));
    }
}
