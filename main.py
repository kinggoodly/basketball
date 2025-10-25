import cv2, mediapipe as mp, math, time

mp_pose = mp.solutions.pose
pose = mp_pose.Pose(min_detection_confidence=0.5,
                    min_tracking_confidence=0.5)
cap = cv2.VideoCapture(0)

ball_x, ball_y = 320, 200
ball_vx, ball_vy = 0, 0
gravity = 1.5
elasticity = 0.85
friction = 0.85
BALL_RADIUS = 55
BALL_COLOR = (0, 140, 255)

holding = False
hold_hand = None
hold_start_time = 0
last_hit_time = 0
hit_cooldown = False
dribble_energy = 0
side_mode = False
side_mode_time = 0
SIDE_ASSIST_DURATION = 0.15


HOLD_DIST = BALL_RADIUS + 70
RELEASE_DIST = BALL_RADIUS + 100
DRIBBLE_FORCE = 18
COOLDOWN = 0.5
SPEED_TWO_HANDS = 70


DRIBBLE_SPEED_THRESHOLD = 35     # lower = easier bounce
CROSSOVER_SPEED_THRESHOLD = 6    # lower = easier horizontal detection
CROSSOVER_HOLD_TIME = 0.18       # more time before cross allowed
CROSS_PREP_Y_IGNORE = 15         # ignore small up/down while swinging
MIN_HOLD_BEFORE_RELEASE = 0.08   # slight grip time before drop

# --- Hand motion memory ---
prev_left = prev_right = None
left_speed_y = right_speed_y = 0
left_speed_x = right_speed_x = 0

def dist(a,b): return math.hypot(a[0]-b[0], a[1]-b[1])

def release_ball_downward():
    global holding, hit_cooldown, last_hit_time, ball_vy, dribble_energy, BALL_COLOR
    holding = False
    hit_cooldown = True
    last_hit_time = time.time()
    ball_vy = DRIBBLE_FORCE * 1.2
    dribble_energy = DRIBBLE_FORCE * 0.8
    BALL_COLOR = (0, 255, 255)

while True:
    ok, frame = cap.read()
    if not ok:
        break
    frame = cv2.flip(frame,1)
    h,w,_ = frame.shape
    floor_y = int(h * 0.90)
    res = pose.process(cv2.cvtColor(frame,cv2.COLOR_BGR2RGB))

    right = left = None
    if res.pose_landmarks:
        mp.solutions.drawing_utils.draw_landmarks(
            frame, res.pose_landmarks, mp_pose.POSE_CONNECTIONS)
        rw = res.pose_landmarks.landmark[mp_pose.PoseLandmark.RIGHT_WRIST]
        lw = res.pose_landmarks.landmark[mp_pose.PoseLandmark.LEFT_WRIST]
        right, left = (int(rw.x*w),int(rw.y*h)), (int(lw.x*w),int(lw.y*h))

    # --- Hand velocities ---
    if left:
        if prev_left:
            left_speed_y = left[1] - prev_left[1]
            left_speed_x = left[0] - prev_left[0]
        prev_left = left
    else:
        left_speed_x = left_speed_y = 0
        prev_left = None

    if right:
        if prev_right:
            right_speed_y = right[1] - prev_right[1]
            right_speed_x = right[0] - prev_right[0]
        prev_right = right
    else:
        right_speed_x = right_speed_y = 0
        prev_right = None

    # --- Ball physics ---
    if not holding:
        if side_mode and time.time() - side_mode_time < SIDE_ASSIST_DURATION:
            ball_vy += gravity * 0.4
        else:
            ball_vy += gravity
            side_mode = False

        ball_y += ball_vy
        ball_x += ball_vx
        ball_vx *= friction

        if ball_x - BALL_RADIUS < 0:
            ball_x = BALL_RADIUS
            ball_vx = -ball_vx * 0.6
        elif ball_x + BALL_RADIUS > w:
            ball_x = w - BALL_RADIUS
            ball_vx = -ball_vx * 0.6

        if ball_y >= floor_y:
            ball_y = floor_y
            ball_vy = -ball_vy * elasticity
            if abs(ball_vy) < 1:
                dribble_energy = 0
            elif dribble_energy > 0:
                ball_vy -= dribble_energy * 0.3
                dribble_energy *= 0.7

    BALL_COLOR = (0,140,255)

    # --- Hand interaction ---
    if right or left:
        nearL = left and dist(left,(ball_x,ball_y)) < HOLD_DIST
        nearR = right and dist(right,(ball_x,ball_y)) < HOLD_DIST

        if (nearL or nearR) and not hit_cooldown:
            if not holding:
                hold_start_time = time.time()
            holding = True
            if nearL and nearR:
                hold_hand = "both"
            elif nearL:
                hold_hand = "left"
            elif nearR:
                hold_hand = "right"
        else:
            if holding:
                too_far = True
                for hpos in [left,right]:
                    if hpos and dist(hpos,(ball_x,ball_y)) < RELEASE_DIST:
                        too_far = False
                if too_far:
                    release_ball_downward()

        if holding:
            if hold_hand == "left" and left:
                ball_x, ball_y = left
                ball_vx = left_speed_x
            elif hold_hand == "right" and right:
                ball_x, ball_y = right
                ball_vx = right_speed_x
            elif hold_hand == "both" and left and right:
                ball_x = (left[0]+right[0])//2
                ball_y = (left[1]+right[1])//2
                ball_vx = (left_speed_x+right_speed_x)/2

            BALL_COLOR = (0,255,0)
            hold_time = time.time() - hold_start_time

            if ball_y >= floor_y - BALL_RADIUS*0.2:
                ball_y = floor_y
                release_ball_downward()

            if hold_hand in ["left","right"]:
                speed_y = left_speed_y if hold_hand=="left" else right_speed_y
                speed_x = left_speed_x if hold_hand=="left" else right_speed_x

                # 🟡 Normal easy dribble
                if hold_time > MIN_HOLD_BEFORE_RELEASE and speed_y > DRIBBLE_SPEED_THRESHOLD:
                    release_ball_downward()

                # 🔵 Smooth cross — wait until full swing
                elif hold_time > CROSSOVER_HOLD_TIME and abs(speed_x) > CROSSOVER_SPEED_THRESHOLD and abs(speed_y) < CROSS_PREP_Y_IGNORE:
                    side_mode = True
                    side_mode_time = time.time()

                    # smooth glide crossover
                    target_vx = speed_x * 2.2
                    ball_vx += (target_vx - ball_vx) * 0.25
                    ball_vy = DRIBBLE_FORCE * 0.8
                    dribble_energy = DRIBBLE_FORCE * 0.4
                    release_ball_downward()

            elif hold_hand == "both":
                if left_speed_y > SPEED_TWO_HANDS and right_speed_y > SPEED_TWO_HANDS:
                    release_ball_downward()

        if hit_cooldown and time.time()-last_hit_time > COOLDOWN:
            hit_cooldown = False

        if not holding:
            body_center = w // 2
            if ball_x < body_center:
                hold_hand = "left"
            elif ball_x > body_center:
                hold_hand = "right"

    else:
        holding = False
        hold_hand = None
        dribble_energy = 0

    cv2.line(frame,(0,floor_y),(w,floor_y),(255,255,255),3)
    cv2.circle(frame,(int(ball_x),int(ball_y)),BALL_RADIUS,BALL_COLOR,-1)
    txt = f"{'HOLDING' if holding else 'DRIBBLING'} ({hold_hand or 'auto'})"
    cv2.putText(frame,txt,(20,50),cv2.FONT_HERSHEY_SIMPLEX,0.7,(255,255,255),2)
    cv2.putText(frame,f"vx:{ball_vx:.1f} vy:{ball_vy:.1f}",(20,90),
                cv2.FONT_HERSHEY_SIMPLEX,0.5,(0,255,255),1)

    cv2.imshow("Basketball Physics",frame)
    if cv2.waitKey(1)&0xFF==27:
        break

cap.release()
cv2.destroyAllWindows()
