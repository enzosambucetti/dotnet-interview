using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TodoApi.External;
using TodoApi.Models;
using TodoApi.Sync.Jobs;

namespace TodoApi.Tests.E2E;

public class SyncE2ETests
{
    [Fact]
    public async Task PostTodoList_WhenOutboundJobRuns_CreatesListInExternalApi()
    {
        using var externalFactory = new ExternalApiFactory();
        using var todoFactory = new TodoApiFactory(externalFactory);
        var todoClient = todoFactory.CreateClient();
        var externalClient = externalFactory.CreateClient();
        await ResetExternalApiAsync(externalClient);

        var response = await todoClient.PostAsJsonAsync(
            "/api/todolists",
            new { name = "E2E Work" }
        );
        response.EnsureSuccessStatusCode();
        var localTodoListId = await ReadLongPropertyAsync(response, "Id");
        var syncEventId = await GetLatestSyncEventIdAsync(todoFactory);

        await ProcessOutboundAsync(todoFactory, syncEventId);

        var externalTodoLists =
            await externalClient.GetFromJsonAsync<IList<global::ExternalApi.Models.TodoList>>(
                "/todolists"
            );

        Assert.Contains(externalTodoLists!, x => x.Name == "E2E Work");
        await AssertLocalTodoListHasExternalIdAsync(todoFactory, localTodoListId);
    }

    [Fact]
    public async Task PostItem_WhenOutboundJobRuns_CreatesItemInExternalApi()
    {
        using var externalFactory = new ExternalApiFactory();
        using var todoFactory = new TodoApiFactory(externalFactory);
        var todoClient = todoFactory.CreateClient();
        var externalClient = externalFactory.CreateClient();
        await ResetExternalApiAsync(externalClient);

        var listResponse = await todoClient.PostAsJsonAsync(
            "/api/todolists",
            new { name = "E2E Work" }
        );
        listResponse.EnsureSuccessStatusCode();
        var localTodoListId = await ReadLongPropertyAsync(listResponse, "Id");
        await ProcessOutboundAsync(todoFactory, await GetLatestSyncEventIdAsync(todoFactory));

        var itemResponse = await todoClient.PostAsJsonAsync(
            $"/api/todolists/{localTodoListId}/items",
            new { name = "E2E Item", isCompleted = false }
        );
        itemResponse.EnsureSuccessStatusCode();
        var localItemId = await ReadLongPropertyAsync(itemResponse, "Id");
        await ProcessOutboundAsync(todoFactory, await GetLatestSyncEventIdAsync(todoFactory));

        var externalTodoLists =
            await externalClient.GetFromJsonAsync<IList<global::ExternalApi.Models.TodoList>>(
                "/todolists"
            );
        Assert.Contains(
            externalTodoLists!,
            x =>
                x.SourceId == localTodoListId.ToString()
                && x.Items.Any(item => item.Description == "E2E Item")
        );
        await AssertLocalItemHasExternalIdAsync(todoFactory, localItemId);
    }

    [Fact]
    public async Task CreateExternalTodoList_WhenInboundJobRuns_ImportsListIntoTodoApi()
    {
        using var externalFactory = new ExternalApiFactory();
        using var todoFactory = new TodoApiFactory(externalFactory);
        var todoClient = todoFactory.CreateClient();
        var externalClient = externalFactory.CreateClient();
        await ResetExternalApiAsync(externalClient);

        var externalResponse = await externalClient.PostAsJsonAsync(
            "/todolists",
            new
            {
                source_id = "external-e2e-list",
                name = "Inbound E2E Work",
                items = Array.Empty<object>(),
            }
        );
        externalResponse.EnsureSuccessStatusCode();

        await ProcessInboundAsync(todoFactory);

        var localTodoLists = await todoClient.GetFromJsonAsync<IList<TodoList>>("/api/todolists");

        Assert.Contains(localTodoLists!, x => x.Name == "Inbound E2E Work" && x.ExternalId != null);
        await AssertNoOutboundEventsAsync(todoFactory);
    }

    private static async Task<long> ReadLongPropertyAsync(HttpResponseMessage response, string propertyName)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.GetInt64();
            }
        }

        throw new KeyNotFoundException($"Property '{propertyName}' was not present in the response.");
    }

    private static async Task ResetExternalApiAsync(HttpClient externalClient)
    {
        var response = await externalClient.PostAsync("/__test/reset", content: null);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<long> GetLatestSyncEventIdAsync(TodoApiFactory todoFactory)
    {
        using var scope = todoFactory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoContext>();
        return await context.SyncEvents
            .OrderByDescending(x => x.Id)
            .Select(x => x.Id)
            .FirstAsync();
    }

    private static async Task ProcessOutboundAsync(TodoApiFactory todoFactory, long syncEventId)
    {
        using var scope = todoFactory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<IOutboundSyncJob>();
        await job.ProcessAsync(syncEventId, CancellationToken.None);
    }

    private static async Task ProcessInboundAsync(TodoApiFactory todoFactory)
    {
        using var scope = todoFactory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<IInboundSyncJob>();
        await job.ProcessAsync(CancellationToken.None);
    }

    private static async Task AssertLocalTodoListHasExternalIdAsync(
        TodoApiFactory todoFactory,
        long localTodoListId
    )
    {
        using var scope = todoFactory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoContext>();
        var todoList = await context.TodoList.IgnoreQueryFilters().SingleAsync(x => x.Id == localTodoListId);
        Assert.False(string.IsNullOrWhiteSpace(todoList.ExternalId));
    }

    private static async Task AssertLocalItemHasExternalIdAsync(
        TodoApiFactory todoFactory,
        long localItemId
    )
    {
        using var scope = todoFactory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoContext>();
        var item = await context.Items.IgnoreQueryFilters().SingleAsync(x => x.Id == localItemId);
        Assert.False(string.IsNullOrWhiteSpace(item.ExternalId));
    }

    private static async Task AssertNoOutboundEventsAsync(TodoApiFactory todoFactory)
    {
        using var scope = todoFactory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoContext>();
        Assert.Empty(await context.SyncEvents.ToListAsync());
    }

    private class ExternalApiFactory
        : WebApplicationFactory<global::ExternalApi.ExternalApiAssemblyMarker>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
        }
    }

    private class TodoApiFactory : WebApplicationFactory<global::TodoApi.TodoApiAssemblyMarker>
    {
        private readonly ExternalApiFactory _externalApiFactory;
        private readonly string _databaseName = Guid.NewGuid().ToString();

        public TodoApiFactory(ExternalApiFactory externalApiFactory)
        {
            _externalApiFactory = externalApiFactory;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration(
                (_, configuration) =>
                {
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Sync:HangfireEnabled"] = "false",
                            ["ExternalApi:BaseUrl"] = "http://external-api.test",
                            ["ExternalApi:TimeoutSeconds"] = "10",
                            ["ExternalApi:MaxRetries"] = "1",
                        }
                    );
                }
            );
            builder.ConfigureTestServices(
                services =>
                {
                    services.RemoveAll<DbContextOptions<TodoContext>>();
                    services.AddDbContext<TodoContext>(
                        options => options.UseInMemoryDatabase(_databaseName)
                    );

                    services.RemoveAll<IExternalTodoApiClient>();
                    services.AddHttpClient<IExternalTodoApiClient, ExternalTodoApiClient>(
                        (serviceProvider, client) =>
                        {
                            var options = serviceProvider
                                .GetRequiredService<
                                    Microsoft.Extensions.Options.IOptions<ExternalApiOptions>
                                >()
                                .Value;
                            client.BaseAddress = new Uri(options.BaseUrl);
                            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
                        }
                    )
                        .ConfigurePrimaryHttpMessageHandler(
                            () => _externalApiFactory.Server.CreateHandler()
                        );
                }
            );
        }
    }
}
