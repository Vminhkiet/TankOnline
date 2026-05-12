find_package(fmt CONFIG REQUIRED)

file(GLOB_RECURSE LC_SOURCES "src/*.cpp")

add_executable(load_client ${LC_SOURCES})

target_include_directories(load_client PRIVATE
    include
    ../server_tank/include      # Packets.hpp, Opcode.hpp, NetworkConstants.h
    ../bit_packing/src          # WriteStream.h, BitWriter.h, Serialization.h
    D:/Library/glm-1.0.1-light
)

target_compile_options(load_client PRIVATE /utf-8)

target_link_libraries(load_client PRIVATE
    ws2_32
    fmt::fmt
    bit_packing
)
