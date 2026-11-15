namespace CovertAction.Msc;

/// <summary>
/// The original would read a local variable here that it never set: what it gets is whatever the stack happens to
/// hold, which no rewrite can know. The rewrite follows the original up to this point and then stops with this
/// exception instead of inventing a value. Real play does not come here; each place that can throw it says under
/// which state it would.
/// </summary>
/// <param name="what">The variable and the state in which it is unset.</param>
public sealed class UninitialisedReadException(string what) : Exception($"the original reads an uninitialised variable here: {what}");
