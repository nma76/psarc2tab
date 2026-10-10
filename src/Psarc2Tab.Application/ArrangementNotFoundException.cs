namespace Psarc2Tab.Application;

/// <summary>Reports that a requested numbered arrangement is unavailable in an archive.</summary>
public sealed class ArrangementNotFoundException : Exception
{
    /// <summary>Creates an exception for an unavailable arrangement number.</summary>
    /// <param name="arrangementNumber">The one-based number that was requested.</param>
    /// <param name="innerException">The lower-level selection failure.</param>
    public ArrangementNotFoundException(int arrangementNumber, Exception? innerException = null)
        : base($"Arrangement {arrangementNumber} is not available in this archive.", innerException)
    {
        ArrangementNumber = arrangementNumber;
    }

    /// <summary>Gets the unavailable arrangement number.</summary>
    public int ArrangementNumber { get; }
}
