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

    Queue<AudioClip> audioToPlay = new Queue<AudioClip>();

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        audioFilePath = Application.persistentDataPath + "/audio.mp3";

        // string text = "Head northwest on Ivy Circle toward N Harvard Blvd";
        // RequestTTS(text);
    }

    void Update()
    {
        if (!audioSource.isPlaying && audioToPlay.Count > 0) {
            audioSource.clip = audioToPlay.Dequeue();
            audioSource.Play();
        }
    }

    // Get TTS audio and play it
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

    // Load local audio file to queue
    IEnumerator LoadAudio()
    {
        var dh = new DownloadHandlerAudioClip(audioFilePath, AudioType.MPEG);
        dh.compressed = true;
     
        using (UnityWebRequest webRequest = new UnityWebRequest(audioFilePath, "GET", dh, null)) {
            yield return webRequest.SendWebRequest();
            if (webRequest.responseCode == 200) {
                audioToPlay.Enqueue(dh.audioClip);
            }
        }
    }
}
