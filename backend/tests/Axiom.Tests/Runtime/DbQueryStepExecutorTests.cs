using Axiom.Secrets;
using Microsoft.Data.Sqlite;

namespace Axiom.Tests.Runtime;

public class DbQueryStepExecutorTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly string _database;

    public DbQueryStepExecutorTests()
    {
        _database = Path.Combine(_folder.Path, "test.db");
        using var connection = new SqliteConnection($"Data Source={_database}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE todos (Id INTEGER, Title TEXT, Done INTEGER NULL); INSERT INTO todos VALUES (1,'first',0),(2,'second',NULL),(3,'third',1);";
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _folder.Dispose();
    }

    private async Task<(StepExecutionResult Result, StepExecutionContext Context)> Run(StepDefinition step, string? connectionString = null, string provider = "sqlite", RunSecrets? secrets = null)
    {
        await using var queries = new DbQueryExecutor([new SqliteConnectionFactory(), new SqlServerConnectionFactory()]);
        var runner = new StepRunner([new DbQueryStepExecutor(queries, Build.Engine())]);
        var collection = Build.Collection();
        collection.Connections["db"] = new DbConnectionDefinition { Provider = provider, ConnectionString = connectionString ?? $"Data Source={_database}" };
        var context = Build.Context(collection, runner, secrets: secrets);
        var results = await runner.RunAsync(context, [step], default);
        return (results[0], context);
    }

    private static StepDefinition Query(string sql, string? saveAs = null, params AssertionDefinition[] assertions) =>
        new() { Id = "q", Name = "q", Type = "db_query", Connection = "db", Sql = sql, SaveAs = saveAs, Assert = assertions.ToList() };

    [Fact]
    public async Task Runs_the_query_and_exposes_rows_and_row_count()
    {
        var (result, _) = await Run(Query("SELECT * FROM todos WHERE Id > 1 ORDER BY Id", null,
            Build.Assertion("row_count", "==", 2),
            Build.Assertion("rows", "==", "second", "0.Title"),
            Build.Assertion("rows", "==", 2, aggregate: "count"),
            Build.Assertion("rows", "==", 5, "*.Id", "sum"),
            Build.Assertion("rows", "is_null", null, "0.Done")));           // a NULL column is present and null

        Assert.True(result.Passed, string.Join("; ", result.Assertions.Where(a => !a.Passed).Select(a => a.Error)));
        Assert.Equal(2, result.RowCount);
    }

    [Fact]
    public async Task The_saved_rows_can_be_used_in_later_steps_by_template()
    {
        var (_, context) = await Run(Query("SELECT Title FROM todos WHERE Id = 3", "todo"));

        Assert.Equal("third", TemplateResolver.ResolveString("{{todo.0.Title}}", context.Variables));
        Assert.Equal(1, context.Variables["q_row_count"]);
    }

    [Fact]
    public async Task Sql_can_use_variables()
    {
        var (result, context) = await Run(Query("SELECT Title FROM todos WHERE Id = {{wanted}}", "row"), null);
        Assert.Equal(RunOutcome.Error, result.Outcome);                      // the variable is not defined
        Assert.Contains("wanted", result.Error);
        Assert.NotNull(context);
    }

    [Fact]
    public async Task A_query_that_returns_nothing_still_passes_a_zero_row_assertion()
    {
        var (result, _) = await Run(Query("SELECT * FROM todos WHERE Id = 99", null, Build.Assertion("row_count", "==", 0)));
        Assert.True(result.Passed);
    }

    [Fact]
    public async Task Secrets_can_be_used_in_the_connection_string()
    {
        var secrets = new RunSecrets(new Dictionary<string, string> { ["db_path"] = _database });
        var (result, _) = await Run(Query("SELECT COUNT(*) AS n FROM todos", null, Build.Assertion("rows", "==", 3, "0.n")),
            "Data Source={{secret.db_path}}", secrets: secrets);

        Assert.True(result.Passed);
    }

    [Fact]
    public async Task Bad_sql_and_missing_configuration_are_errors()
    {
        var badSql = (await Run(Query("SELECT * FROM missing_table"))).Result;
        Assert.Equal(RunOutcome.Error, badSql.Outcome);

        var noConnection = Query("SELECT 1");
        noConnection.Connection = null;
        Assert.Contains("requires connection", (await Run(noConnection)).Result.Error);

        var noSql = Query("");
        Assert.Contains("requires sql", (await Run(noSql)).Result.Error);

        var unknown = Query("SELECT 1");
        unknown.Connection = "nope";
        Assert.Contains("'nope' was not found", (await Run(unknown)).Result.Error);

        var provider = (await Run(Query("SELECT 1"), provider: "oracle")).Result;
        Assert.Contains("Unsupported DB provider 'oracle'", provider.Error);
    }
}
