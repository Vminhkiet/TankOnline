cmake_minimum_required(VERSION 3.10)

# Khai báo tên toàn bộ Workspace
project(TankGame CXX)

# Thiết lập C++20 cho TẤT CẢ các project con bên trong
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Gọi các thư mục con (CMake sẽ tự động chạy vào trong và đọc file CMake của chúng)
add_subdirectory(bit_packing)
add_subdirectory(server_tank)
add_subdirectory(server_baseline)
add_subdirectory(load_client)