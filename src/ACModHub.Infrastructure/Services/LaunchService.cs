using System.Diagnostics;
using ACModHub.Core;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;

namespace ACModHub.Infrastructure.Services;

public sealed class LaunchService : ILaunchService
{
    public Task LaunchAsync(GameInstallation installation, bool throughSteam, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);
        cancellationToken.ThrowIfCancellationRequested();
        if (!installation.IsValid) throw new ModHubException(installation.ValidationMessage ?? "The selected game installation is invalid.");

        var startInfo = throughSteam
            ? new ProcessStartInfo("steam://rungameid/244210") { UseShellExecute = true }
            : new ProcessStartInfo(installation.ExecutablePath) { UseShellExecute = true, WorkingDirectory = installation.RootPath };
        if (Process.Start(startInfo) is null) throw new ModHubException("Assetto Corsa could not be started.");
        return Task.CompletedTask;
    }
}
