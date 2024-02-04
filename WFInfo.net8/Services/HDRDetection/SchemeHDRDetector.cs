using WFInfo.Services.HDRDetection.Schemes;

namespace WFInfo.Services.HDRDetection;

public sealed class SchemeHDRDetector(IEnumerable<IHDRDetectionScheme> schemes) : IHDRDetectorService
{
    private readonly List<IHDRDetectionScheme> _schemes = schemes.ToList();

    public bool IsHdr()
    {
        foreach (var scheme in _schemes)
        {
            var result = scheme.Detect();
            if (result.IsGuaranteed)
                return result.IsDetected;
        }

        return false;
    }
}