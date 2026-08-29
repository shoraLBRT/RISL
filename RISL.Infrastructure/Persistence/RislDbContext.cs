using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RISL.Domain.Entities;

namespace RISL.Infrastructure.Persistence;

/// <summary>
/// Хранилище словаря. Объём данных здесь мизерный — три тысячи статей, — поэтому
/// SQLite покрывает задачу целиком, а бэкап сводится к одному файлу.
/// </summary>
public class RislDbContext(DbContextOptions<RislDbContext> options) : DbContext(options)
{
    public DbSet<Word> Words => Set<Word>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Feedback> Feedback => Set<Feedback>();

    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();

    /// <summary>
    /// Формат хранения меток времени: UTC фиксированной ширины.
    /// </summary>
    /// <remarks>
    /// SQLite отказывается сортировать по DateTimeOffset, потому что в его обычном
    /// представлении есть смещение часового пояса и сравнение становится
    /// бессмысленным. Приводим всё к UTC в ISO-8601: такие строки сортируются
    /// лексикографически и при этом читаются глазами в любом просмотрщике базы.
    /// </remarks>
    private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

    private static readonly ValueConverter<DateTimeOffset, string> TimestampConverter = new(
        value => value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture),
        value => DateTimeOffset.ParseExact(
            value,
            TimestampFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Word>(word =>
        {
            word.HasKey(entity => entity.Id);

            word.Property(entity => entity.Text).IsRequired().HasMaxLength(200);
            word.Property(entity => entity.NormalizedText).IsRequired().HasMaxLength(200);
            word.Property(entity => entity.Description).IsRequired();
            word.Property(entity => entity.NormalizedDescription).IsRequired();
            word.Property(entity => entity.Slug).IsRequired().HasMaxLength(120);
            word.Property(entity => entity.VideoFileName).HasMaxLength(100);
            word.Property(entity => entity.PosterFileName).HasMaxLength(100);
            word.Property(entity => entity.IncomingVideoFileName).HasMaxLength(100);
            word.Property(entity => entity.VideoError).HasMaxLength(2000);
            word.Property(entity => entity.VideoStatus).HasConversion<int>();
            word.Property(entity => entity.CreatedAt).HasConversion(TimestampConverter);
            word.Property(entity => entity.UpdatedAt).HasConversion(TimestampConverter);

            // Ключ сопоставления при импорте: два написания одного слова недопустимы.
            word.HasIndex(entity => entity.NormalizedText).IsUnique();

            // Слаг участвует только в оформлении URL, авторитетным остаётся Id,
            // поэтому уникальность не требуется — индекс нужен лишь для поиска по адресу.
            word.HasIndex(entity => entity.Slug);

            // Фоновая обработка выбирает незавершённые задания при каждом старте.
            word.HasIndex(entity => entity.VideoStatus);

            // Коллекции доступны только на чтение, EF работает с полями _categories
            // и _words по соглашению об именовании.
            word.HasMany(entity => entity.Categories)
                .WithMany(category => category.Words)
                .UsingEntity(join => join.ToTable("WordCategories"));
        });

        modelBuilder.Entity<Category>(category =>
        {
            category.HasKey(entity => entity.Id);

            category.Property(entity => entity.Name).IsRequired().HasMaxLength(100);
            category.Property(entity => entity.NormalizedName).IsRequired().HasMaxLength(100);
            category.Property(entity => entity.Slug).IsRequired().HasMaxLength(120);

            category.HasIndex(entity => entity.NormalizedName).IsUnique();
        });

        modelBuilder.Entity<Feedback>(feedback =>
        {
            feedback.HasKey(entity => entity.Id);

            feedback.Property(entity => entity.Name).HasMaxLength(200);
            feedback.Property(entity => entity.Contact).HasMaxLength(200);
            feedback.Property(entity => entity.Message).IsRequired().HasMaxLength(4000);
            feedback.Property(entity => entity.CreatedAt).HasConversion(TimestampConverter);

            // Непрочитанные сообщения показываются на главной админки при каждом входе.
            feedback.HasIndex(entity => new { entity.IsHandled, entity.CreatedAt });
        });

        modelBuilder.Entity<ImportJob>(job =>
        {
            job.HasKey(entity => entity.Id);

            job.Property(entity => entity.FileName).IsRequired().HasMaxLength(260);
            job.Property(entity => entity.ReportJson).IsRequired();
            job.Property(entity => entity.Status).HasConversion<int>();
            job.Property(entity => entity.CreatedAt).HasConversion(TimestampConverter);
            job.Property(entity => entity.CompletedAt).HasConversion(TimestampConverter!);
        });
    }
}
