using RISL.Application.Admin;
using RISL.Application.Import;

namespace RISL.Application.Abstractions;

/// <summary>Работа со словами из панели администратора.</summary>
public interface IWordAdminService
{
    Task<AdminWordPage> ListAsync(AdminWordQuery query, CancellationToken cancellationToken = default);

    Task<AdminWordDetails?> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Создаёт слово. Если приложен исходник, ставит его в очередь на обработку.</summary>
    Task<AdminWordSaveResult> CreateAsync(AdminWordForm form, CancellationToken cancellationToken = default);

    Task<AdminWordSaveResult> UpdateAsync(int id, AdminWordForm form, CancellationToken cancellationToken = default);

    /// <summary>Удаляет слово вместе со всеми его файлами.</summary>
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Повторяет обработку по ранее загруженному исходнику после неудачи.</summary>
    Task<AdminWordSaveResult> RetryVideoAsync(int id, CancellationToken cancellationToken = default);

    Task<AdminDashboard> GetDashboardAsync(CancellationToken cancellationToken = default);
}

/// <summary>Работа с темами словаря.</summary>
public interface ICategoryAdminService
{
    Task<IReadOnlyList<AdminCategory>> ListAsync(CancellationToken cancellationToken = default);

    Task<AdminWordSaveResult> CreateAsync(string name, int sortOrder, CancellationToken cancellationToken = default);

    Task<AdminWordSaveResult> UpdateAsync(int id, string name, int sortOrder, CancellationToken cancellationToken = default);

    /// <summary>Удаляет тему. Слова остаются, теряя привязку к ней.</summary>
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}

/// <summary>Приём и разбор сообщений от гостей.</summary>
public interface IFeedbackService
{
    Task SubmitAsync(string message, string? name, string? contact, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminFeedback>> ListAsync(bool onlyPending, CancellationToken cancellationToken = default);

    Task<bool> SetHandledAsync(int id, bool isHandled, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}

/// <summary>Массовый импорт словаря из CSV и архива с видео.</summary>
public interface IImportService
{
    /// <summary>
    /// Разбирает файлы и сохраняет план. Ничего не пишет в словарь: админ сначала
    /// видит отчёт и только потом подтверждает применение.
    /// </summary>
    Task<int> PrepareAsync(ImportSource source, CancellationToken cancellationToken = default);

    Task<ImportJobView?> GetAsync(int jobId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImportJobView>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Применяет ранее подготовленный план одной транзакцией.</summary>
    Task<ImportJobView?> ApplyAsync(int jobId, CancellationToken cancellationToken = default);

    /// <summary>Отменяет задание и убирает распакованные файлы.</summary>
    Task<bool> CancelAsync(int jobId, CancellationToken cancellationToken = default);
}
