from utils import *

def release_ball_downward(state):
    state["holding"] = False
    state["hit_cooldown"] = True
    state["last_hit_time"] = time.time()
    state["ball_vy"] = DRIBBLE_FORCE * 1.2
    state["dribble_energy"] = DRIBBLE_FORCE * 0.8
    state["BALL_COLOR"] = BALL_COLOR_RELEASE
    state["last_label"] = "Dribble"
