from dribble import *
from shoot import *
from utils import *
from pose_stream import extract_keypoints
import asyncio, websockets, json

# --- Pose & camera setup ---
mp_pose = mp.solutions.pose
pose = mp_pose.Pose(min_detection_confidence=0.5, min_tracking_confidence=0.5)
cap = cv2.VideoCapture(0)

# --- Ball and player state ---
state = {
    "ball_x": 320, "ball_y": 200,
    "ball_vx": 0, "ball_vy": 0,
    "dribble_energy": 0,
    "holding": False, "hold_hand": None,
    "hold_start_time": 0, "last_hit_time": 0,
    "hit_cooldown": False,
    "side_mode": False, "side_mode_time": 0,
    "BALL_COLOR": BALL_COLOR_DEFAULT,
    "last_label": "",
}

# --- Unity input state (from joystick / WASD) ---
unity_input = {"move_x": 0, "move_y": 0, "offset_x": 0, "offset_z": 0}

# --- Latest Mediapipe keypoints for Unity ---
latest_keypoints = {}


# ---------- Helper functions ----------
def current_ball_position():
    return {
        "x": state["ball_x"],
        "y": state["ball_y"],
        "vx": state["ball_vx"],
        "vy": state["ball_vy"],
    }


def detect_action(state):
    if state["last_label"]:
        return state["last_label"].lower().replace(" ", "_")
    if state["holding"]:
        return f"holding_{state['hold_hand'] or 'auto'}"
    return "dribbling"


def detect_shot_result(state):
    if "shot" in state["last_label"].lower():
        return True  # shot made
    else:
        return None  # no shot


def dist(a, b):
    return math.hypot(a[0] - b[0], a[1] - b[1])


# ---------- Main CV loop ----------
def run_cv_loop():
    global latest_keypoints

    prev_left = prev_right = None
    left_speed_y = right_speed_y = left_speed_x = right_speed_x = 0

    while True:
        ok, frame = cap.read()
        if not ok:
            break

        frame = cv2.flip(frame, 1)
        h, w, _ = frame.shape
        floor_y = int(h * 0.90)

        res = pose.process(cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))
        latest_keypoints = extract_keypoints(res)

        right = left = rw = lw = rs = ls = re = le = head = None

        if res.pose_landmarks:
            mp.solutions.drawing_utils.draw_landmarks(frame, res.pose_landmarks, mp_pose.POSE_CONNECTIONS)
            lm = res.pose_landmarks.landmark
            rw, lw = lm[mp_pose.PoseLandmark.RIGHT_WRIST], lm[mp_pose.PoseLandmark.LEFT_WRIST]
            rs, ls = lm[mp_pose.PoseLandmark.RIGHT_SHOULDER], lm[mp_pose.PoseLandmark.LEFT_SHOULDER]
            re, le = lm[mp_pose.PoseLandmark.RIGHT_ELBOW], lm[mp_pose.PoseLandmark.LEFT_ELBOW]
            head = lm[mp_pose.PoseLandmark.NOSE]
            right, left = (int(rw.x * w), int(rw.y * h)), (int(lw.x * w), int(lw.y * h))

        # --- Hand speed ---
        if left and prev_left:
            left_speed_y, left_speed_x = left[1] - prev_left[1], left[0] - prev_left[0]
        prev_left = left
        if right and prev_right:
            right_speed_y, right_speed_x = right[1] - prev_right[1], right[0] - prev_right[0]
        prev_right = right

        # --- Ball physics ---
        if not state["holding"]:
            if state["side_mode"] and time.time() - state["side_mode_time"] < SIDE_ASSIST_DURATION:
                state["ball_vy"] += GRAVITY * 0.4
            else:
                state["ball_vy"] += GRAVITY
                state["side_mode"] = False
            state["ball_y"] += state["ball_vy"]
            state["ball_x"] += state["ball_vx"]
            state["ball_vx"] *= FRICTION

            if state["ball_x"] - BALL_RADIUS < 0:
                state["ball_x"], state["ball_vx"] = BALL_RADIUS, -state["ball_vx"] * 0.6
            elif state["ball_x"] + BALL_RADIUS > w:
                state["ball_x"], state["ball_vx"] = w - BALL_RADIUS, -state["ball_vx"] * 0.6

            if state["ball_y"] >= floor_y:
                state["ball_y"] = floor_y
                state["ball_vy"] = -state["ball_vy"] * ELASTICITY
                if abs(state["ball_vy"]) < 1:
                    state["dribble_energy"] = 0
                elif state["dribble_energy"] > 0:
                    state["ball_vy"] -= state["dribble_energy"] * 0.3
                    state["dribble_energy"] *= 0.7

        # --- Apply Unity joystick offset here ---
        state["ball_x"] += unity_input["move_x"] * 5
        state["ball_y"] -= unity_input["move_y"] * 5

        # --- Hand interaction logic (same as before) ---
        state["BALL_COLOR"] = BALL_COLOR_DEFAULT
        if right or left:
            nearL = left and dist(left, (state["ball_x"], state["ball_y"])) < HOLD_DIST
            nearR = right and dist(right, (state["ball_x"], state["ball_y"])) < HOLD_DIST

            if (nearL or nearR) and not state["hit_cooldown"]:
                if not state["holding"]:
                    state["hold_start_time"] = time.time()
                state["holding"] = True
                state["hold_hand"] = "both" if (nearL and nearR) else "left" if nearL else "right"
            else:
                if state["holding"]:
                    too_far = all(
                        not hpos or dist(hpos, (state["ball_x"], state["ball_y"])) >= RELEASE_DIST
                        for hpos in [left, right]
                    )
                    if too_far:
                        release_ball_downward(state)

            if state["holding"]:
                if state["hold_hand"] == "left" and left:
                    state["ball_x"], state["ball_y"], state["ball_vx"] = left[0], left[1], left_speed_x
                elif state["hold_hand"] == "right" and right:
                    state["ball_x"], state["ball_y"], state["ball_vx"] = right[0], right[1], right_speed_x
                elif state["hold_hand"] == "both" and left and right:
                    state["ball_x"] = (left[0] + right[0]) // 2
                    state["ball_y"] = (left[1] + right[1]) // 2
                    state["ball_vx"] = (left_speed_x + right_speed_x) / 2

                state["BALL_COLOR"] = BALL_COLOR_HOLD
                hold_time = time.time() - state["hold_start_time"]

                if state["ball_y"] >= floor_y - BALL_RADIUS * 0.2:
                    state["ball_y"] = floor_y
                    release_ball_downward(state)

                # One-hand dribble / cross
                if state["hold_hand"] in ["left", "right"]:
                    speed_y = left_speed_y if state["hold_hand"] == "left" else right_speed_y
                    speed_x = left_speed_x if state["hold_hand"] == "left" else right_speed_x

                    if hold_time > MIN_HOLD_BEFORE_RELEASE and speed_y > DRIBBLE_SPEED_THRESHOLD:
                        release_ball_downward(state)
                        state["last_label"] = f"Dribble ({state['hold_hand']})"
                    elif (
                        hold_time > CROSSOVER_HOLD_TIME
                        and abs(speed_x) > CROSSOVER_SPEED_THRESHOLD
                        and abs(speed_y) < CROSS_PREP_Y_IGNORE
                    ):
                        state["side_mode"], state["side_mode_time"] = True, time.time()
                        is_low = state["ball_y"] > floor_y - 150
                        is_behind = (state["hold_hand"] == "right" and rw.z > rs.z + 0.1) or (
                            state["hold_hand"] == "left" and lw.z > ls.z + 0.1
                        )
                        if is_low:
                            move_type = "Between Legs"
                            state["ball_vx"], state["ball_vy"], state["dribble_energy"] = (
                                speed_x * 1.2,
                                DRIBBLE_FORCE * 1.6,
                                DRIBBLE_FORCE * 0.6,
                            )
                        elif is_behind:
                            move_type = "Behind Back"
                            state["ball_vx"], state["ball_vy"], state["dribble_energy"] = (
                                -speed_x * 0.8,
                                DRIBBLE_FORCE * 1.0,
                                DRIBBLE_FORCE * 0.4,
                            )
                        else:
                            move_type = "Cross"
                            state["ball_vx"], state["ball_vy"], state["dribble_energy"] = (
                                speed_x * 2.2,
                                DRIBBLE_FORCE * 0.8,
                                DRIBBLE_FORCE * 0.4,
                            )
                        state["last_label"] = move_type
                        release_ball_downward(state)

                # Two-hand shoot
                elif state["hold_hand"] == "both":
                    shot_type = detect_shot_type(rw, lw, re, le, head, left_speed_y, right_speed_y)
                    if shot_type == "real":
                        shoot_real(state)
                        state["last_label"] = "Real Shot"
                    elif shot_type == "fake":
                        shoot_fake(state)
                        state["last_label"] = "Fake Shot"
                    elif left_speed_y > SPEED_TWO_HANDS and right_speed_y > SPEED_TWO_HANDS:
                        release_ball_downward(state)
                        state["last_label"] = "Two-hand Dribble"

            if state["hit_cooldown"] and time.time() - state["last_hit_time"] > COOLDOWN:
                state["hit_cooldown"] = False
        else:
            state["holding"], state["hold_hand"], state["dribble_energy"] = False, None, 0

        # --- Draw frame ---
        cv2.circle(frame, (int(state["ball_x"]), int(state["ball_y"])), BALL_RADIUS, state["BALL_COLOR"], -1)
        cv2.imshow("Basketball", frame)
        if cv2.waitKey(1) & 0xFF == 27:
            break

    cap.release()
    cv2.destroyAllWindows()


# ---------- WebSocket handler ----------
async def handle_unity(websocket):
    print("Unity connected to Python WebSocket")

    async def send_loop():
        """Send ball + pose data to Unity continuously"""
        while True:
            msg = {
                "pose": latest_keypoints,
                "action": detect_action(state),
                "ball": current_ball_position(),
                "shot_in": detect_shot_result(state),
            }
            await websocket.send(json.dumps(msg))
            await asyncio.sleep(0.03)

    send_task = asyncio.create_task(send_loop())

    try:
        async for message in websocket:
            data = json.loads(message)
            if data.get("type") == "input":
                # Unity sends joystick / keyboard input
                unity_input["move_x"] = float(data.get("move_x", 0))
                unity_input["move_y"] = float(data.get("move_y", 0))
                unity_input["offset_x"] = float(data.get("offset_x", 0))
                unity_input["offset_z"] = float(data.get("offset_z", 0))
                print(f"Unity input: move=({unity_input['move_x']:.2f},{unity_input['move_y']:.2f}) "
                      f"offset=({unity_input['offset_x']:.2f},{unity_input['offset_z']:.2f})")
    except websockets.ConnectionClosed:
        print("Unity disconnected")
    finally:
        send_task.cancel()


# ---------- Run both CV + WebSocket together ----------
async def run_all():
    server = await websockets.serve(handle_unity, "localhost", 8765)
    print("WebSocket server started at ws://localhost:8765")
    await asyncio.gather(
        asyncio.to_thread(run_cv_loop),
        server.wait_closed()
    )


if __name__ == "__main__":
    asyncio.run(run_all())
