cmake_minimum_required(VERSION 3.20)
project(EastwardModToolNative LANGUAGES CXX)

set(LUAJIT_DECOMPILER_SOURCE "${CMAKE_CURRENT_SOURCE_DIR}/decompiler" CACHE PATH "Vendored decompiler core")
if(MSVC)
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()
add_subdirectory("${CMAKE_CURRENT_SOURCE_DIR}/../EastwardLib/Native" bridge)
install(FILES "${CMAKE_CURRENT_SOURCE_DIR}/decompiler/NOTICE.txt" DESTINATION licenses RENAME luajit-decompiler-NOTICE.txt)
