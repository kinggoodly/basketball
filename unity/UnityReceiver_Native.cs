using UnityEngine;
using NativeWebSocket;
using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine.InputSystem;

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

    [Header("Joystick Settings")]
    public float moveSpeed = 3f;

    bool isConnecting = false;
    bool isShooting = false;
    bool ignorePython = false;

    Vector3 targetPos;
    Vector3 lastPosition;
    Vector3 initialBallPos;
    Vector3 manualOffset = Vector3.zero;  // 🕹️ joystick/keyboard offset

    void Start()
    {
        lastPosition = Vector3.zero;

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
            Debug.Log("Connected to Python WebSocket");
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
            Debug.LogError("WebSocket Error: " + e);
            isConnecting = false;
        };

        ws.OnClose += (e) =>
        {
            Debug.Log("WebSocket Closed, will retry...");
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

        // Color feedback
        Renderer r = ball.GetComponent<Renderer>();
        if (r != null)
        {
            if (currentShot) r.material.color = Color.green;
            else if (shotIn == false) r.material.color = Color.red;
            else r.material.color = Color.yellow;
        }

        // Ignore python updates during animation
        if (ignorePython) return;

        // Update ball position relative to start
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

        // Trigger shot only once when shot_in changes from false → true
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

        // Flight phase
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

        // Detect floor
        float floorY = 0.5f;
        GameObject floorObj = GameObject.Find("floor");
        if (floorObj == null) floorObj = GameObject.Find("Ground");
        if (floorObj != null)
        {
            Collider floorCol = floorObj.GetComponent<Collider>();
            if (floorCol != null)
                floorY = floorCol.bounds.max.y + 0.01f;
        }

        // Drop phase
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
            yield return null;
        }

        // Three Bounces (progressively smaller)
        for (int i = 0; i < 3; i++)
        {
            float height = Mathf.Lerp(3f, 0.7f, i / 2f);  // 3 → 1.85 → 0.7
            float forward = Mathf.Lerp(2f, 0.4f, i / 2f); // forward distance shrinks
            float bounceT = 0f;

            while (bounceT < 1f)
            {
                bounceT += Time.deltaTime * (1.8f + i * 0.4f);
                float bounceY = Mathf.Sin(bounceT * Mathf.PI) * height;
                Vector3 pos = ball.transform.position + direction * (forward * Time.deltaTime * 2f);
                pos.y = Mathf.Max(floorY + bounceY, floorY);
                ball.transform.position = pos;
                yield return null;
            }
        }

        // Return to hand
        Vector3 handPos = initialBallPos + manualOffset;
        Vector3 returnStart = ball.transform.position;
        float returnT = 0f;

        while (returnT < 1f)
        {
            returnT += Time.deltaTime;
            Vector3 pos = Vector3.Lerp(returnStart, handPos, Mathf.SmoothStep(0, 1, returnT));
            pos.y = Mathf.Max(pos.y, floorY);
            ball.transform.position = pos;
            yield return null;
        }

        isShooting = false;
        ignorePython = false;
    }
void Update()
{
    ws?.DispatchMessageQueue();

    // 🎮 1️⃣ Read joystick or keyboard input manually
    Vector2 moveInput = Vector2.zero;

    // ✅ Gamepad analog stick
    if (Gamepad.current != null)
    {
        moveInput = Gamepad.current.leftStick.ReadValue();
    }

    // ✅ Fallback to keyboard
    if (Keyboard.current != null)
    {
        if (Keyboard.current.wKey.isPressed) moveInput.y += 1;
        if (Keyboard.current.sKey.isPressed) moveInput.y -= 1;
        if (Keyboard.current.dKey.isPressed) moveInput.x += 1;
        if (Keyboard.current.aKey.isPressed) moveInput.x -= 1;
    }

    // normalize diagonal movement
    moveInput = Vector2.ClampMagnitude(moveInput, 1f);

    // 🧭 2️⃣ Apply joystick offset locally
    Vector3 delta = new Vector3(moveInput.x, 0, moveInput.y) * moveSpeed * Time.deltaTime;
    manualOffset += delta;

    // 🧠 3️⃣ Combine Python position + manual offset
    if (ball != null && !ignorePython)
    {
        Vector3 combinedTarget = targetPos + manualOffset;
        ball.transform.position = Vector3.Lerp(ball.transform.position, combinedTarget, Time.deltaTime * moveSmooth);
    }

    // 🔄 4️⃣ Ball spin
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

    // 📨 5️⃣ Send movement data to Python
    if (ws != null && ws.State == WebSocketState.Open)
    {
        JObject msg = new JObject();
        msg["type"] = "input";
        msg["move_x"] = moveInput.x;
        msg["move_y"] = moveInput.y;
        msg["offset_x"] = manualOffset.x;
        msg["offset_z"] = manualOffset.z;

        ws.SendText(msg.ToString());
        Debug.Log($"[SEND→PYTHON] move=({moveInput.x:F2},{moveInput.y:F2})");
    }
}

    private void OnApplicationQuit() => ws?.Close();
}
