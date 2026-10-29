cmake_minimum_required(VERSION 3.20)
project(NativeE2eTest LANGUAGES CXX)

add_executable(NativeE2eTest NativeE2eTest.cpp)
target_link_libraries(NativeE2eTest PRIVATE ole32 shlwapi)
