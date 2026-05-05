using Microsoft.AspNetCore.Mvc;
using TodoApi.Dtos;
using TodoApi.Errors;
using TodoApi.Models;
using TodoApi.Services;

namespace TodoApi.Controllers
{
    [Route("api/todolists")]
    [ApiController]
    public class TodoListsController : ControllerBase
    {
        private readonly ILogger<TodoListsController> _logger;
        private readonly ITodoListsService _todoListsService;

        public TodoListsController(
            ITodoListsService todoListsService,
            ILogger<TodoListsController> logger
        )
        {
            _todoListsService = todoListsService;
            _logger = logger;
        }

        // GET: api/todolists
        [HttpGet]
        public async Task<ActionResult<IList<TodoList>>> GetTodoLists()
        {
            return Ok(await _todoListsService.GetTodoListsAsync());
        }

        // GET: api/todolists/5
        [HttpGet("{id}")]
        public async Task<ActionResult<TodoList>> GetTodoList(long id)
        {
            var todoList = await _todoListsService.GetTodoListAsync(id);

            if (todoList == null)
            {
                LogTodoListNotFound("TodoList not found.", id);
                return TodoListNotFound(id);
            }

            return Ok(todoList);
        }

        // PUT: api/todolists/5
        // To protect from over-posting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<ActionResult> PutTodoList(long id, UpdateTodoList payload)
        {
            var todoList = await _todoListsService.UpdateTodoListAsync(id, payload);

            if (todoList == null)
            {
                LogTodoListNotFound("TodoList update target not found.", id);
                return TodoListNotFound(id);
            }

            return Ok(todoList);
        }

        // POST: api/todolists
        // To protect from over-posting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<TodoList>> PostTodoList(CreateTodoList payload)
        {
            var todoList = await _todoListsService.CreateTodoListAsync(payload);

            return CreatedAtAction("GetTodoList", new { id = todoList.Id }, todoList);
        }

        // DELETE: api/todolists/5
        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteTodoList(long id)
        {
            if (!await _todoListsService.DeleteTodoListAsync(id))
            {
                LogTodoListNotFound("TodoList delete target not found.", id);
                return TodoListNotFound(id);
            }

            return NoContent();
        }

        private ObjectResult TodoListNotFound(long id)
        {
            return ApiProblemDetails.NotFound(
                this,
                "Todo list not found.",
                $"Todo list '{id}' was not found.",
                "todo_list_not_found"
            );
        }

        private void LogTodoListNotFound(string message, long id)
        {
            _logger.LogWarning(
                ApiErrorEventIds.TodoListNotFound,
                "{Message} TodoListId: {TodoListId}; TraceId: {TraceId}",
                message,
                id,
                ControllerContext.HttpContext?.TraceIdentifier
            );
        }
    }
}
