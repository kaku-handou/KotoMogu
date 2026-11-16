cmake_minimum_required(VERSION 3.10)
project(KotoMoguLeapBridge C)

if(NOT DEFINED LEAP_SDK_ROOT)
  message(FATAL_ERROR "Set -DLEAP_SDK_ROOT=path/to/LeapSDK")
endif()
file(TO_CMAKE_PATH "${LEAP_SDK_ROOT}" LEAP_SDK_ROOT)
if(NOT EXISTS "${LEAP_SDK_ROOT}/include/LeapC.h" OR
   NOT EXISTS "${LEAP_SDK_ROOT}/samples/ExampleConnection.c")
  message(FATAL_ERROR "LeapSDK header or ExampleConnection sample not found in ${LEAP_SDK_ROOT}")
endif()

find_package(LeapSDK 4.1.0 REQUIRED CONFIG PATHS "${LEAP_SDK_ROOT}" NO_DEFAULT_PATH)
add_executable(LeapBridge
  LeapBridge.c
  "${LEAP_SDK_ROOT}/samples/ExampleConnection.c"
)
target_include_directories(LeapBridge PRIVATE
  "${LEAP_SDK_ROOT}/include"
  "${LEAP_SDK_ROOT}/samples"
)
target_link_libraries(LeapBridge PRIVATE LeapSDK::LeapC ws2_32)
add_custom_command(TARGET LeapBridge POST_BUILD
  COMMAND "${CMAKE_COMMAND}" -E copy_if_different
    "${LEAP_SDK_ROOT}/lib/x64/LeapC.dll"
    "$<TARGET_FILE_DIR:LeapBridge>"
)
