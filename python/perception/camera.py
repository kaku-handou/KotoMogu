import cv2


class Camera:
    def __init__(self, index, width=1280, height=720):
        self.cap = cv2.VideoCapture(index, cv2.CAP_DSHOW)
        self.cap.set(cv2.CAP_PROP_FRAME_WIDTH, width)
        self.cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)

        if not self.cap.isOpened():
            raise RuntimeError(f"カメラ {index} 番を開けません")

    def read(self):
        ok, frame = self.cap.read()
        if not ok:
            return None
        return frame

    def release(self):
        self.cap.release()


if __name__ == "__main__":
    camera = Camera(0)

    while True:
        frame = camera.read()
        if frame is None:
            print("映像が読めません")
            break

        cv2.imshow("camera", frame)

        if cv2.waitKey(1) & 0xFF == ord("q"):
            break

    camera.release()
    cv2.destroyAllWindows()