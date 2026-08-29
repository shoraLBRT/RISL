using System.Collections.Concurrent;
using RISL.Application.Abstractions;

namespace RISL.Infrastructure.Catalog;

/// <summary>
/// Копит просмотры в памяти между сбросами в базу.
/// </summary>
/// <remarks>
/// Точность здесь не важна: при аварийной остановке теряется не больше минуты
/// счётчиков, зато открытие страницы слова остаётся операцией чтения без единой записи.
/// </remarks>
public sealed class ViewCounter : IViewCounter
{
    private ConcurrentDictionary<int, int> _pending = new();

    public void Register(int wordId) =>
        _pending.AddOrUpdate(wordId, 1, static (_, current) => current + 1);

    public IReadOnlyDictionary<int, int> DrainPending()
    {
        // Подменяем словарь целиком: параллельные Register уйдут уже в новый,
        // и ни один просмотр не потеряется между чтением и очисткой.
        var drained = Interlocked.Exchange(ref _pending, new ConcurrentDictionary<int, int>());
        return drained;
    }
}
