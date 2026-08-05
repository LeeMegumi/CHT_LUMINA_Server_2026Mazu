using UnityEngine;

public class AudioPlayer : MonoBehaviour
{
    public static AudioPlayer instance {  get; private set; }
    private AudioSource m_AudioSource;
    public AudioClip[] audioClips;

    private void Awake()
    {
        if(instance == null)
            instance = this;
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        m_AudioSource = GetComponent<AudioSource>();
    }
    public void PlayAudio(int index, ulong delaytime = 0)
    {
        m_AudioSource.clip = audioClips[index];
        m_AudioSource.Play(delaytime);
    }


}
