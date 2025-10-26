import math
import numpy as np
import cv2

def dist(a, b):
    return math.hypot(a[0]-b[0], a[1]-b[1])
