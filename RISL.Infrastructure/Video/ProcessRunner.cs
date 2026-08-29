using System.Diagnostics;
using System.Text;

namespace RISL.Infrastructure.Video;

/// <param name="ExitCode">Код возврата; ноль означает успех.</param>
/// <param name="StandardOutput">Всё, что процесс написал в stdout.</param>
/// <param name="StandardError">Всё, что процесс написал в stderr — ffmpeg пишет туда и обычный лог.</param>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool IsSuccess => ExitCode == 0;

    /// <summary>Последние строки stderr — то, что имеет смысл показать админу как причину отказа.</summary>
    public string ShortError(int maxLength = 1000)
    {
        var text = StandardError.Trim();
        if (text.Length == 0)
        {
            text = $"Процесс завершился с кодом {ExitCode}.";
        }

        return text.Length <= maxLength ? text : text[^maxLength..];
    }
}

/// <summary>Запуск внешних процессов ffmpeg и ffprobe с чтением их вывода.</summary>
internal static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            // ArgumentList экранирует значения сам — склеивать строку вручную нельзя,
            // иначе пробел в пути превратится в лишний аргумент.
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();
        var error = new StringBuilder();

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                output.AppendLine(args.Data);
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                error.AppendLine(args.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return new ProcessResult(-1, output.ToString(), $"Процесс не уложился в {timeout.TotalSeconds:0} с и был остановлен.");
        }

        return new ProcessResult(process.ExitCode, output.ToString(), error.ToString());
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Процесс успел завершиться сам — ничего делать не нужно.
        }
    }
}
