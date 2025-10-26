import cv2
import time
from config import *

def shoot_real(state):
    state["holding"] = False
    state["hit_cooldown"] = True
    state["last_hit_time"] = time.time()
    state["ball_vy"] = -DRIBBLE_FORCE * 2.8
    state["dribble_energy"] = DRIBBLE_FORCE * 1.2
    state["BALL_COLOR"] = BALL_COLOR_REAL_SHOT
    state["last_label"] = "Real Shot"

def shoot_fake(state):
    state["holding"] = True
    state["hit_cooldown"] = True
    state["last_hit_time"] = time.time()
    state["ball_vy"] = -DRIBBLE_FORCE * 0.8
    state["dribble_energy"] = DRIBBLE_FORCE * 0.4
    state["BALL_COLOR"] = BALL_COLOR_FAKE_SHOT
    state["last_label"] = "Fake Shot"

def detect_shot_type(rw, lw, re, le, head, left_speed_y, right_speed_y):
    if not (rw and lw and re and le and head):
        return None
    if left_speed_y < -35 and right_speed_y < -35:
        both_above_head = (rw.y < head.y) and (lw.y < head.y)
        left_stretch = abs(lw.y - le.y) > 0.15
        right_stretch = abs(rw.y - re.y) > 0.15
        if both_above_head and (left_stretch or right_stretch):
            return "real"
        else:
            return "fake"
    return None