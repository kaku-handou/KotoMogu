import time

import cv2
import mediapipe as mp
from mediapipe.tasks import python as mp_python
from mediapipe.tasks.python import vision


class HandDetector:
    def __init__(self, model_path):
        base_options = mp_python.BaseOptions(model_asset_path=model_path)
        options = vision.HandLandmarkerOptions(
            base_options=base_options,
            running_mode=vision.RunningMode.VIDEO,
            num_hands=1,
        )
        self.landmarker = vision.HandLandmarker.create_from_options(options)
        self.last_timestamp = 0

    def detect(self, frame):
        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)

        timestamp = int(time.monotonic() * 1000)
        if timestamp <= self.last_timestamp:
            timestamp = self.last_timestamp + 1
        self.last_timestamp = timestamp

        result = self.landmarker.detect_for_video(image, timestamp)

        if len(result.hand_landmarks) == 0:
            return None
        return result.hand_landmarks[0]

    def close(self):
        self.landmarker.close()


if __name__ == "__main__":
    from perception.camera import Camera

    CAMERA_INDEX = 0

    camera = Camera(CAMERA_INDEX)
    detector = HandDetector("models/hand_landmarker.task")

    while True:
        frame = camera.read()
        if frame is None:
            break

        landmarks = detector.detect(frame)

        if landmarks is not None:
            h, w = frame.shape[:2]
            for i, point in enumerate(landmarks):
                x = int(point.x * w)
                y = int(point.y * h)
                cv2.circle(frame, (x, y), 4, (0, 255, 0), -1)
                cv2.putText(frame, str(i), (x + 5, y - 5),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.4, (255, 255, 255), 1)

        cv2.imshow("hand", frame)
        if cv2.waitKey(1) & 0xFF == ord("q"):
            break

    detector.close()
    camera.release()
    cv2.destroyAllWindows()