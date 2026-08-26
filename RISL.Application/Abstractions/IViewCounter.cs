namespace RISL.Application.Abstractions;

/// <summary>
/// Счётчик просмотров слов.
/// </summary>
/// <remarks>
/// Инкременты копятся в памяти и сбрасываются в базу пачкой раз в минуту: писать
/// в SQLite на каждое открытие страницы — это лишняя запись на ровном месте
/// и постоянная инвалидация снимка словаря.
/// </remarks>
public interface IViewCounter
{
    /// <summary>Отмечает один просмотр слова.</summary>
    void Register(int wordId);

    /// <summary>Забирает накопленное и обнуляет счётчики. Вызывается фоновым сбросом.</summary>
    IReadOnlyDictionary<int, int> DrainPending();
}
