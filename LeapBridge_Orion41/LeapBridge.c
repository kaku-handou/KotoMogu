/* KotoMogu LeapBridge: reads Orion 4.1 LeapC tracking data and sends
 * localhost UDP JSON on port 8767. Uses the user's existing SDK's
 * ExampleConnection.c/.h; no SDK binaries or source are redistributed.
 * Prototype: NOT yet an input recognized by Unity KotoMogu.
 */
#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <stdio.h>
#include <stdint.h>
#include <string.h>
#include "LeapC.h"
#include "ExampleConnection.h"

#pragma comment(lib, "Ws2_32.lib")

int main(void) {
  WSADATA wsa;
  SOCKET udp = INVALID_SOCKET;
  struct sockaddr_in dest;
  int64_t last_frame_id = -1;
  ULONGLONG last_send_ms = 0;
  ULONGLONG last_log_ms = 0;
  int last_hand_count = -1;

  if (WSAStartup(MAKEWORD(2,2), &wsa) != 0) {
    fprintf(stderr, "WSAStartup failed.\n");
    return 1;
  }
  udp = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
  if (udp == INVALID_SOCKET) {
    fprintf(stderr, "Cannot create UDP socket.\n");
    WSACleanup();
    return 1;
  }
  memset(&dest, 0, sizeof(dest));
  dest.sin_family = AF_INET;
  dest.sin_port = htons(8767);
  if (inet_pton(AF_INET, "127.0.0.1", &dest.sin_addr) != 1) {
    fprintf(stderr, "Invalid destination.\n");
    closesocket(udp);
    WSACleanup();
    return 1;
  }

  printf("LeapBridge Orion 4.1: UDP 127.0.0.1:8767\n");
  printf("Waiting for Leap Motion service... (Ctrl+C to stop)\n");
  OpenConnection();
  ULONGLONG start_ms = GetTickCount64();
  while (!IsConnected) {
    if (GetTickCount64() - start_ms > 15000) {
      fprintf(stderr, "Leap service connection timed out. Check Leap Motion Software.\n");
      closesocket(udp);
      WSACleanup();
      return 2;
    }
    millisleep(100);
  }
  printf("Connected to Leap Motion service. Move a hand above the device.\n");

  for (;;) {
    LEAP_TRACKING_EVENT *frame = GetFrame();
    if (!frame || frame->tracking_frame_id <= last_frame_id) {
      millisleep(3);
      continue;
    }
    last_frame_id = frame->tracking_frame_id;
    ULONGLONG now = GetTickCount64();
    if (now - last_send_ms < 33) {
      millisleep(2);
      continue;
    }
    last_send_ms = now;

    char packet[1024];
    int written = snprintf(packet, sizeof(packet),
      "{\"source\":\"leap-gen1\",\"frame\":%lld,\"hands\":[",
      (long long)frame->tracking_frame_id);
    if (written < 0 || written >= (int)sizeof(packet)) continue;
    size_t used = (size_t)written;
    uint32_t count = frame->nHands;
    if (count > 2) count = 2; /* first prototype handles 2 hands */
    for (uint32_t i = 0; i < count; ++i) {
      const LEAP_HAND *hand = &frame->pHands[i];
      written = snprintf(packet + used, sizeof(packet) - used,
        "%s{\"id\":%d,\"hand\":\"%s\",\"x\":%.2f,\"y\":%.2f,\"z\":%.2f}",
        i ? "," : "", (int)hand->id,
        hand->type == eLeapHandType_Left ? "left" : "right",
        hand->palm.position.x, hand->palm.position.y, hand->palm.position.z);
      if (written < 0 || (size_t)written >= sizeof(packet) - used) break;
      used += (size_t)written;
    }
    if (used + 3 >= sizeof(packet)) continue;
    packet[used++] = ']';
    packet[used++] = '}';
    packet[used] = '\0';
    sendto(udp, packet, (int)used, 0, (const struct sockaddr *)&dest, sizeof(dest));

    if ((int)count != last_hand_count || now - last_log_ms >= 1000) {
      printf("Frame %lld: %u hands; UDP -> 127.0.0.1:8767\n",
        (long long)frame->tracking_frame_id, (unsigned)count);
      fflush(stdout);
      last_log_ms = now;
      last_hand_count = (int)count;
    }
  }
  /* Ctrl+C stops the process; no normal exit from this simple prototype. */
}
