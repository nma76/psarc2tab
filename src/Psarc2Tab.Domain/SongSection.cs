namespace Psarc2Tab.Domain;

/// <summary>Represents a named section marker on the song timeline.</summary>
public sealed record SongSection
{
    /// <summary>Creates a named section marker.</summary>
    /// <param name="name">The section label from the source chart.</param>
    /// <param name="number">The source section number.</param>
    /// <param name="startTime">The section start time from the beginning of the song.</param>
    /// <exception cref="ArgumentException">The section name is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The start time is negative.</exception>
    public SongSection(string name, int number, TimeSpan startTime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (startTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(startTime), startTime, "A section start time cannot be negative.");
        }

        Name = name;
        Number = number;
        StartTime = startTime;
    }

    /// <summary>Gets the section label.</summary>
    public string Name { get; }

    /// <summary>Gets the source section number.</summary>
    public int Number { get; }

    /// <summary>Gets the section start time from the beginning of the song.</summary>
    public TimeSpan StartTime { get; }
}
