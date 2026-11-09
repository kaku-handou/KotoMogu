
"""KotoMogu - OpenAI STT + WebRTC Voice Activity Detection."""

import argparse
import io
import json
import math
import os
import struct
import threading
import time
import wave
import sys
from array import array
from datetime import datetime
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlsplit

import webrtcvad

MAX_BYTES = 6 * 1024 * 1024
LANGUAGE_HINTS = ["ja", "zh", "en", "fr", "tr", "ko", "vi"]
KEYWORD_HINTS = ["KotoMogu", "コトモグ", "未来ペット"]

CONTEXT_PROMPT = (
    "Short utterances recorded near a virtual pet exhibit "
    "at a Japanese school. Visitors may speak Japanese, "
    "Mandarin Chinese, English, French, Turkish, Korean or "
    "Vietnamese. The pet's name is KotoMogu (コトモグ)."
)

VAD_MODE = 2
MIN_VOICED_MS = 210


def inspect_wav(body, minimum=0.2, maximum=15):
    try:
        with wave.open(io.BytesIO(body), "rb") as wav:
            rate = wav.getframerate()
            channels = wav.getnchannels()
            width = wav.getsampwidth()
            frames = wav.getnframes()
            seconds = frames / rate

            if rate not in (8000, 16000, 32000, 48000):
                raise ValueError("Unsupported sample rate.")
            if channels not in (1, 2) or width != 2:
                raise ValueError("Expected PCM16 mono/stereo WAV.")
            if not minimum <= seconds <= maximum:
                raise ValueError("Invalid audio duration.")

            pcm = wav.readframes(frames)
            if len(pcm) != frames * channels * 2:
                raise ValueError("Incomplete WAV audio.")

        values = array("h")
        values.frombytes(pcm)
        if sys.byteorder != "little":
            values.byteswap()

        energy = sum(v * v for v in values)
        rms = math.sqrt(energy / max(len(values), 1)) / 32768

        return seconds, rms, rate, channels, pcm

    except (wave.Error, EOFError, struct.error) as exc:
        raise ValueError("Invalid WAV file.") from exc


def voice_metrics(rate, channels, pcm):
    values = array("h")
    values.frombytes(pcm)
    if sys.byteorder != "little":
        values.byteswap()

    if channels == 2:
        mono = array(
            "h",
            (
                (int(values[i]) + int(values[i + 1])) // 2
                for i in range(0, len(values), 2)
            )
        )
    else:
        mono = values

    if sys.byteorder != "little":
        mono.byteswap()

    data = mono.tobytes()
    frame_bytes = int(rate * 0.03) * 2
    vad = webrtcvad.Vad(VAD_MODE)

    total = 0
    voiced = 0

    for pos in range(0, len(data) - frame_bytes + 1, frame_bytes):
        frame = data[pos:pos + frame_bytes]
        total += 1
        if vad.is_speech(frame, rate):
            voiced += 1

    ratio = voiced / total if total else 0.0
    return ratio, voiced * 30


def language_codes(items):
    codes = []
    for item in items or []:
        code = (
            item.get("code")
            if isinstance(item, dict)
            else getattr(item, "code", None)
        )
        if isinstance(code, str) and code not in codes:
            codes.append(code)
    return codes


class Recognizer:
    def __init__(self, client, model):
        self.client = client
        self.model = model

    def transcribe(self, body):
        audio = io.BytesIO(body)
        audio.name = "visitor.wav"

        result = self.client.audio.transcriptions.create(
            model=self.model,
            file=audio,
            prompt=CONTEXT_PROMPT,
            extra_body={
                "languages": LANGUAGE_HINTS,
                "keywords": KEYWORD_HINTS,
            },
        )

        return {
            "text": (getattr(result, "text", "") or "").strip(),
            "languages": language_codes(
                getattr(result, "languages", None)
            ),
        }


class Monitor:
    def __init__(self, model):
        self.lock = threading.Lock()
        self.state = {
            "model": model,
            "state": "waiting",
            "text": "",
            "language": "",
            "languages": [],
            "audio_seconds": 0,
            "elapsed_seconds": 0,
            "count": 0,
            "updated_at": "",
            "warning": "",
            "error": "",
            "history": [],
        }

    def update(self, **values):
        with self.lock:
            current = dict(self.state)
            current.update(values)
            current["updated_at"] = datetime.now().strftime(
                "%Y-%m-%d %H:%M:%S"
            )

            if current["state"] == "done":
                current["count"] = self.state["count"] + 1
                if current["text"]:
                    entry = {
                        key: current[key]
                        for key in (
                            "updated_at", "text", "language",
                            "audio_seconds", "elapsed_seconds"
                        )
                    }
                    current["history"] = (
                        [entry] + self.state["history"]
                    )[:12]

            self.state = current

    def get(self):
        with self.lock:
            result = dict(self.state)
            result["history"] = list(self.state["history"])
            return result


def make_handler(recognizer, model_name, monitor):
    transcription_lock = threading.Lock()

    class Handler(BaseHTTPRequestHandler):
        def reply(self, code, result):
            raw = json.dumps(
                result, ensure_ascii=False
            ).encode("utf-8")
            self.send_response(code)
            self.send_header(
                "Content-Type", "application/json; charset=utf-8"
            )
            self.send_header("Cache-Control", "no-store")
            self.send_header("Content-Length", str(len(raw)))
            self.end_headers()
            try:
                self.wfile.write(raw)
            except (BrokenPipeError, ConnectionResetError):
                pass

        def read_audio(self, limit):
            length = int(self.headers.get("Content-Length", "0"))
            if not 44 < length <= limit:
                raise ValueError("Invalid audio request size.")
            body = self.rfile.read(length)
            if len(body) != length:
                raise ValueError("Incomplete audio request.")
            return body

        def do_GET(self):
            path = urlsplit(self.path).path

            if path == "/health":
                self.reply(200, {
                    "ok": True,
                    "model": model_name,
                    "vad": "WebRTC",
                    "languages": LANGUAGE_HINTS,
                })
            elif path == "/status":
                self.reply(200, monitor.get())
            elif path in ("/monitor", "/monitor/"):
                page = Path(__file__).with_name(
                    "monitor.html"
                ).read_bytes()
                self.send_response(200)
                self.send_header(
                    "Content-Type", "text/html; charset=utf-8"
                )
                self.send_header("Cache-Control", "no-store")
                self.send_header("Content-Length", str(len(page)))
                self.end_headers()
                try:
                    self.wfile.write(page)
                except (BrokenPipeError, ConnectionResetError):
                    pass
            else:
                self.reply(404, {"ok": False, "error": "Not found."})

        def do_POST(self):
            path = urlsplit(self.path).path
            if path not in ("/vad", "/transcribe"):
                self.reply(404, {"ok": False, "error": "Not found."})
                return

            if path == "/vad":
                try:
                    self.connection.settimeout(5)
                    body = self.read_audio(512000)
                    _, rms, rate, channels, pcm = inspect_wav(
                        body, minimum=0.2, maximum=1.5
                    )
                    ratio, voiced_ms = voice_metrics(
                        rate, channels, pcm
                    )
                    self.reply(200, {
                        "ok": True,
                        "speech": ratio >= 0.40 and rms >= 0.0025,
                        "ratio": round(ratio, 3),
                        "voiced_ms": voiced_ms,
                    })
                except Exception as exc:
                    self.reply(400, {
                        "ok": False,
                        "error": str(exc)[:150],
                    })
                return

            if not transcription_lock.acquire(blocking=False):
                self.reply(409, {
                    "ok": False,
                    "error": "Recognition is busy.",
                })
                return

            try:
                self.connection.settimeout(25)
                body = self.read_audio(MAX_BYTES)
                seconds, rms, rate, channels, pcm = inspect_wav(
                    body, minimum=0.4, maximum=15
                )
                ratio, voiced_ms = voice_metrics(
                    rate, channels, pcm
                )

                if rms < 0.0025 or voiced_ms < MIN_VOICED_MS:
                    monitor.update(
                        state="silence",
                        text="",
                        language="",
                        languages=[],
                        audio_seconds=round(seconds, 2),
                        elapsed_seconds=0,
                        warning="VAD: no sufficient speech",
                        error="",
                    )
                    self.reply(200, {
                        "ok": True,
                        "text": "",
                        "language": "",
                        "languages": [],
                        "language_probability": 0.0,
                        "elapsed_seconds": 0,
                        "audio_seconds": round(seconds, 2),
                        "warning": "Rejected by local VAD",
                    })
                    return

                monitor.update(
                    state="processing", warning="", error=""
                )

                start = time.perf_counter()
                result = recognizer.transcribe(body)
                elapsed = round(
                    time.perf_counter() - start, 3
                )

                text = (result["text"] or "")[:500]
                languages = result["languages"]
                language = languages[0] if languages else ""

                payload = {
                    "ok": True,
                    "text": text,
                    "language": language,
                    "languages": languages,
                    "language_probability": 0.0,
                    "audio_seconds": round(seconds, 2),
                    "elapsed_seconds": elapsed,
                    "warning": "" if text else "No clear speech",
                }

                monitor.update(
                    state="done",
                    text=text,
                    language=language,
                    languages=languages,
                    audio_seconds=round(seconds, 2),
                    elapsed_seconds=elapsed,
                    warning=payload["warning"],
                    error="",
                )

                print(
                    f"[{datetime.now():%H:%M:%S}] "
                    f"{language or '?'} "
                    f"VAD={ratio:.2f} "
                    f"API={elapsed:.2f}s "
                    f"Text={text!r}",
                    flush=True,
                )

                self.reply(200, payload)

            except ValueError as exc:
                self.reply(400, {
                    "ok": False,
                    "error": str(exc),
                })
            except Exception as exc:
                print(
                    "[API ERROR]",
                    type(exc).__name__,
                    str(exc)[:250],
                    flush=True,
                )
                monitor.update(
                    state="error",
                    error="Check Python server window",
                )
                self.reply(502, {
                    "ok": False,
                    "error": "OpenAI API error. Check server window.",
                })
            finally:
                transcription_lock.release()

        def log_message(self, fmt, *args):
            path = urlsplit(self.path).path
            if path not in ("/vad", "/status"):
                super().log_message(fmt, *args)

    return Handler


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", default="gpt-transcribe")
    parser.add_argument("--port", type=int, default=8765)
    args = parser.parse_args()

    if not os.environ.get("OPENAI_API_KEY"):
        raise SystemExit("OPENAI_API_KEY is missing.")

    from openai import OpenAI

    client = OpenAI(timeout=25.0, max_retries=1)
    monitor = Monitor(args.model)

    server = ThreadingHTTPServer(
        ("127.0.0.1", args.port),
        make_handler(
            Recognizer(client, args.model),
            args.model,
            monitor,
        ),
    )
    server.daemon_threads = True

    print(
        f"READY model={args.model} VAD=WebRTC "
        f"http://127.0.0.1:{args.port}/health",
        flush=True,
    )
    print(
        f"Monitor: http://127.0.0.1:{args.port}/monitor",
        flush=True,
    )

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
