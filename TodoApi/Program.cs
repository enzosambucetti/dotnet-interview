using Microsoft.EntityFrameworkCore;
using TodoApi.Middleware;
using TodoApi.Repositories;
using TodoApi.Services;

var builder = WebApplication.CreateBuilder(args);
var todoConnectionString = TodoDatabaseConfiguration.GetTodoConnectionString(builder.Configuration);

builder
    .Services.AddDbContext<TodoContext>(opt =>
        opt.UseSqlServer(todoConnectionString)
    )
    .AddEndpointsApiExplorer()
    .AddSwaggerGen()
    .AddControllers();

builder.Services.AddScoped<IItemsRepository, ItemsRepository>();
builder.Services.AddScoped<IItemsService, ItemsService>();
builder.Services.AddScoped<ITodoListsRepository, TodoListsRepository>();
builder.Services.AddScoped<ITodoListsService, TodoListsService>();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();


var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<UnhandledExceptionMiddleware>();
app.UseAuthorization();
app.MapControllers();
app.Run();
