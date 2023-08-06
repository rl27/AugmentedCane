using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class WebClient : MonoBehaviour
{
    private static string ROUTE_SERVER_URL = "https://routes.googleapis.com/directions/v2:computeRoutes";
    private static string TTS_SERVER_URL = "https://texttospeech.googleapis.com/v1beta1/text:synthesize";
    private static string apiKey;

    void Awake()
    {
        var sr = new StreamReader("Assets/Resources/apikey.txt");
        apiKey = sr.ReadLine();
        sr.Close();
    }

    public static IEnumerator SendRouteRequest(double startLat, double startLng, double endLat, double endLng, Action<JObject> callback)
    {
        JObject request = ConstructRouteRequest(startLat, startLng, endLat, endLng);

        string url = ROUTE_SERVER_URL;

        using (UnityWebRequest webRequest = new UnityWebRequest(url, "POST"))
        {   
            // Request and wait for the desired page.
            byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(request.ToString());
            webRequest.uploadHandler = (UploadHandler) new UploadHandlerRaw(jsonToSend);
            webRequest.downloadHandler = (DownloadHandler) new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", "application/json");
            webRequest.SetRequestHeader("X-Goog-Api-Key", apiKey);
            webRequest.SetRequestHeader("X-Goog-FieldMask", "routes.legs.distanceMeters,routes.legs.duration,routes.legs.polyline,routes.legs.steps");
            yield return webRequest.SendWebRequest();

            if (checkStatus(webRequest, url.Split('/'))) {
                var response = JObject.Parse(webRequest.downloadHandler.text);
                callback(response);
            }
        }
    }

    // https://developers.google.com/maps/documentation/routes/compute_route_directions
    // https://developers.google.com/maps/documentation/routes/reference/rest/v2/TopLevel/computeRoutes
    private static JObject ConstructRouteRequest(double startLat, double startLng, double endLat, double endLng)
    {
        return
            new JObject(
                new JProperty("origin",
                    new JObject(
                        new JProperty("location",
                            new JObject(
                                new JProperty("latLng",
                                    new JObject(
                                        new JProperty("latitude", startLat),
                                        new JProperty("longitude", startLng)
                                    )
                                )
                            )
                        )
                    )
                ),
                new JProperty("destination",
                    new JObject(
                        new JProperty("location",
                            new JObject(
                                new JProperty("latLng",
                                    new JObject(
                                        new JProperty("latitude", endLat),
                                        new JProperty("longitude", endLng)
                                    )
                                )
                            )
                        )
                    )
                ),
                new JProperty("travelMode", "WALK"),
                new JProperty("polylineQuality", "OVERVIEW"), // "HIGH_QUALITY"
                new JProperty("computeAlternativeRoutes", false),
                new JProperty("routeModifiers",
                    new JObject(
                        new JProperty("avoidTolls", false),
                        new JProperty("avoidHighways", false),
                        new JProperty("avoidFerries", false)
                    )
                ),
                new JProperty("languageCode", "en-US"),
                new JProperty("units", "METRIC")
            );
    }

    public static IEnumerator SendTTSRequest(string text, Action<JObject> callback)
    {
        JObject request = ConstructTTSRequest(text);

        string url = TTS_SERVER_URL;

        using (UnityWebRequest webRequest = new UnityWebRequest(url, "POST"))
        {   
            // Request and wait for the desired page.
            byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(request.ToString());
            webRequest.uploadHandler = (UploadHandler) new UploadHandlerRaw(jsonToSend);
            webRequest.downloadHandler = (DownloadHandler) new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", "application/json");
            webRequest.SetRequestHeader("X-Goog-Api-Key", apiKey);
            yield return webRequest.SendWebRequest();

            if (checkStatus(webRequest, url.Split('/'))) {
                var response = JObject.Parse(webRequest.downloadHandler.text);
                callback(response);
            }
        }
    }

    // https://cloud.google.com/text-to-speech/docs/reference/rest/v1beta1/text/synthesize
    private static JObject ConstructTTSRequest(string text)
    {
        return
            new JObject(
                new JProperty("input",
                    new JObject(
                        new JProperty("text", text)
                    )
                ),
                new JProperty("voice",
                    new JObject(
                        new JProperty("ssmlGender", "NEUTRAL"),
                        new JProperty("languageCode", "en-US")
                    )
                ),
                new JProperty("audioConfig",
                    new JObject(
                        new JProperty("audioEncoding", "MP3"),
                        new JProperty("speakingRate", 1.25)
                    )
                )
            );
    }

    // Returns true on success, false on fail.
    private static bool checkStatus(UnityWebRequest webRequest, string[] pages)
    {
        int page = pages.Length - 1;
        switch (webRequest.result)
        {
            case UnityWebRequest.Result.ConnectionError:
                Debug.LogError(pages[page] + ": Error: " + webRequest.error);
                return false;
            case UnityWebRequest.Result.DataProcessingError:
                Debug.LogError(pages[page] + ": Error: " + webRequest.error);
                return false;
            case UnityWebRequest.Result.ProtocolError:
                Debug.LogError(pages[page] + ": HTTP Error: " + webRequest.error);
                return false;
            case UnityWebRequest.Result.Success:
                // Debug.Log(pages[page] + ":\nReceived: " + webRequest.downloadHandler.text);
                return true;
        }
        return false;
    }
}
