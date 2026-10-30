namespace ShellyHwinfoBridge.Core.Curves;

/// <summary>The kind of problem found by <see cref="CurveValidator"/>.</summary>
public enum CurveValidationErrorCode
{
    /// <summary>The curve has fewer than <see cref="CurveValidator.MinimumPoints"/> rows.</summary>
    TooFewPoints,

    /// <summary>A value is NaN or infinite.</summary>
    NotFinite,

    /// <summary>The AC value is not strictly greater than the previous row's AC value.</summary>
    AcNotStrictlyIncreasing,

    /// <summary>The DC value is zero or negative.</summary>
    DcNotPositive,

    /// <summary>The DC value is not strictly less than the AC value.</summary>
    DcNotBelowAc,
}

/// <summary>A single curve validation problem.</summary>
/// <param name="Code">The kind of problem.</param>
/// <param name="RowIndex">Zero-based row index, or <c>null</c> when the problem concerns the whole curve.</param>
/// <param name="Message">A human-readable description (row numbers in it are 1-based).</param>
public sealed record CurveValidationError(CurveValidationErrorCode Code, int? RowIndex, string Message);

/// <summary>Thrown when a <see cref="PsuCurve"/> is created from invalid points.</summary>
public sealed class CurveValidationException : ArgumentException
{
    /// <summary>Initializes a new instance with the given errors.</summary>
    /// <param name="errors">The validation errors; must not be empty.</param>
    public CurveValidationException(IReadOnlyList<CurveValidationError> errors)
        : base("Invalid PSU curve: " + string.Join(" ", errors.Select(e => e.Message)))
    {
        Errors = errors;
    }

    /// <summary>Gets the validation errors.</summary>
    public IReadOnlyList<CurveValidationError> Errors { get; }
}
