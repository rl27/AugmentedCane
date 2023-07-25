using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class AudioPlayer : MonoBehaviour
{
    AudioSource audioSource;

    public AudioClip left;
    public AudioClip right;

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
    }

    public void PlayLeft()
    {
        audioSource.PlayOneShot(left, 1);
    }

    public void PlayRight()
    {
        audioSource.PlayOneShot(right, 1);
    }
}
