# The C++ benchmark plugin (GDExtension). Builds bin/loonie_bench.<target>.dll for the Godot project.
#   cmake -S cpp -B cpp/build -G Ninja -DCMAKE_BUILD_TYPE=Release -DGODOT_CPP_DIR=<path to godot-cpp 4.5>
#   cmake --build cpp/build
cmake_minimum_required(VERSION 3.17)
project(loonie_bench LANGUAGES CXX)

set(GODOT_CPP_DIR "" CACHE PATH "A checkout of github.com/godotengine/godot-cpp (branch 4.5)")
add_subdirectory(${GODOT_CPP_DIR} godot-cpp)

add_library(loonie_bench SHARED src/bench.cpp src/register_types.cpp)
target_link_libraries(loonie_bench PRIVATE godot-cpp)
get_target_property(GODOTCPP_SUFFIX godot-cpp GODOTCPP_SUFFIX)
set(OUTPUT_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../bin/")
set_target_properties(
    loonie_bench
    PROPERTIES
        CXX_STANDARD 17
        LIBRARY_OUTPUT_DIRECTORY "$<1:${OUTPUT_DIR}>"
        RUNTIME_OUTPUT_DIRECTORY "$<1:${OUTPUT_DIR}>"
        PDB_OUTPUT_DIRECTORY "$<1:${OUTPUT_DIR}>"
        OUTPUT_NAME "loonie_bench${GODOTCPP_SUFFIX}"
)
