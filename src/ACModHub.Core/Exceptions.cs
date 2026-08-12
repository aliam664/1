namespace ACModHub.Core;

public class ModHubException : Exception
{
    public ModHubException(string message) : base(message) { }
    public ModHubException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class UnsafeArchiveException : ModHubException
{
    public UnsafeArchiveException(string message) : base(message) { }
}

public sealed class InstallationException : ModHubException
{
    public InstallationException(string message) : base(message) { }
    public InstallationException(string message, Exception innerException) : base(message, innerException) { }
}
