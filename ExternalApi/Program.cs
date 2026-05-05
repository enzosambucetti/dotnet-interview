using ExternalApi.Store;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IExternalTodoStore, ExternalTodoStore>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    app.MapPost(
        "/__test/reset",
        (IExternalTodoStore store) =>
        {
            store.Reset();
            return Results.NoContent();
        }
    )
        .WithTags("Test")
        .WithName("ResetExternalTodoData");
}

app.MapControllers();
app.Run();
