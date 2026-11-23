import time

import cv2
import mediapipe as mp
from mediapipe.tasks import python as mp_python
from mediapipe.tasks.python import vision


class FaceDetector:
    def __init__(self, model_path, *, expressions=False):
        base_options = mp_python.BaseOptions(model_asset_path=model_path)
        options = vision.FaceLandmarkerOptions(
            base_options=base_options,
            running_mode=vision.RunningMode.VIDEO,
            num_faces=1,
            output_face_blendshapes=expressions,
        )
        self.landmarker = vision.FaceLandmarker.create_from_options(options)
        self.last_timestamp = 0

    def detect_result(self, frame):
        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)

        timestamp = int(time.monotonic() * 1000)
        if timestamp <= self.last_timestamp:
            timestamp = self.last_timestamp + 1
        self.last_timestamp = timestamp

        return self.landmarker.detect_for_video(image, timestamp)

    def detect(self, frame):
        result = self.detect_result(frame)

        if len(result.face_landmarks) == 0:
            return None
        return result.face_landmarks[0]

    def close(self):
        self.landmarker.close()


if __name__ == "__main__":
    from perception.camera import Camera

    CAMERA_INDEX = 0

    camera = Camera(CAMERA_INDEX)
    detector = FaceDetector("models/face_landmarker.task")

    while True:
        frame = camera.read()
        if frame is None:
            break

        landmarks = detector.detect(frame)

        if landmarks is not None:
            h, w = frame.shape[:2]
            for point in landmarks:
                x = int(point.x * w)
                y = int(point.y * h)
                cv2.circle(frame, (x, y), 1, (0, 255, 0), -1)

        text = "face: yes" if landmarks is not None else "face: no"
        cv2.putText(frame, text, (20, 40), cv2.FONT_HERSHEY_SIMPLEX, 1.2, (0, 255, 0), 2)

        cv2.imshow("face", frame)
        if cv2.waitKey(1) & 0xFF == ord("q"):
            break

    detector.close()
    camera.release()
    cv2.destroyAllWindows()