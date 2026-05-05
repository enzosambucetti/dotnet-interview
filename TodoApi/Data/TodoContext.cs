using Microsoft.EntityFrameworkCore;
using TodoApi.Models;

public class TodoContext : DbContext
{
    public TodoContext(DbContextOptions<TodoContext> options)
        : base(options) { }

    public DbSet<TodoList> TodoList { get; set; } = default!;
    public DbSet<Item> Items { get; set; } = default!;
    public DbSet<SyncEvent> SyncEvents { get; set; } = default!;

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
                .WithMany(todoList => todoList.Items)
                .HasForeignKey(item => item.TodoListId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SyncEvent>(entity =>
        {
            entity.ToTable("SyncEvents");
            entity.Property(syncEvent => syncEvent.EntityType).HasMaxLength(64);
            entity.Property(syncEvent => syncEvent.EventType).HasMaxLength(64);
            entity.Property(syncEvent => syncEvent.Status).HasMaxLength(64);
            entity.Property(syncEvent => syncEvent.CorrelationId).HasMaxLength(128);
        });
    }
}
