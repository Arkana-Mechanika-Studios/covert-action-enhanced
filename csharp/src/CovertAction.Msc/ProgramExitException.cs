namespace CovertAction.Msc;

/// <summary>
/// The program ended itself: the original called the C library's <c>exit()</c> from the middle of its code (the
/// player confirmed "Quit"). A function that calls <c>exit</c> never returns in the original; in the rewrite the call
/// throws this exception, which unwinds to whoever started the module.
/// </summary>
/// <param name="code">The exit code handed to DOS.</param>
public sealed class ProgramExitException(short code) : Exception($"the program ended with exit code {code}")
{
    /// <summary>The exit code handed to DOS.</summary>
    public short Code { get; } = code;
}
