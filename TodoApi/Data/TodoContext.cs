using Microsoft.EntityFrameworkCore;
using TodoApi.Models;

public class TodoContext : DbContext
{
    public TodoContext(DbContextOptions<TodoContext> options)
        : base(options) { }

    public DbSet<TodoList> TodoList { get; set; } = default!;
    public DbSet<Item> Items { get; set; } = default!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TodoList>(entity =>
        {
            entity.HasQueryFilter(todoList => !todoList.IsDeleted);
        });

        modelBuilder.Entity<Item>(entity =>
        {
            entity.ToTable("Items");
            entity.HasQueryFilter(item => !item.IsDeleted);

            entity
                .HasOne(item => item.TodoList)
                .WithMany()
                .HasForeignKey(item => item.TodoListId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
