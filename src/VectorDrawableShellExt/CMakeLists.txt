cmake_minimum_required(VERSION 3.20)
project(VectorDrawableShellExt LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(VectorDrawableShellExt SHARED
    dllmain.cpp
    exports.cpp
    ClassFactory.cpp
    ThumbnailProvider.cpp
    PreviewHandler.cpp
    VectorDrawableShellExt.def
)

target_compile_definitions(VectorDrawableShellExt PRIVATE
    UNICODE
    _UNICODE
)

target_link_libraries(VectorDrawableShellExt PRIVATE
    shell32
    ole32
    uuid
    shlwapi
    gdiplus
)

set_target_properties(VectorDrawableShellExt PROPERTIES
    OUTPUT_NAME "VectorDrawableShellExt"
    PREFIX ""
)
