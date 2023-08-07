using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class AudioPlayer : MonoBehaviour
{
    AudioSource audioSource;

    public AudioClip collision;

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
    }

    public void PlayCollision()
    {
        if (!audioSource.isPlaying)
            audioSource.PlayOneShot(collision, 1);
    }
}
