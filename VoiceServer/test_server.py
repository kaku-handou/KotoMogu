"""Offline integration/transport tests. Never contact OpenAI or charge API credits."""
import io
import json
import math
import struct
import threading
import unittest
import urllib.error
import urllib.request
import wave
from http.server import ThreadingHTTPServer

from server import Monitor, OpenAIRecognizer, make_handler, validate_wav, _codes


def audio(silent=False):
    samples = b"".join(struct.pack("<h", 0 if silent else int(5000 * math.sin(i * .15)))
                       for i in range(16000))
    data = io.BytesIO()
    with wave.open(data, "wb") as wav:
        wav.setparams((1, 2, 16000, 0, "NONE", "not compressed"))
        wav.writeframes(samples)
    return data.getvalue()


class FakeRecognizer:
    def __init__(self):
        self.text = "ジャンプして"
        self.languages = ["ja"]
        self.calls = 0
        self.fail = False

    def transcribe(self, wav_bytes):
        self.calls += 1
        if self.fail:
            raise RuntimeError("synthetic service failure")
        return {"text": self.text, "languages": self.languages}


class ServerChecks(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.recognizer = FakeRecognizer()
        cls.monitor = Monitor("gpt-transcribe")
        cls.http = ThreadingHTTPServer(
            ("127.0.0.1", 0),
            make_handler(cls.recognizer, "gpt-transcribe", cls.monitor),
        )
        cls.worker = threading.Thread(target=cls.http.serve_forever, daemon=True)
        cls.worker.start()
        cls.url = "http://127.0.0.1:" + str(cls.http.server_port)

    @classmethod
    def tearDownClass(cls):
        cls.http.shutdown()
        cls.http.server_close()
        cls.worker.join()

    def setUp(self):
        self.recognizer.text = "ジャンプして"
        self.recognizer.languages = ["ja"]
        self.recognizer.fail = False

    def request(self, path="/transcribe", data=None):
        request = urllib.request.Request(self.url + path, data=data)
        try:
            with urllib.request.urlopen(request, timeout=5) as response:
                return response.status, json.load(response)
        except urllib.error.HTTPError as response:
            return response.code, json.load(response)

    def test_health(self):
        code, result = self.request("/health")
        self.assertEqual(code, 200)
        self.assertEqual(result["model"], "gpt-transcribe")
        self.assertEqual(result["device"], "OpenAI API")

    def test_original_language_and_unity_compatibility(self):
        for language, text in [("ja", "ジャンプして"), ("zh", "请跳一下"),
                               ("en", "Jump please"), ("fr", "Bonjour")]:
            self.recognizer.languages = [language]
            self.recognizer.text = text
            code, result = self.request(data=audio())
            self.assertEqual(code, 200)
            self.assertTrue(result["ok"])
            self.assertEqual(result["language"], language)
            self.assertEqual(result["text"], text)
            self.assertIn("elapsed_seconds", result)
            self.assertIn("audio_seconds", result)
            self.assertIn("language_probability", result)

    def test_no_language_guess(self):
        self.recognizer.languages = []
        code, result = self.request(data=audio())
        self.assertEqual(code, 200)
        self.assertEqual(result["text"], "ジャンプして")
        self.assertEqual(result["language"], "")
        self.assertEqual(result["languages"], [])

    def test_silence_skips_api(self):
        calls = self.recognizer.calls
        _, result = self.request(data=audio(True))
        self.assertEqual(result["text"], "")
        self.assertEqual(self.recognizer.calls, calls)

    def test_invalid_wav(self):
        code, _ = self.request(data=b"bad" * 200)
        self.assertEqual(code, 400)

    def test_api_error_sanitized(self):
        self.recognizer.fail = True
        code, result = self.request(data=audio())
        self.assertEqual(code, 502)
        self.assertNotIn("synthetic service failure", result["error"])

    def test_monitor_read_only_status(self):
        code, result = self.request("/status")
        self.assertEqual(code, 200)
        self.assertIn("history", result)
        with urllib.request.urlopen(self.url + "/monitor", timeout=5) as res:
            self.assertEqual(res.status, 200)
            self.assertIn(b"KOTOMOGU", res.read())

    def test_unknown_path(self):
        code, _ = self.request("/unknown")
        self.assertEqual(code, 404)

    def test_language_object_normalization(self):
        class Lang:
            code = "ja"
        self.assertEqual(_codes([Lang(), {"code": "zh"}, {"code": "ja"}]), ["ja", "zh"])

    def test_sdk_adapter_parameters_no_network(self):
        from types import SimpleNamespace

        class Endpoint:
            def create(self, **kwargs):
                self.kwargs = kwargs
                return SimpleNamespace(text="你好", languages=[SimpleNamespace(code="zh")])

        endpoint = Endpoint()
        client = SimpleNamespace(audio=SimpleNamespace(transcriptions=endpoint))
        recognizer = OpenAIRecognizer(client)
        result = recognizer.transcribe(audio())
        self.assertEqual(result, {"text": "你好", "languages": ["zh"]})
        self.assertEqual(endpoint.kwargs["model"], "gpt-transcribe")
        self.assertEqual(endpoint.kwargs["file"].name, "visitor.wav")
        self.assertEqual(endpoint.kwargs["extra_body"]["languages"][:2], ["ja", "zh"])
        self.assertNotIn("language", endpoint.kwargs)

    def test_wav_metrics(self):
        seconds, rms = validate_wav(audio())
        self.assertAlmostEqual(seconds, 1.0)
        self.assertGreater(rms, 0.01)


if __name__ == "__main__":
    unittest.main(verbosity=2)
