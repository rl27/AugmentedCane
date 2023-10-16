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
    private static string OVERPASS_PREFIX = "https://overpass-api.de/api/interpreter?data=[bbox:";
    private static string OVERPASS_SUFFIX = "][out:csv(::type,::lat,::lon,'name')];way['highway'~'^(trunk|primary|secondary|tertiary|unclassified|residential)$'];node(way_link:3-);foreach->.c{.c;out;way(bn);out;}";
    private static string apiKey;
    private static bool apiKeyInitialized = false;

    void Awake()
    {
        string apikeyPath = Path.Combine(Application.streamingAssetsPath, "apikey.txt");
        StartCoroutine(GetAPIKey(apikeyPath));
    }

    private IEnumerator GetAPIKey(string apikeyPath)
    {
        #if UNITY_ANDROID
            UnityWebRequest webRequest = UnityWebRequest.Get(apikeyPath);
            yield return webRequest.SendWebRequest();
            if (checkStatus(webRequest, apikeyPath.Split('/')))
                apiKey = webRequest.downloadHandler.text;
        #else
            var sr = new StreamReader(apikeyPath);
            apiKey = sr.ReadLine();
            sr.Close();
        #endif
        apiKeyInitialized = true;
        yield break;
    }

    // Find nearby intersections
    // Below is a request you can try inputting to overpass turbo (https://overpass-turbo.eu/)
    // Also see: https://wiki.openstreetmap.org/wiki/Overpass_API/Overpass_API_by_Example#Search_for_street_intersections
    /*[out:csv(::type,::lat,::lon,'name')][bbox:{{bbox}}];
    way['highway'~'^(trunk|primary|secondary|tertiary|unclassified|residential)$'];
    node(way_link:3-);
    foreach->.c(
      way(bn.c);
      if (u(t['name']) == '< multiple values found >') {
        .c;
        out;
        way(bn);
        out;
      }
    );*/
    public static IEnumerator SendOverpassRequest(Navigation.Point bottomLeft, Navigation.Point topRight, Action<string> callback)
    {
        string url = String.Format("{0}{1},{2},{3},{4}{5}", OVERPASS_PREFIX, bottomLeft.lat, bottomLeft.lng, topRight.lat, topRight.lng, OVERPASS_SUFFIX);

        using (UnityWebRequest webRequest = new UnityWebRequest(url, "GET"))
        {
            webRequest.downloadHandler = (DownloadHandler) new DownloadHandlerBuffer();
            // webRequest.SetRequestHeader("Content-Type", "text/csv");
            yield return webRequest.SendWebRequest();
            if (checkStatus(webRequest, url.Split('/'))) {
                callback(webRequest.downloadHandler.text);
            }
        }
    }

    public static IEnumerator SendLogData(Dictionary<string, dynamic> coords)
    {
        string url = "raymondl.pythonanywhere.com";

        using (UnityWebRequest webRequest = new UnityWebRequest(url, "POST"))
        {
            byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(JsonConvert.SerializeObject(coords));
            webRequest.uploadHandler = (UploadHandler) new UploadHandlerRaw(jsonToSend);
            webRequest.downloadHandler = (DownloadHandler) new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", "application/json");
            yield return webRequest.SendWebRequest();
            if (checkStatus(webRequest, url.Split('/'))) {
                Debug.Log("log success");
            }
        }
    }

    public static IEnumerator SendRouteRequest(Navigation.Point start, Navigation.Point end, Action<JObject> callback)
    {
        JObject request = ConstructRouteRequest(start, end);

        string url = ROUTE_SERVER_URL;

        yield return new WaitUntil(() => apiKeyInitialized);

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
    private static JObject ConstructRouteRequest(Navigation.Point start, Navigation.Point end)
    {
        JObject destinationObject = new JObject();
        if (!start.isAddress && !end.isAddress) {
            destinationObject =
            new JObject(
                new JProperty("location",
                    new JObject(
                        new JProperty("latLng",
                            new JObject(
                                new JProperty("latitude", end.lat),
                                new JProperty("longitude", end.lng)
                            )
                        )
                    )
                )
            );
        }
        else if (!start.isAddress && end.isAddress) {
            destinationObject = new JObject(new JProperty("address", end.address));
        }

        return
            new JObject(
                new JProperty("origin",
                    new JObject(
                        new JProperty("location",
                            new JObject(
                                new JProperty("latLng",
                                    new JObject(
                                        new JProperty("latitude", start.lat),
                                        new JProperty("longitude", start.lng)
                                    )
                                )
                            )
                        )
                    )
                ),
                new JProperty("destination",
                    destinationObject
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

        yield return new WaitUntil(() => apiKeyInitialized);

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
    public static bool checkStatus(UnityWebRequest webRequest, string[] pages)
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
