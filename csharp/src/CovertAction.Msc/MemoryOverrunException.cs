namespace CovertAction.Msc;

/// <summary>
/// A C string routine would have read or written past the buffer the port handed it. The original has no such check:
/// it runs on into whatever lies behind the buffer in the data segment. A port whose behaviour depends on that (a text
/// without its end mark, a copy longer than its target) must hand the routine the bytes the original really reaches,
/// in the original's order, and say so with a <c>// C:</c> comment; everywhere else this exception shows the mistake
/// instead of hiding it.
/// </summary>
public sealed class MemoryOverrunException(string message) : Exception(message);
