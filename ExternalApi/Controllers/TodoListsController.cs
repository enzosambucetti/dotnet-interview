using ExternalApi.Contracts;
using ExternalApi.Models;
using ExternalApi.Store;
using Microsoft.AspNetCore.Mvc;

namespace ExternalApi.Controllers;

[ApiController]
[Route("todolists")]
public class TodoListsController : ControllerBase
{
    private readonly IExternalTodoStore _store;

    public TodoListsController(IExternalTodoStore store)
    {
        _store = store;
    }

    [HttpGet]
    public ActionResult<IList<TodoList>> ListTodoLists()
    {
        return Ok(_store.GetTodoLists());
    }

    [HttpPost]
    public ActionResult<TodoList> CreateTodoList(CreateTodoListBody body)
    {
        var todoList = _store.CreateTodoList(body);

        return Created($"/todolists/{todoList.Id}", todoList);
    }

    [HttpPatch("{todolistId}")]
    public ActionResult<TodoList> UpdateTodoList(string todolistId, UpdateTodoListBody body)
    {
        var todoList = _store.UpdateTodoList(todolistId, body);

        if (todoList == null)
        {
            return NotFound();
        }

        return Ok(todoList);
    }

    [HttpDelete("{todolistId}")]
    public IActionResult DeleteTodoList(string todolistId)
    {
        if (!_store.DeleteTodoList(todolistId))
        {
            return NotFound();
        }

        return NoContent();
    }
}
