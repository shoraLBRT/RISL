using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RISL.Infrastructure.Persistence;

/// <summary>
/// Контекст для инструментов EF Core во время разработки.
/// </summary>
/// <remarks>
/// Нужен только команде <c>dotnet ef migrations</c>: она поднимает контекст вне
/// приложения, где нет ни конфигурации, ни контейнера. В рантайме не участвует.
/// </remarks>
public sealed class RislDbContextFactory : IDesignTimeDbContextFactory<RislDbContext>
{
    public RislDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<RislDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;

        return new RislDbContext(options);
    }
}
