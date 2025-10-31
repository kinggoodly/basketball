from utils import *

mp_pose = mp.solutions.pose
def extract_keypoints(res):
    """
    Convert MediaPipe result to dict of named keypoints.
    Call this from main.py with res = pose.process(...)
    """
    keypoints = {}
    if not res or not res.pose_landmarks:
        return keypoints

    for lm_id, lm in enumerate(res.pose_landmarks.landmark):
        name = mp_pose.PoseLandmark(lm_id).name
        keypoints[name] = {
            "x": round(lm.x, 4),
            "y": round(lm.y, 4),
            "z": round(lm.z, 4)
        }

    return keypoints
