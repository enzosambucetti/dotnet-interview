using Hangfire;
using Hangfire.SqlServer;
using Microsoft.EntityFrameworkCore;
using TodoApi.External;
using TodoApi.Middleware;
using TodoApi.Realtime;
using TodoApi.Repositories;
using TodoApi.Services;
using TodoApi.Sync;
using TodoApi.Sync.Jobs;

var builder = WebApplication.CreateBuilder(args);
var todoConnectionString = TodoDatabaseConfiguration.GetTodoConnectionString(builder.Configuration);
var hangfireEnabled = builder.Configuration.GetValue("Sync:HangfireEnabled", true);
var realtimeAllowedOrigins =
    builder.Configuration.GetSection("Realtime:AllowedOrigins").Get<string[]>() ?? [];
const string realtimeCorsPolicy = "RealtimeCors";

builder
    .Services.AddDbContext<TodoContext>(opt =>
        opt.UseSqlServer(todoConnectionString)
    )
    .AddEndpointsApiExplorer()
    .AddSwaggerGen()
    .AddControllers();
builder.Services.AddSignalR();
if (realtimeAllowedOrigins.Length > 0)
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy(
            realtimeCorsPolicy,
            policy =>
            {
                policy
                    .WithOrigins(realtimeAllowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            }
        );
    });
}

builder.Services.Configure<ExternalApiOptions>(
    builder.Configuration.GetSection(ExternalApiOptions.SectionName)
);
builder.Services.AddHttpClient<IExternalTodoApiClient, ExternalTodoApiClient>(
    (serviceProvider, client) =>
    {
        var options = serviceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<ExternalApiOptions>>()
            .Value;

        client.BaseAddress = new Uri(options.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    }
);
if (hangfireEnabled)
{
    builder.Services.AddHangfire(configuration =>
    {
        configuration.UseSqlServerStorage(
            todoConnectionString,
            new SqlServerStorageOptions
            {
                PrepareSchemaIfNecessary = true,
            }
        );
    });
    builder.Services.AddHangfireServer();
}

builder.Services.AddScoped<IItemsRepository, ItemsRepository>();
builder.Services.AddScoped<IItemsService, ItemsService>();
builder.Services.AddScoped<ITodoListsRepository, TodoListsRepository>();
builder.Services.AddScoped<ITodoListsService, TodoListsService>();
builder.Services.AddScoped<ISyncEventPublisher, SyncEventPublisher>();
builder.Services.AddScoped<IOutboundSyncJob, OutboundSyncJob>();
builder.Services.AddScoped<IInboundSyncJob, InboundSyncJob>();
builder.Services.AddScoped<ISyncEventRecoveryJob, SyncEventRecoveryJob>();
builder.Services.AddScoped<ITodoRealtimeNotifier, SignalRTodoRealtimeNotifier>();
builder.Services.AddScoped<NoOpSyncJobScheduler>();
builder.Services.AddScoped<HangfireSyncJobScheduler>();
builder.Services.AddScoped<ISyncJobScheduler>(
    serviceProvider =>
    {
        if (hangfireEnabled)
        {
            return serviceProvider.GetRequiredService<HangfireSyncJobScheduler>();
        }

        return serviceProvider.GetRequiredService<NoOpSyncJobScheduler>();
    }
);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();


var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    if (hangfireEnabled)
    {
        app.UseHangfireDashboard("/hangfire");
    }
}

app.UseMiddleware<UnhandledExceptionMiddleware>();
if (realtimeAllowedOrigins.Length > 0)
{
    app.UseCors(realtimeCorsPolicy);
}
app.UseAuthorization();
app.MapControllers();
var todoUpdatesHub = app.MapHub<TodoUpdatesHub>("/hubs/todo-updates");
if (realtimeAllowedOrigins.Length > 0)
{
    todoUpdatesHub.RequireCors(realtimeCorsPolicy);
}

if (hangfireEnabled)
{
    RecurringJob.AddOrUpdate<IInboundSyncJob>(
        "todoapi-inbound-sync",
        job => job.ProcessAsync(CancellationToken.None),
        Cron.Minutely()
    );
    RecurringJob.AddOrUpdate<ISyncEventRecoveryJob>(
        "todoapi-sync-event-recovery",
        job => job.ProcessAsync(CancellationToken.None),
        Cron.Minutely()
    );
}

app.Run();
