using UnityEngine;
using NativeWebSocket;
using System.Collections;
using Newtonsoft.Json.Linq;

public class UnityReceiver_Native : MonoBehaviour
{
    WebSocket ws;
    public GameObject ball;
    public Transform rimTarget;  // 🏀 assign your hoop rim here
    public float moveSmooth = 8f;
    public float shotSpeed = 10f;
    public float dropAfterShot = 0.4f;

    bool isConnecting = false;
    bool isScoring = false;
    Vector3 targetPos;

    void Start()
    {
        StartCoroutine(AutoConnectLoop());
    }

    IEnumerator AutoConnectLoop()
    {
        while (true)
        {
            if (ws == null || ws.State != WebSocketState.Open)
            {
                if (!isConnecting)
                {
                    isConnecting = true;
                    Debug.Log("🔄 Trying to connect to Python WebSocket...");
                    ConnectToPython();
                }
            }
            yield return new WaitForSeconds(2f);
        }
    }

    async void ConnectToPython()
    {
        ws = new WebSocket("ws://localhost:8765");

        ws.OnOpen += () =>
        {
            Debug.Log("✅ Connected to Python WebSocket");
            isConnecting = false;
        };

        ws.OnMessage += (bytes) =>
        {
            string message = System.Text.Encoding.UTF8.GetString(bytes);
            JObject data = JObject.Parse(message);
            HandlePoseData(data);
        };

        ws.OnError += (e) =>
        {
            Debug.LogError("❌ WebSocket Error: " + e);
            isConnecting = false;
        };

        ws.OnClose += (e) =>
        {
            Debug.Log("🔴 WebSocket Closed, will retry...");
            isConnecting = false;
        };

        await ws.Connect();
    }

    void HandlePoseData(JObject data)
    {
        if (ball == null) return;

        string action = data["action"]?.ToString() ?? "";
        bool? shotIn = data["shot_in"]?.Type == JTokenType.Boolean ? (bool?)data["shot_in"] : null;

        // --- BALL POSITION FROM PYTHON ---
        JObject ballData = data["ball"] as JObject;
        if (ballData != null && !isScoring)
        {
            float bx = (float)(ballData["x"]?.ToObject<double>() ?? 0);
            float by = (float)(ballData["y"]?.ToObject<double>() ?? 0);

            // Python frame (640x480) → Unity space
            float normalizedX = (bx - 320f) / 100f;
            float normalizedY = (by - 240f) / 100f;
            float fixedY = Mathf.Clamp(2.5f - normalizedY, 0.5f, 5f);

            targetPos = new Vector3(normalizedX, fixedY, 0);
        }

        // --- COLOR STATUS ---
        Renderer r = ball.GetComponent<Renderer>();
        if (r != null)
        {
            if (shotIn == true)
            {
                r.material.color = Color.green;
                StartCoroutine(ShootBallIntoHoop());
            }
            else if (shotIn == false)
                r.material.color = Color.red;
            else
                r.material.color = Color.yellow;
        }
    }

   IEnumerator ShootBallIntoHoop()
{
    if (rimTarget == null || ball == null) yield break;

    isScoring = true;

    Vector3 startPos = ball.transform.position;
    Vector3 endPos = rimTarget.position + new Vector3(0, -0.1f, 0);

    float distance = Vector3.Distance(startPos, endPos);
    float heightGain = Mathf.Clamp(distance * 1.2f, 2.5f, 6f);  // 🟢 auto arc height based on distance
    float duration = Mathf.Clamp(distance * 0.2f, 0.8f, 1.5f);  // flight speed scales with distance
    float t = 0f;

    while (t < 1f)
    {
        t += Time.deltaTime / duration;

        // 🟠 smoother easing
        float smoothT = Mathf.SmoothStep(0, 1, t);

        // 🏀 high parabola arc
        float heightOffset = 4 * heightGain * smoothT * (1 - smoothT);

        Vector3 pos = Vector3.Lerp(startPos, endPos, smoothT);
        pos.y += heightOffset;

        ball.transform.position = pos;
        yield return null;
    }

    // ⬇️ gentle drop
    Vector3 dropPos = endPos + new Vector3(0, -0.6f, 0);
    float dropT = 0f;
    while (dropT < 1f)
    {
        dropT += Time.deltaTime * 2f;
        ball.transform.position = Vector3.Lerp(endPos, dropPos, dropT);
        yield return null;
    }

    isScoring = false;
}


    void Update()
    {
        ws?.DispatchMessageQueue();

        // Smooth ball movement even while not shooting
        if (ball != null && !isScoring)
        {
            ball.transform.position = Vector3.Lerp(
                ball.transform.position,
                targetPos,
                Time.deltaTime * moveSmooth
            );
        }
    }

    private void OnApplicationQuit()
    {
        ws?.Close();
    }
}
