namespace MicroORM.Dialects;

/// <summary>A part of a date or time value that <see cref="SqlDialect.DatePart"/> extracts.</summary>
public enum DatePart
{
    /// <summary>The year.</summary>
    Year,

    /// <summary>The month, from 1 to 12.</summary>
    Month,

    /// <summary>The day of the month, from 1 to 31.</summary>
    Day,

    /// <summary>The hour, from 0 to 23.</summary>
    Hour,

    /// <summary>The minute, from 0 to 59.</summary>
    Minute,

    /// <summary>The whole second, from 0 to 59.</summary>
    Second,
}
