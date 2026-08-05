using UnityEngine;

public class Lumina_Custom_Audio : MonoBehaviour
{
    public AudioSource uLipsync_Audiosource;
    public AudioSource customAudiosource;

    public AudioClip LuminaAudioClip_Open;
    public AudioClip[] LuminaAudioClips_Tossing;
    public AudioClip[] LuminaAudioClips_TossingFailed;
    public AudioClip[] LuminaAudioClips_End;
    public void PlayCustomAudio(AudioClip clip)
    {
        customAudiosource.clip = clip;
        customAudiosource.Play();
        uLipsync_Audiosource.clip = clip;
        uLipsync_Audiosource.Play();
        uLipsync_Audiosource.loop = false;
    }

    public void AudioStop()
    {
        uLipsync_Audiosource.Stop();
        customAudiosource.Stop();
    }
}
