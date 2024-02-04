namespace WFInfo.Services.HDRDetection.Schemes;

/// <summary>
/// Result of a HDR detection scheme
/// <para>
/// IsDetected: Whether the scheme detected a possibility of HDR being enabled
/// </para>
/// <para>
/// Whether the scheme guarantees that <see cref="IsDetected"/> is the true value. E.g. if a user has disabled HDR in warframe they can still have Auto HDR on.
/// </para>
/// </summary>
public sealed record HDRDetectionSchemeResult(bool IsDetected, bool IsGuaranteed);

/// <summary>
/// Determines whether HDR could be enabled from a single source
/// </summary>
public interface IHDRDetectionScheme
{
    HDRDetectionSchemeResult Detect();
}