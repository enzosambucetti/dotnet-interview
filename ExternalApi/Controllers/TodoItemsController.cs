using ExternalApi.Contracts;
using ExternalApi.Models;
using ExternalApi.Store;
using Microsoft.AspNetCore.Mvc;

namespace ExternalApi.Controllers;

[ApiController]
[Route("todolists/{todolistId}/todoitems")]
public class TodoItemsController : ControllerBase
{
    private readonly IExternalTodoStore _store;

    public TodoItemsController(IExternalTodoStore store)
    {
        _store = store;
    }

    [HttpPatch("{todoitemId}")]
    public ActionResult<TodoItem> UpdateTodoItem(
        string todolistId,
        string todoitemId,
        UpdateTodoItemBody body
    )
    {
        var todoItem = _store.UpdateTodoItem(todolistId, todoitemId, body);

        if (todoItem == null)
        {
            return NotFound();
        }

        return Ok(todoItem);
    }

    [HttpDelete("{todoitemId}")]
    public IActionResult DeleteTodoItem(string todolistId, string todoitemId)
    {
        if (!_store.DeleteTodoItem(todolistId, todoitemId))
        {
            return NotFound();
        }

        return NoContent();
    }
}
