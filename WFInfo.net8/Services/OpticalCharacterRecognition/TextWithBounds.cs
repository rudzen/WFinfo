using System.Drawing;

namespace WFInfo.Services.OpticalCharacterRecognition;

public sealed record TextWithBounds(string Text, Rectangle Bounds);
