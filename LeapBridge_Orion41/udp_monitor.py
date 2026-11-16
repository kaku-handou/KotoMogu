"""KotoMogu LeapBridge Stage 1: local-only UDP monitor; no packages required."""
import json
import socket
import time

with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
    sock.bind(("127.0.0.1", 8767))
    sock.settimeout(3)
    print("Listening on 127.0.0.1:8767 (Ctrl+C to stop)", flush=True)
    previous = 0.0
    while True:
        try:
            payload, _ = sock.recvfrom(4096)
        except socket.timeout:
            print("No UDP data yet. Is LeapBridge.exe running?", flush=True)
            continue
        now = time.monotonic()
        if now - previous < .3:
            continue
        previous = now
        try:
            data = json.loads(payload.decode("utf-8"))
        except (ValueError, UnicodeDecodeError) as exc:
            print("Invalid data:", exc)
            continue
        hands = data.get("hands", [])
        if not hands:
            print("0 hands")
        else:
            for hand in hands:
                print(f"{hand.get('hand')}: X={hand.get('x')} Y={hand.get('y')} Z={hand.get('z')}")
