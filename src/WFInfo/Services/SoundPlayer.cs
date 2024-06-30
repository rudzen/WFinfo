using System.Reflection;

namespace WFInfo.Services;

public sealed class SoundPlayer : ISoundPlayer
{
    private readonly System.Media.SoundPlayer _player;

    public SoundPlayer()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var audioStream = assembly.GetManifestResourceStream("WFInfo.Resources.achievment_03.wav");
        _player = new System.Media.SoundPlayer(audioStream);
    }

    public void Play()
    {
        _player.Play();
    }

    public void Dispose()
    {
        _player.Dispose();
    }
}
