using System.Drawing;

namespace WFInfo.Services.OpticalCharacterRecognition;

public interface IRewardSelector
{
    int GetSelectedReward(
        ref Point lastClick,
        in double uiScaling,
        int numberOfRewardsDisplayed,
        bool dumpToImage = false
    );
}
