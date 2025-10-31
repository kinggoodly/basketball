using UnityEngine;
using NativeWebSocket;
using System.Collections;
using Newtonsoft.Json.Linq;

public class UnityReceiver_Native : MonoBehaviour
{
    WebSocket ws;
    public GameObject ball;
    public Transform rimTarget;

    [Header("Motion Settings")]
    public float moveSmooth = 8f;
    public float shotSpeed = 10f;
    public float dropAfterShot = 1.2f;
    public float arcHeight = 5f;

    [Header("Spin Settings")]
    public float spinSpeed = 480f;
    public float spinThreshold = 0.002f;

    bool isConnecting = false;
    bool isShooting = false;
    bool ignorePython = false;   // ⛔ ignore python updates during animation

    Vector3 targetPos;
    Vector3 lastPosition;
    Vector3 initialBallPos;      // 🟢 store wherever the ball starts

    void Start()
    {
        lastPosition = Vector3.zero;

        // 🟢 Remember the ball's starting position
        if (ball != null)
            initialBallPos = ball.transform.position;

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

    bool lastShotState = false;

    void HandlePoseData(JObject data)
    {
        if (ball == null) return;

        bool? shotIn = data["shot_in"]?.Type == JTokenType.Boolean ? (bool?)data["shot_in"] : null;
        bool currentShot = shotIn ?? false;

        // 🎨 Color feedback
        Renderer r = ball.GetComponent<Renderer>();
        if (r != null)
        {
            if (currentShot) r.material.color = Color.green;
            else if (shotIn == false) r.material.color = Color.red;
            else r.material.color = Color.yellow;
        }

        // ⛔ Ignore python updates during animation
        if (ignorePython) return;

        // 🟢 Update ball position relative to start
        JObject ballData = data["ball"] as JObject;
        if (ballData != null)
        {
            float bx = (float)(ballData["x"]?.ToObject<double>() ?? 320);
            float by = (float)(ballData["y"]?.ToObject<double>() ?? 240);

            float normalizedX = (bx - 320f) / 100f;
            float normalizedY = (by - 240f) / 100f;
            float fixedY = Mathf.Clamp(2.5f - normalizedY, 0.5f, 5f);

            targetPos = initialBallPos + new Vector3(normalizedX, fixedY - 2.5f, 0);
        }

        // 🏀 Trigger shot only once when shot_in changes from false → true
        if (currentShot && !lastShotState && !isShooting)
        {
            StartCoroutine(ShootToRim());
        }

        lastShotState = currentShot;
    }

    IEnumerator ShootToRim()
    {
        if (ball == null || rimTarget == null) yield break;

        isShooting = true;
        ignorePython = true;

        Vector3 startPos = ball.transform.position;
        Vector3 endPos = rimTarget.position;

        float distance = Vector3.Distance(startPos, endPos);
        float duration = Mathf.Clamp(distance * 0.22f, 0.8f, 1.3f);
        float dynamicArc = Mathf.Clamp(distance * 0.8f, 2f, arcHeight);
        float t = 0f;

        // 🟢 Flight phase (arc)
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            float smoothT = Mathf.SmoothStep(0, 1, t);
            Vector3 pos = Vector3.Lerp(startPos, endPos, smoothT);
            pos.y += Mathf.Sin(Mathf.PI * smoothT) * dynamicArc;

            ball.transform.position = pos;
            ball.transform.Rotate(Vector3.right * Time.deltaTime * spinSpeed * 1.5f);
            yield return null;
        }

        // 🟡 Detect floor height automatically
        float floorY = 0.5f; // fallback value
        GameObject floorObj = GameObject.Find("floor");
        if (floorObj == null) floorObj = GameObject.Find("Ground");

        if (floorObj != null)
        {
            Collider floorCol = floorObj.GetComponent<Collider>();
            if (floorCol != null)
            {
                floorY = floorCol.bounds.max.y + 0.01f; // 🟢 real top of collider
            }
            else
            {
                floorY = floorObj.transform.position.y - 2f; // 🟠 fallback offset
            }
        }

        // 🟡 Drop phase (forward + gravity)
        float dropSpeed = shotSpeed * (dropAfterShot * 0.5f);
        float forwardSpeed = 2.2f;
        Vector3 direction = (endPos - startPos).normalized;
        Vector3 dropPos = ball.transform.position;

        while (dropPos.y > floorY + 0.3f)
        {
            dropPos += direction * forwardSpeed * Time.deltaTime;
            dropPos.y -= Time.deltaTime * dropSpeed;
            dropPos.y = Mathf.Max(dropPos.y, floorY);
            ball.transform.position = dropPos;
            ball.transform.Rotate(Vector3.right * Time.deltaTime * -spinSpeed * 0.8f);
            yield return null;
        }

        // 🟠 First bounce
        Vector3 bounceStart = ball.transform.position;
        float bounceHeight = 3f;
        float bounceForward = 2f;
        float bounceT = 0f;

        while (bounceT < 1f)
        {
            bounceT += Time.deltaTime * 1.8f;
            float bounceY = Mathf.Sin(bounceT * Mathf.PI) * bounceHeight;
            Vector3 pos = bounceStart + direction * (bounceForward * bounceT);
            pos.y = Mathf.Max(floorY + bounceY, floorY);
            ball.transform.position = pos;
            ball.transform.Rotate(Vector3.right * Time.deltaTime * -spinSpeed);
            yield return null;
        }

        // 🔵 Second smaller bounce
        Vector3 secondStart = ball.transform.position;
        float secondHeight = 0.7f;
        float secondForward = 0.5f;
        float secondT = 0f;

        while (secondT < 1f)
        {
            secondT += Time.deltaTime * 2.2f;
            float bounceY = Mathf.Sin(secondT * Mathf.PI) * secondHeight;
            Vector3 pos = secondStart + direction * (secondForward * secondT);
            pos.y = Mathf.Max(floorY + bounceY, floorY);
            ball.transform.position = pos;
            ball.transform.Rotate(Vector3.right * Time.deltaTime * -spinSpeed * 0.6f);
            yield return null;
        }

        // ✋ Return to hand
        Vector3 handPos = initialBallPos;
        Vector3 returnStart = ball.transform.position;
        float returnT = 0f;

        while (returnT < 1f)
        {
            returnT += Time.deltaTime;
            Vector3 pos = Vector3.Lerp(returnStart, handPos, Mathf.SmoothStep(0, 1, returnT));
            pos.y = Mathf.Max(pos.y, floorY);
            ball.transform.position = pos;
            ball.transform.Rotate(Vector3.right * Time.deltaTime * spinSpeed * 0.4f);
            yield return null;
        }

        // ✅ Resume Python control
        isShooting = false;
        ignorePython = false;
    }

    void Update()
    {
        ws?.DispatchMessageQueue();

        // 🟢 Smooth follow during Python control
        if (ball != null && !ignorePython)
        {
            ball.transform.position = Vector3.Lerp(
                ball.transform.position,
                targetPos,
                Time.deltaTime * moveSmooth
            );
        }

        // 🔄 Spin system
        if (ball != null)
        {
            float velocity = (ball.transform.position - lastPosition).sqrMagnitude;
            if (velocity > 0.000001f)
            {
                float dynamicSpin = Mathf.Lerp(spinSpeed * 0.5f, spinSpeed * 1.5f, Mathf.Clamp01(velocity * 1000f));
                ball.transform.Rotate(Vector3.right * Time.deltaTime * dynamicSpin);
            }
            lastPosition = ball.transform.position;
        }
    }

    private void OnApplicationQuit() => ws?.Close();
}
