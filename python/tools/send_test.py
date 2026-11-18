import json
import socket

UNITY_ADDRESS = ("127.0.0.1", 5005)

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

while True:
    name = input("動きの名前 > ")

    message = {"type": "play", "action": name}
    data = json.dumps(message).encode("utf-8")
    sock.sendto(data, UNITY_ADDRESS)

    print("送信:", name)