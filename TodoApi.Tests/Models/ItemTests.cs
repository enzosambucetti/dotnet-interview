using System.Text.Json;
using TodoApi.Models;

namespace TodoApi.Tests.Models;

public class ItemTests
{
    [Fact]
    public void Item_WhenSerialized_UsesSynchronizationPropertyNamesAndIgnoresNavigation()
    {
        var item = new Item
        {
            Id = 1,
            SourceId = "ext-item-1",
            Name = "Review sync",
            IsCompleted = true,
            CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-01-02T00:00:00Z"),
            IsDeleted = true,
            DeletedAt = DateTimeOffset.Parse("2026-01-03T00:00:00Z"),
            TodoListId = 10,
            TodoList = new TodoList { Id = 10, Name = "Work" },
        };

        var json = JsonSerializer.Serialize(item);

        Assert.Contains("\"source_id\"", json);
        Assert.Contains("\"created_at\"", json);
        Assert.Contains("\"updated_at\"", json);
        Assert.Contains("\"is_deleted\"", json);
        Assert.Contains("\"deleted_at\"", json);
        Assert.DoesNotContain("\"TodoList\":", json);
    }
}
