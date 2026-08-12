using ACModHub.Infrastructure.Services;

namespace ACModHub.Tests.Infrastructure;

public sealed class ErrorMessageTests
{
    [Fact]
    public void PermissionError_IsConvertedToActionableMessage()
    {
        var message = new UserErrorMessageService().ToUserMessage(new UnauthorizedAccessException("raw EACCES details"), "Installation");
        Assert.Contains("not writable", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EACCES", message, StringComparison.OrdinalIgnoreCase);
    }
}
