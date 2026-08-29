using Microsoft.EntityFrameworkCore;
using RISL.Application.Abstractions;
using RISL.Application.Admin;
using RISL.Domain.Entities;
using RISL.Infrastructure.Persistence;

namespace RISL.Infrastructure.Admin;

/// <inheritdoc cref="IFeedbackService"/>
public sealed class FeedbackService(RislDbContext database) : IFeedbackService
{
    private const int MaxMessageLength = 4000;
    private const int MaxFieldLength = 200;

    public async Task SubmitAsync(string message, string? name, string? contact, CancellationToken cancellationToken = default)
    {
        // Длину режем здесь, а не только в разметке: форму можно отправить и в обход браузера.
        var feedback = new Feedback(
            Truncate(message, MaxMessageLength) ?? string.Empty,
            Truncate(name, MaxFieldLength),
            Truncate(contact, MaxFieldLength));

        database.Feedback.Add(feedback);
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdminFeedback>> ListAsync(bool onlyPending, CancellationToken cancellationToken = default)
    {
        var query = database.Feedback.AsNoTracking();

        if (onlyPending)
        {
            query = query.Where(message => !message.IsHandled);
        }

        return await query
            .OrderByDescending(message => message.CreatedAt)
            .Select(message => new AdminFeedback(
                message.Id,
                message.Name,
                message.Contact,
                message.Message,
                message.CreatedAt,
                message.IsHandled))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> SetHandledAsync(int id, bool isHandled, CancellationToken cancellationToken = default)
    {
        var feedback = await database.Feedback.FirstOrDefaultAsync(message => message.Id == id, cancellationToken);
        if (feedback is null)
        {
            return false;
        }

        feedback.IsHandled = isHandled;
        await database.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var feedback = await database.Feedback.FirstOrDefaultAsync(message => message.Id == id, cancellationToken);
        if (feedback is null)
        {
            return false;
        }

        database.Feedback.Remove(feedback);
        await database.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
