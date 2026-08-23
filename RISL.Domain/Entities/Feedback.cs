namespace RISL.Domain.Entities;

/// <summary>
/// Сообщение от гостя: предложение нового слова, замечание к описанию, вопрос.
/// Никуда не пересылается — админ читает список в панели.
/// </summary>
public class Feedback
{
    private Feedback()
    {
        // Для EF Core.
    }

    public Feedback(string message, string? name, string? contact)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Сообщение не может быть пустым.", nameof(message));
        }

        Message = message.Trim();
        Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        Contact = string.IsNullOrWhiteSpace(contact) ? null : contact.Trim();
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int Id { get; private set; }

    /// <summary>Как представился отправитель. Заполнять необязательно.</summary>
    public string? Name { get; private set; }

    /// <summary>Любой способ связи, который оставил отправитель. Заполнять необязательно.</summary>
    public string? Contact { get; private set; }

    public string Message { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsHandled { get; set; }
}
