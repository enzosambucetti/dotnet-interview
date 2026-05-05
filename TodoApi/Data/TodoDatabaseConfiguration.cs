using Microsoft.Extensions.Configuration;

public static class TodoDatabaseConfiguration
{
    public static string GetTodoConnectionString(IConfiguration configuration)
    {
        var databaseTarget = configuration["DatabaseTarget"];

        if (string.IsNullOrWhiteSpace(databaseTarget))
        {
            throw new InvalidOperationException(
                "Missing configuration value 'DatabaseTarget'. Set it to 'DockerSql' or 'LocalSql'."
            );
        }

        var connectionString = configuration.GetConnectionString(databaseTarget);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Missing connection string for target '{databaseTarget}'."
            );
        }

        return connectionString;
    }
}
