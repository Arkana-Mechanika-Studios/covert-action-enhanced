namespace CovertAction.Msc;

/// <summary>
/// The processor's divide error (interrupt 0): a division by zero, or a signed 16-bit quotient that does not fit
/// (-32768 / -1). The original program stops there with the run-time message "R6003 integer divide by 0"; a port that
/// reaches it has met a state the original could not survive either, so it is an exception and never a silent value.
/// </summary>
public sealed class DivideErrorException(string message) : Exception(message);
