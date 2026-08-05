using UnityEngine;
using UnityEngine.Video;

public class VideoController : MonoBehaviour
{
    public static VideoController Instance;
    public VideoPlayer player;

    void Awake()
    {
        Instance = this;
    }

    public void Play()
    {
        if (!player.isPlaying)
            player.Play();
    }

    public void Pause()
    {
        if (player.isPlaying)
            player.Pause();
    }
}
