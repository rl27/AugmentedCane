using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[RequireComponent(typeof(AudioSource))]
public class TTS : MonoBehaviour
{
    AudioSource audioSource;
    private string audioFilePath;

    // If an audio clip is older than timeToDiscard, it is discarded.
    private double timeToDiscard = 999.0; // Get rid of this? If user stands in front of obstacle for too long they may miss an instruction.

    private struct AudioWithTime {
        public AudioClip audioClip;
        public DateTime timeAdded;
        public AudioWithTime(AudioClip ac) {
            audioClip = ac;
            timeAdded = DateTime.Now;
        }
    }

    private Queue<AudioWithTime> audioToPlay = new Queue<AudioWithTime>();

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        audioFilePath = Path.Combine(Application.persistentDataPath, "audio.mp3");

        // Testing - speak some text
        // string text = "Head northwest on Ivy Circle toward N Harvard Blvd";
        // RequestTTS(text);
    }

    void Update()
    {
        if (DepthImage.direction == DepthImage.Direction.None && !audioSource.isPlaying && audioToPlay.Count > 0) {
            var at = audioToPlay.Dequeue();
            if ((DateTime.Now - at.timeAdded).TotalSeconds > timeToDiscard)
                return;
            audioSource.clip = at.audioClip;
            audioSource.Play();
        }
    }

    // Get TTS audio, convert it (string -> bytes -> MP3 -> AudioClip) and queue it up to be played
    public void RequestTTS(string text)
    {
        StartCoroutine(WebClient.SendTTSRequest(text,
            response => {
                string audioText = (string) response["audioContent"];
                byte[] bytes = Convert.FromBase64String(audioText);
                File.WriteAllBytes(audioFilePath, bytes);
                StartCoroutine(LoadAudio());
            })
        );
    }

    // Queue up audio clip
    public void EnqueueTTS(AudioClip audioClip)
    {
        audioToPlay.Enqueue(new AudioWithTime(audioClip));
    }

    // Load local audio file to queue
    IEnumerator LoadAudio()
    {
        string uri = "file://" + audioFilePath;
        using (UnityWebRequest webRequest = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.MPEG))
        {
            yield return webRequest.SendWebRequest();
            // if (webRequest.responseCode == 200) 
            if (WebClient.checkStatus(webRequest, uri.Split('/')))
                audioToPlay.Enqueue(new AudioWithTime(DownloadHandlerAudioClip.GetContent(webRequest)));
        }
    }
}
