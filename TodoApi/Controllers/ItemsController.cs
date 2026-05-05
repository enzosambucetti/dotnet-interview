using Microsoft.AspNetCore.Mvc;
using TodoApi.Dtos;
using TodoApi.Errors;
using TodoApi.Models;
using TodoApi.Services;

namespace TodoApi.Controllers;

[Route("api/todolists/{todoListId:long}/items")]
[ApiController]
public class ItemsController : ControllerBase
{
    private readonly IItemsService _itemsService;
    private readonly ILogger<ItemsController> _logger;

    public ItemsController(IItemsService itemsService, ILogger<ItemsController> logger)
    {
        _itemsService = itemsService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IList<Item>>> GetItems(long todoListId)
    {
        var items = await _itemsService.GetItemsAsync(todoListId);

        if (items == null)
        {
            LogTodoListNotFound("TodoList for items was not found.", todoListId);
            return TodoListNotFound(todoListId);
        }

        return Ok(items);
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<Item>> GetItem(long todoListId, long id)
    {
        var item = await _itemsService.GetItemAsync(todoListId, id);

        if (item == null)
        {
            LogTodoItemNotFound("TodoItem not found.", todoListId, id);
            return TodoItemNotFound(todoListId, id);
        }

        return Ok(item);
    }

    [HttpPost]
    public async Task<ActionResult<Item>> PostItem(long todoListId, CreateItem payload)
    {
        var item = await _itemsService.CreateItemAsync(todoListId, payload);

        if (item == null)
        {
            LogTodoListNotFound("TodoList for item creation was not found.", todoListId);
            return TodoListNotFound(todoListId);
        }

        return CreatedAtAction("GetItem", new { todoListId, id = item.Id }, item);
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult> PutItem(long todoListId, long id, UpdateItem payload)
    {
        var item = await _itemsService.UpdateItemAsync(todoListId, id, payload);

        if (item == null)
        {
            LogTodoItemNotFound("TodoItem update target not found.", todoListId, id);
            return TodoItemNotFound(todoListId, id);
        }

        return Ok(item);
    }

    [HttpDelete("{id:long}")]
    public async Task<ActionResult> DeleteItem(long todoListId, long id)
    {
        var deleted = await _itemsService.DeleteItemAsync(todoListId, id);

        if (!deleted)
        {
            LogTodoItemNotFound("TodoItem delete target not found.", todoListId, id);
            return TodoItemNotFound(todoListId, id);
        }

        return NoContent();
    }

    private ObjectResult TodoListNotFound(long todoListId)
    {
        return ApiProblemDetails.NotFound(
            this,
            "Todo list not found.",
            $"Todo list '{todoListId}' was not found.",
            "todo_list_not_found"
        );
    }

    private ObjectResult TodoItemNotFound(long todoListId, long id)
    {
        return ApiProblemDetails.NotFound(
            this,
            "Todo item not found.",
            $"Todo item '{id}' was not found in todo list '{todoListId}'.",
            "todo_item_not_found"
        );
    }

    private void LogTodoListNotFound(string message, long todoListId)
    {
        _logger.LogWarning(
            ApiErrorEventIds.TodoListNotFound,
                "{Message} TodoListId: {TodoListId}; TraceId: {TraceId}",
                message,
                todoListId,
                ControllerContext.HttpContext?.TraceIdentifier
            );
    }

    private void LogTodoItemNotFound(string message, long todoListId, long id)
    {
        _logger.LogWarning(
            ApiErrorEventIds.TodoItemNotFound,
                "{Message} TodoListId: {TodoListId}; ItemId: {ItemId}; TraceId: {TraceId}",
                message,
                todoListId,
                id,
                ControllerContext.HttpContext?.TraceIdentifier
            );
        }
}
