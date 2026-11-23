"""Send expression events to the Alice_Test Unity scene over localhost UDP."""
import argparse
from contextlib import closing
import socket
import time
from pathlib import Path


class ExpressionGate:
    def __init__(self, hold=0.5, release=0.35):
        self.hold = hold
        self.release = release
        self.candidate = ""
        self.since = 0.0
        self.ready = True

    def observe(self, label, now):
        if label != self.candidate:
            self.candidate = label
            self.since = now
        elapsed = now - self.since
        if not label and elapsed >= self.release:
            self.ready = True
        if label and self.ready and elapsed >= self.hold:
            self.ready = False
            return label
        return None


def expression(scores):
    if min(scores.get("mouthSmileLeft", 0), scores.get("mouthSmileRight", 0)) >= 0.52:
        return "smile"
    if scores.get("jawOpen", 0) >= 0.65:
        return "mouth_open"
    return ""


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--camera", type=int, default=0, help="Windows camera index")
    parser.add_argument("--simulate", choices=("smile", "mouth_open"),
                        help="Send one test event without opening a camera")
    args = parser.parse_args()
    if args.simulate:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sender:
            sender.sendto(args.simulate.encode("ascii"), ("127.0.0.1", 8768))
        print("Simulated camera event:", args.simulate)
        return
    import cv2
    from perception.camera import Camera
    from perception.face import FaceDetector

    model = Path(__file__).resolve().parent / "models" / "face_landmarker.task"
    gate = ExpressionGate()
    camera = Camera(args.camera, width=640, height=480)
    try:
        with closing(FaceDetector(str(model), expressions=True)) as detector:
            with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sender:
                print("Camera ready. Keep this window open. Press Q in the preview to stop.")
                while True:
                    frame = camera.read()
                    if frame is None:
                        raise RuntimeError("Camera frame unavailable. Check the device and close other camera apps.")
                    result = detector.detect_result(frame)
                    scores = ({item.category_name: item.score for item in result.face_blendshapes[0]}
                              if result.face_blendshapes else {})
                    label = expression(scores)
                    event = gate.observe(label, time.monotonic())
                    if event:
                        sender.sendto(event.encode("ascii"), ("127.0.0.1", 8768))
                        print("Camera -> Unity:", event, flush=True)
                    status = label or ("neutral" if scores else "no face")
                    cv2.putText(frame, status + " | Q to quit", (12, 30),
                                cv2.FONT_HERSHEY_SIMPLEX, 0.75, (50, 230, 50), 2)
                    cv2.imshow("Alice face camera", frame)
                    if cv2.waitKey(1) & 0xFF == ord("q"):
                        break
    finally:
        camera.release()
        cv2.destroyAllWindows()


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        pass
