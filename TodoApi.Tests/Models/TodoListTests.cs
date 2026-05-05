using System.Text.Json;
using TodoApi.Models;

namespace TodoApi.Tests.Models;

public class TodoListTests
{
    [Fact]
    public void TodoList_WhenSerialized_UsesSynchronizationPropertyNames()
    {
        var todoList = new TodoList
        {
            Id = 1,
            SourceId = "ext-list-1",
            Name = "Work",
            CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-01-02T00:00:00Z"),
            IsDeleted = true,
            DeletedAt = DateTimeOffset.Parse("2026-01-03T00:00:00Z"),
        };

        var json = JsonSerializer.Serialize(todoList);

        Assert.Contains("\"source_id\"", json);
        Assert.Contains("\"created_at\"", json);
        Assert.Contains("\"updated_at\"", json);
        Assert.Contains("\"is_deleted\"", json);
        Assert.Contains("\"deleted_at\"", json);
    }
}
