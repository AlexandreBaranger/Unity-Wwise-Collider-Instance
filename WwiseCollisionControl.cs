using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using EventWwise = AK.Wwise.Event;
using System.Collections;
using UnityEngine.Networking;

[System.Serializable]
public class CollisionEventData
{
    public GameObject gameObject;
    public float distance;
    public float speed;
}

[System.Serializable]
public class CSVData
{
    public string Volume;
    public string Parameter;
    public float Value;
    public float MinRandomRange;
    public float MaxRandomRange;
}

[System.Serializable]
public class RTPCConfiguration
{
    public AK.Wwise.RTPC rtpc;
    public string csvFileName;
    public List<KeyValuePair<float, float>> animationData = new List<KeyValuePair<float, float>>();
    public List<float> interpolatedValues = new List<float>();
    public float currentRTPCValue = 0f;
}

public class WwiseCollisionControl : MonoBehaviour
{
    public bool debugCollisions = false;
    public ColliderCollisionHandler collisionHandler;
    public CollisionEvent[] collisionEvents;
    public float collisionCheckInterval = 0.1f;
    private float timeSinceLastCollisionCheck = 0f;

    void Start()
    {
        foreach (CollisionEvent collisionEvent in collisionEvents)
        {
            collisionEvent.CreateColliders();
            collisionEvent.CheckCollisions(collisionHandler);
        }
    }

    void Update()
    {
        timeSinceLastCollisionCheck += Time.deltaTime;
        if (timeSinceLastCollisionCheck >= collisionCheckInterval)
        {
            timeSinceLastCollisionCheck = 0f;
            foreach (CollisionEvent collisionEvent in collisionEvents)
            {
                collisionEvent.CheckCollisions(collisionHandler);
            }
        }
    }

    void OnTriggerExit(Collider other)
    {
        foreach (CollisionEvent collisionEvent in collisionEvents)
        {
            collisionEvent.OnColliderExit(other, collisionHandler);
        }
    }

    void OnTriggerStay(Collider other)
    {
        foreach (CollisionEvent collisionEvent in collisionEvents)
        {
            collisionEvent.OnColliderStay(other, collisionHandler);
        }
    }
}

[System.Serializable]
public class CollisionEvent
{
    public EventWwise wwiseEvent;
    public EventWwise secondWwiseEvent;
    public float delayBeforeSecondEvent = 1.0f;
    public float minDistance;
    public float maxDistance;
    public float minSpeed;
    public float maxSpeed;
    public List<string> csvFileNames = new List<string>();
    public List<GameObject> gameObjectsToSync = new List<GameObject>();
    public LayerMask collisionLayer;
    public float colliderRadius = 1.0f;
    public List<CollisionEventData> collisionDataList = new List<CollisionEventData>();
    public List<CSVData> csvDataList = new List<CSVData>();
    public float delayInSeconds = 5f;
    [SerializeField] public List<RTPCConfiguration> rtpcConfigurations = new List<RTPCConfiguration>();
    [SerializeField] private bool enableDebugLogs = true;
    public EventWwise exitWwiseEvent; 
    public EventWwise stayWwiseEvent; 

    public void CreateColliders()
    {
        foreach (GameObject go in gameObjectsToSync)
        {
            SphereCollider collider = go.AddComponent<SphereCollider>();
            collider.radius = colliderRadius;
            collider.isTrigger = true;
        }
    }

    public void CheckCollisions(ColliderCollisionHandler collisionHandler)
    {
        foreach (GameObject go in gameObjectsToSync)
        {
            Collider[] colliders = Physics.OverlapSphere(go.transform.position, colliderRadius, collisionLayer);
            foreach (Collider other in colliders)
            {
                if (other.gameObject != go)
                {
                    float distance = Vector3.Distance(go.transform.position, other.transform.position);
                    Rigidbody otherRigidbody = other.attachedRigidbody;
                    float speed = (otherRigidbody != null) ? otherRigidbody.velocity.magnitude : 0f;
                    if (distance >= minDistance && distance <= maxDistance &&
                        speed >= minSpeed && speed <= maxSpeed)
                    {
                        Debug.Log($"Collision detected with {other.gameObject.name}: Distance = {distance}, Speed = {speed}");
                        CollisionEventData collisionData = new CollisionEventData
                        {
                            gameObject = other.gameObject,
                            distance = distance,
                            speed = speed
                        };
                        collisionDataList.Add(collisionData);
                        collisionHandler?.HandleCollision(collisionData);

                        AkSoundEngine.RegisterGameObj(go);

                        GameObject.FindObjectOfType<WwiseCollisionControl>().StartCoroutine(LoadCSVAndPostEvent(go));
                    }
                }
            }
        }
    }

    private IEnumerator LoadCSVAndPostEvent(GameObject go)
    {
        yield return LoadCSVs();

        if (go != null)
        {
            wwiseEvent.Post(go);

            if (secondWwiseEvent != null)
            {
                yield return new WaitForSeconds(delayBeforeSecondEvent);
                if (go != null)
                {
                    secondWwiseEvent.Post(go);
                }
            }
        }

        AkSoundEngine.UnregisterGameObj(go);
    }

    private IEnumerator LoadCSVs()
    {
        foreach (string csvFileName in csvFileNames)
        {
            string filePath = Path.Combine(Application.streamingAssetsPath, csvFileName);
            if (File.Exists(filePath))
            {
                yield return LoadCSV(filePath);
            }
            else
            {
                Debug.LogError("CSV file not found at path: " + filePath);
            }
        }
    }

    private IEnumerator LoadCSV(string filePath)
    {
        string[] rows = File.ReadAllLines(filePath);
        yield return null;

        csvDataList.Clear();

        foreach (string row in rows)
        {
            string[] columns = row.Split(',');
            if (columns.Length == 5)
            {
                CSVData data = new CSVData
                {
                    Volume = columns[0].Trim(),
                    Parameter = columns[1].Trim(),
                    MinRandomRange = float.Parse(columns[3].Trim(), CultureInfo.InvariantCulture),
                    MaxRandomRange = float.Parse(columns[4].Trim(), CultureInfo.InvariantCulture)
                };
                float value;
                string valueStr = columns[2].Trim();
                if (valueStr == "0.000000")
                {
                    value = 0.0f;
                }
                else if (float.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                {
                    data.Value = value;
                    SendValueToWwise(data);
                }
                else
                {
                    Debug.LogWarning("Failed to parse value: " + valueStr);
                }
                csvDataList.Add(data);
            }
            else
            {
                Debug.LogWarning("Row format is incorrect: " + row);
            }
        }
    }

    private void InterpolateSegment(float startTime, float endTime, float startValue, float endValue, RTPCConfiguration rtpcConfig)
    {
        int steps = Mathf.CeilToInt((endTime - startTime) * 100);

        for (int j = 0; j <= steps; j++)
        {
            float t = (float)j / steps;
            float interpolatedValue = Mathf.Lerp(startValue, endValue, t);
            rtpcConfig.interpolatedValues.Add(interpolatedValue);
            if (enableDebugLogs) Debug.Log($"Interpolated Value at {startTime + (t * (endTime - startTime))} ms: {interpolatedValue}");
        }
    }

    private void ProcessCSV(string csvText, RTPCConfiguration rtpcConfig)
    {
        string[] lines = csvText.Split('\n');

        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (enableDebugLogs) Debug.Log($"Reading line: {line}");

            string[] values = line.Trim().Split('_');

            if (values.Length >= 2)
            {
                float time, rtpcValue;

                if (float.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out time) &&
                    float.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out rtpcValue))
                {
                    rtpcConfig.animationData.Add(new KeyValuePair<float, float>(time, rtpcValue));
                    if (enableDebugLogs) Debug.Log($"Loaded CSV line - Time: {time}, RTPC Value: {rtpcValue}");
                }
                else
                {
                    if (enableDebugLogs) Debug.LogWarning($"Failed to parse values - Time: {values[0]}, RTPC Value: {values[1]}");
                }
            }
            else
            {
                if (enableDebugLogs) Debug.LogWarning($"Unexpected line format: {line}");
            }
        }

        if (enableDebugLogs) Debug.Log($"Total lines loaded: {rtpcConfig.animationData.Count}");
    }

    private void SendValueToWwise(CSVData data)
    {
        float randomizedValue = UnityEngine.Random.Range(data.Value + data.MinRandomRange, data.Value + data.MaxRandomRange);
        string formattedValue = randomizedValue.ToString("0.000000", CultureInfo.InvariantCulture);
        AkSoundEngine.SetRTPCValue(data.Parameter, float.Parse(formattedValue, CultureInfo.InvariantCulture));
        PlayAllRTPCCurves();
    }

    private IEnumerator CurveRTPC(RTPCConfiguration rtpcConfig)
    {
        float startTime = Time.time;
        int index = 0;

        while (index < rtpcConfig.interpolatedValues.Count)
        {
            float currentTime = Time.time - startTime;
            rtpcConfig.currentRTPCValue = rtpcConfig.interpolatedValues[index];
            if (enableDebugLogs) Debug.Log($"Time: {currentTime}, RTPC Value: {rtpcConfig.currentRTPCValue}");

            index++;
            yield return null;
        }

        if (rtpcConfig.interpolatedValues.Count > 0)
        {
            rtpcConfig.currentRTPCValue = rtpcConfig.interpolatedValues[rtpcConfig.interpolatedValues.Count - 1];
            if (enableDebugLogs) Debug.Log($"Final RTPC Value: {rtpcConfig.currentRTPCValue}");
        }
    }

    private void CalculateInterpolatedValues(RTPCConfiguration rtpcConfig)
    {
        rtpcConfig.interpolatedValues.Clear();

        if (enableDebugLogs) Debug.Log($"animationData.Count: {rtpcConfig.animationData.Count}");

        for (int i = 0; i < rtpcConfig.animationData.Count - 1; i++)
        {
            float startTime = rtpcConfig.animationData[i].Key;
            float endTime = rtpcConfig.animationData[i + 1].Key;
            float startValue = rtpcConfig.animationData[i].Value;
            float endValue = rtpcConfig.animationData[i + 1].Value;

            if (enableDebugLogs) Debug.Log($"startTime: {startTime}, endTime: {endTime}, startValue: {startValue}, endValue: {endValue}");

            if (Mathf.Sign(startValue) != Mathf.Sign(endValue))
            {
                float midTime = (startTime + endTime) / 2f;
                float midValue = Mathf.Abs(startValue) < Mathf.Abs(endValue) ? startValue : endValue;

                InterpolateSegment(startTime, midTime, startValue, midValue, rtpcConfig);
                InterpolateSegment(midTime, endTime, midValue, endValue, rtpcConfig);
            }
            else
            {
                InterpolateSegment(startTime, endTime, startValue, endValue, rtpcConfig);
            }
        }
    }

    private IEnumerator LoadCSVRTPC(RTPCConfiguration rtpcConfig)
    {
        rtpcConfig.animationData.Clear();
        string csvFilePath = Path.Combine(Application.streamingAssetsPath, "Audio", rtpcConfig.csvFileName);

        if (csvFilePath.StartsWith("http://") || csvFilePath.StartsWith("https://"))
        {
            using (UnityWebRequest www = UnityWebRequest.Get(csvFilePath))
            {
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError("Error loading CSV file: " + www.error);
                    yield break;
                }

                string csvText = www.downloadHandler.text;
                ProcessCSV(csvText, rtpcConfig);
            }
        }
        else
        {
            string csvText = File.ReadAllText(csvFilePath);
            ProcessCSV(csvText, rtpcConfig);
        }
    }

    public void PlayAllRTPCCurves()
    {
        LoadAndPlayAllCSVs();
    }

    public IEnumerator LoadAndPlayAllCSVs()
    {
        yield return new WaitForSeconds(delayInSeconds);

        foreach (var rtpcConfig in rtpcConfigurations)
        {
            yield return LoadCSVRTPC(rtpcConfig);
            CalculateInterpolatedValues(rtpcConfig);
            CurveRTPC(rtpcConfig);
        }
    }

    public void OnColliderExit(Collider other, ColliderCollisionHandler collisionHandler)
    {
        foreach (GameObject go in gameObjectsToSync)
        {
            if (other.gameObject != go)
            {
                collisionHandler.HandleCollisionExit(go, other.gameObject);
                if (exitWwiseEvent != null)
                {
                    exitWwiseEvent.Post(go);
                }
            }
        }
    }

    public void OnColliderStay(Collider other, ColliderCollisionHandler collisionHandler)
    {
        foreach (GameObject go in gameObjectsToSync)
        {
            if (other.gameObject != go)
            {
                float distance = Vector3.Distance(go.transform.position, other.transform.position);
                Rigidbody otherRigidbody = other.attachedRigidbody;
                float speed = (otherRigidbody != null) ? otherRigidbody.velocity.magnitude : 0f;
                collisionHandler.HandleCollisionStay(go, other.gameObject, distance, speed);
                if (stayWwiseEvent != null)
                {
                    stayWwiseEvent.Post(go);
                }
            }
        }
    }
}
    
[System.Serializable]
public class ColliderCollisionHandler : MonoBehaviour
{
    public void HandleCollision(CollisionEventData collisionData)
    {
        Debug.Log("Collision detected with: " + collisionData.gameObject.name);
        Debug.Log("Distance: " + collisionData.distance);
        Debug.Log("Speed: " + collisionData.speed);
    }
    public void HandleCollisionExit(GameObject go, GameObject other)
    {

    }

    public void HandleCollisionStay(GameObject go, GameObject other, float distance, float speed)
    {

    }
}
