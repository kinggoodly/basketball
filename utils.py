import cv2
import math
import time
import json
import asyncio
import mediapipe as mp
import websockets
import numpy as np
from config import *

def dist(a, b):
    return math.hypot(a[0]-b[0], a[1]-b[1])
