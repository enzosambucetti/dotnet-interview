using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

public class TodoContextDesignTimeFactory : IDesignTimeDbContextFactory<TodoContext>
{
    public TodoContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        var connectionString = TodoDatabaseConfiguration.GetTodoConnectionString(configuration);
        var optionsBuilder = new DbContextOptionsBuilder<TodoContext>();

        optionsBuilder.UseSqlServer(connectionString);

        return new TodoContext(optionsBuilder.Options);
    }
}
