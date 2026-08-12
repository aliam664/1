using System.Net.Http;
using ACModHub.Core;
using ACModHub.Core.Interfaces;

namespace ACModHub.Infrastructure.Services;

public sealed class UserErrorMessageService : IUserErrorMessageService
{
    public string ToUserMessage(Exception exception, string operation)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var root = Unwrap(exception);
        return root switch
        {
            UnauthorizedAccessException => $"{operation} failed because the selected game directory is not writable. Check the Steam library permissions or choose a writable installation. AC Mod Hub will not elevate silently.",
            FileNotFoundException file => $"{operation} failed because a required file could not be found: {Path.GetFileName(file.FileName)}.",
            DirectoryNotFoundException => $"{operation} failed because the selected game or working directory no longer exists.",
            UnsafeArchiveException unsafeArchive => $"The mod package was blocked by archive security validation: {unsafeArchive.Message}",
            InvalidDataException => "The downloaded package is incomplete, corrupt, or not a supported archive.",
            HttpRequestException => "The download server could not be reached or returned an invalid response. Check the URL and try again.",
            OperationCanceledException => $"{operation} was cancelled safely.",
            IOException io when IsSharingViolation(io) => $"{operation} could not continue because a game file is locked. Close Assetto Corsa and related tools, then retry.",
            IOException => $"{operation} failed during a file operation. Check free disk space, path validity, and directory permissions.",
            ModHubException => root.Message,
            _ => $"{operation} failed unexpectedly. Details were written to the application log."
        };
    }

    private static Exception Unwrap(Exception exception)
    {
        while (exception is AggregateException { InnerExceptions.Count: 1 } aggregate) exception = aggregate.InnerExceptions[0];
        return exception.InnerException is not null && exception is not ModHubException ? exception.InnerException : exception;
    }

    private static bool IsSharingViolation(IOException exception)
    {
        var code = exception.HResult & 0xFFFF;
        return code is 32 or 33;
    }
}
