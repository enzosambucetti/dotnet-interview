using ExternalApi.Contracts;
using ExternalApi.Models;

namespace ExternalApi.Store;

public class ExternalTodoStore : IExternalTodoStore
{
    private static readonly DateTimeOffset SeedTimestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly object _gate = new();
    private List<TodoList> _todoLists = new();
    private int _nextItemNumber;
    private int _nextListNumber;
    private int _nextTimestampMinute;

    public ExternalTodoStore()
    {
        Reset();
    }

    public IList<TodoList> GetTodoLists()
    {
        lock (_gate)
        {
            return _todoLists.Select(CloneTodoList).ToList();
        }
    }

    public TodoList CreateTodoList(CreateTodoListBody body)
    {
        lock (_gate)
        {
            var timestamp = NextTimestamp();
            var todoList = new TodoList
            {
                Id = NextListId(),
                SourceId = body.SourceId,
                Name = body.Name ?? string.Empty,
                CreatedAt = timestamp,
                UpdatedAt = timestamp,
                Items = (body.Items ?? Array.Empty<CreateTodoItemBody>())
                    .Select(item => new TodoItem
                    {
                        Id = NextItemId(),
                        SourceId = item.SourceId,
                        Description = item.Description ?? string.Empty,
                        Completed = item.Completed.GetValueOrDefault(),
                        CreatedAt = timestamp,
                        UpdatedAt = timestamp,
                    })
                    .ToList(),
            };

            _todoLists.Add(todoList);

            return CloneTodoList(todoList);
        }
    }

    public TodoList? UpdateTodoList(string todoListId, UpdateTodoListBody body)
    {
        lock (_gate)
        {
            var todoList = _todoLists.FirstOrDefault(list => list.Id == todoListId);

            if (todoList == null)
            {
                return null;
            }

            if (body.Name != null)
            {
                todoList.Name = body.Name;
            }

            todoList.UpdatedAt = NextTimestamp();

            return CloneTodoList(todoList);
        }
    }

    public bool DeleteTodoList(string todoListId)
    {
        lock (_gate)
        {
            var todoList = _todoLists.FirstOrDefault(list => list.Id == todoListId);

            if (todoList == null)
            {
                return false;
            }

            _todoLists.Remove(todoList);
            return true;
        }
    }

    public TodoItem? UpdateTodoItem(string todoListId, string todoItemId, UpdateTodoItemBody body)
    {
        lock (_gate)
        {
            var todoItem = _todoLists
                .FirstOrDefault(list => list.Id == todoListId)
                ?.Items.FirstOrDefault(item => item.Id == todoItemId);

            if (todoItem == null)
            {
                return null;
            }

            if (body.Description != null)
            {
                todoItem.Description = body.Description;
            }

            if (body.Completed.HasValue)
            {
                todoItem.Completed = body.Completed.Value;
            }

            todoItem.UpdatedAt = NextTimestamp();

            return CloneTodoItem(todoItem);
        }
    }

    public bool DeleteTodoItem(string todoListId, string todoItemId)
    {
        lock (_gate)
        {
            var todoList = _todoLists.FirstOrDefault(list => list.Id == todoListId);
            var todoItem = todoList?.Items.FirstOrDefault(item => item.Id == todoItemId);

            if (todoList == null || todoItem == null)
            {
                return false;
            }

            todoList.Items.Remove(todoItem);
            todoList.UpdatedAt = NextTimestamp();

            return true;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _todoLists = new List<TodoList>
            {
                new()
                {
                    Id = "ext-list-001",
                    SourceId = "local-list-1",
                    Name = "External Work",
                    CreatedAt = SeedTimestamp,
                    UpdatedAt = SeedTimestamp,
                    Items = new List<TodoItem>
                    {
                        new()
                        {
                            Id = "ext-item-001",
                            SourceId = "local-item-1",
                            Description = "Review sync design",
                            Completed = false,
                            CreatedAt = SeedTimestamp,
                            UpdatedAt = SeedTimestamp,
                        },
                        new()
                        {
                            Id = "ext-item-002",
                            SourceId = "local-item-2",
                            Description = "Prepare demo data",
                            Completed = true,
                            CreatedAt = SeedTimestamp,
                            UpdatedAt = SeedTimestamp,
                        },
                    },
                },
                new()
                {
                    Id = "ext-list-002",
                    SourceId = "local-list-2",
                    Name = "External Personal",
                    CreatedAt = SeedTimestamp,
                    UpdatedAt = SeedTimestamp,
                    Items = new List<TodoItem>
                    {
                        new()
                        {
                            Id = "ext-item-003",
                            SourceId = "local-item-3",
                            Description = "Buy coffee",
                            Completed = false,
                            CreatedAt = SeedTimestamp,
                            UpdatedAt = SeedTimestamp,
                        },
                    },
                },
            };

            _nextListNumber = 3;
            _nextItemNumber = 4;
            _nextTimestampMinute = 1;
        }
    }

    private DateTimeOffset NextTimestamp()
    {
        return SeedTimestamp.AddMinutes(_nextTimestampMinute++);
    }

    private string NextListId()
    {
        return $"ext-list-{_nextListNumber++:000}";
    }

    private string NextItemId()
    {
        return $"ext-item-{_nextItemNumber++:000}";
    }

    private static TodoList CloneTodoList(TodoList todoList)
    {
        return new TodoList
        {
            Id = todoList.Id,
            SourceId = todoList.SourceId,
            Name = todoList.Name,
            CreatedAt = todoList.CreatedAt,
            UpdatedAt = todoList.UpdatedAt,
            Items = todoList.Items.Select(CloneTodoItem).ToList(),
        };
    }

    private static TodoItem CloneTodoItem(TodoItem todoItem)
    {
        return new TodoItem
        {
            Id = todoItem.Id,
            SourceId = todoItem.SourceId,
            Description = todoItem.Description,
            Completed = todoItem.Completed,
            CreatedAt = todoItem.CreatedAt,
            UpdatedAt = todoItem.UpdatedAt,
        };
    }
}
