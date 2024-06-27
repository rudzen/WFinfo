using WFInfo.Services.HDRDetection.Schemes;

namespace WFInfo.Services.HDRDetection;

public sealed class SchemeHdrDetector(IEnumerable<IHDRDetectionScheme> schemes) : IHDRDetectorService
{
    private readonly List<IHDRDetectionScheme> _schemes = schemes.ToList();

    private bool _hasRun;
    private bool _isHdr;

    public bool IsHdr()
    {
        return !_hasRun ? IsDetected() : _isHdr;
    }

    private bool IsDetected()
    {
        var run = false;
        foreach (var scheme in _schemes)
        {
            var result = scheme.Detect();
            if (result.IsGuaranteed)
            {
                run = result.IsDetected;
                break;
            }
        }

        _hasRun = true;
        _isHdr = run;

        return run;
    }
}
