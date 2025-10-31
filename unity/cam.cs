using UnityEngine;

public class CameraFollowAndFaceBallDynamicY : MonoBehaviour
{
    [Header("Targets")]
    public Transform ball;
    public Transform rim;

    [Header("Offset Settings")]
    public float distance = 8f;
    public float height = 3f;
    public float followSmooth = 5f;
    public float lookSmooth = 6f;

    [Header("Dynamic Rotation Settings")]
    public bool autoCenterOnRim = true;     // Enable dynamic Y rotation
    public float rotationLerpSpeed = 2f;    // Smoothness of rotation following
    public float maxYawAngle = 40f;         // Prevent camera from going too far to side

    private float currentYaw = 0f;

    void LateUpdate()
    {
        if (ball == null || rim == null) return;

        // 🟩 Step 1: Determine desired Y rotation so camera sees rim centered
        Vector3 ballToRim = rim.position - ball.position;
        float desiredYaw = Mathf.Atan2(ballToRim.x, ballToRim.z) * Mathf.Rad2Deg;

        // 🟨 Step 2: Smoothly rotate toward that yaw (so camera slides sideways)
        if (autoCenterOnRim)
            currentYaw = Mathf.LerpAngle(currentYaw, desiredYaw, Time.deltaTime * rotationLerpSpeed);

        // 🟦 Step 3: Compute camera offset based on current yaw
        Quaternion yawRotation = Quaternion.Euler(0, currentYaw, 0);
        Vector3 offset = yawRotation * new Vector3(0, height, -distance);

        // 🟧 Step 4: Move camera behind ball
        Vector3 desiredPos = ball.position + offset;
        transform.position = Vector3.Lerp(transform.position, desiredPos, Time.deltaTime * followSmooth);

        // 🟥 Step 5: Always look at the rim
        Quaternion lookRot = Quaternion.LookRotation(rim.position - transform.position, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, Time.deltaTime * lookSmooth);
    }
}
