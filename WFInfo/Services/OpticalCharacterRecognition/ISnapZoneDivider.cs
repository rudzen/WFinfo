using System.Drawing;

namespace WFInfo.Services.OpticalCharacterRecognition;

public interface ISnapZoneDivider
{
    SnapZone[] DivideSnapZones(
        Bitmap filteredImage,
        Bitmap filteredImageClean,
        int[] rowHits,
        int[] colHits);
}
