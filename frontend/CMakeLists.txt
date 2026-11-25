cmake_minimum_required(VERSION 3.29)
project(hosihikari-ir-frontend LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

set(HOSIHIKARI_ZLIB_ROOT "" CACHE PATH "zlib install root")
set(HOSIHIKARI_ZSTD_ROOT "" CACHE PATH "zstd install root")

if(WIN32)
  find_program(VSWHERE_EXE vswhere.exe PATHS "$ENV{ProgramFiles\(x86\)}/Microsoft Visual Studio/Installer")
  if(VSWHERE_EXE)
    execute_process(
      COMMAND "${VSWHERE_EXE}" -latest -products * -property installationPath
      OUTPUT_VARIABLE VS_ROOT
      OUTPUT_STRIP_TRAILING_WHITESPACE
    )
  endif()

  if(NOT TARGET DIASDK::Diaguids)
    add_library(DIASDK::Diaguids STATIC IMPORTED)
    if(VS_ROOT)
      set_target_properties(
        DIASDK::Diaguids
        PROPERTIES
          IMPORTED_LOCATION "${VS_ROOT}/DIA SDK/lib/amd64/diaguids.lib"
      )
      target_include_directories(DIASDK::Diaguids INTERFACE "${VS_ROOT}/DIA SDK/include")
    endif()
  endif()

  if(NOT TARGET ZLIB::ZLIB)
    if(HOSIHIKARI_ZLIB_ROOT)
      # Package stores spell the static library either way; take whichever is present.
      set(_hosihikari_zlib "")
      foreach(_candidate "zlibstatic.lib" "zlib.lib")
        if(EXISTS "${HOSIHIKARI_ZLIB_ROOT}/lib/${_candidate}")
          set(_hosihikari_zlib "${HOSIHIKARI_ZLIB_ROOT}/lib/${_candidate}")
          break()
        endif()
      endforeach()
    endif()
    if(_hosihikari_zlib)
      add_library(ZLIB::ZLIB STATIC IMPORTED)
      set_target_properties(
        ZLIB::ZLIB
        PROPERTIES IMPORTED_LOCATION "${_hosihikari_zlib}"
      )
      target_include_directories(ZLIB::ZLIB INTERFACE "${HOSIHIKARI_ZLIB_ROOT}/include")
    else()
      add_library(ZLIB::ZLIB INTERFACE IMPORTED)
    endif()
  endif()

  if(NOT TARGET zstd::libzstd_static)
    if(HOSIHIKARI_ZSTD_ROOT AND EXISTS "${HOSIHIKARI_ZSTD_ROOT}/lib/zstd_static.lib")
      add_library(zstd::libzstd_static STATIC IMPORTED)
      set_target_properties(
        zstd::libzstd_static
        PROPERTIES IMPORTED_LOCATION "${HOSIHIKARI_ZSTD_ROOT}/lib/zstd_static.lib"
      )
      target_include_directories(zstd::libzstd_static INTERFACE "${HOSIHIKARI_ZSTD_ROOT}/include")
    else()
      add_library(zstd::libzstd_static INTERFACE IMPORTED)
    endif()
  endif()
  if(NOT TARGET LibXml2::LibXml2)
    add_library(LibXml2::LibXml2 INTERFACE IMPORTED)
  endif()
endif()

find_package(Clang REQUIRED CONFIG)
find_package(LLVM REQUIRED CONFIG)
find_package(Threads REQUIRED)

add_executable(hosihikari-ir-frontend src/main.cpp)
target_include_directories(hosihikari-ir-frontend PRIVATE ${LLVM_INCLUDE_DIRS} ${CLANG_INCLUDE_DIRS})
if(MSVC)
  target_compile_options(hosihikari-ir-frontend PRIVATE /utf-8)
  # Endstone's public umbrella reaches deeply nested template instantiations;
  # the default 1 MiB main-thread stack overflows inside clang's AST.
  target_link_options(hosihikari-ir-frontend PRIVATE /STACK:67108864)
endif()
target_link_libraries(
  hosihikari-ir-frontend
  PRIVATE
    clangTooling
    clangFrontend
    clangSerialization
    clangDriver
    clangParse
    clangSema
    clangAnalysis
    clangAST
    clangBasic
    Threads::Threads
)

add_executable(hosihikari-symbol-ir-frontend symbols/main.cpp)
target_include_directories(hosihikari-symbol-ir-frontend PRIVATE ${LLVM_INCLUDE_DIRS})
if(MSVC)
  target_compile_options(hosihikari-symbol-ir-frontend PRIVATE /utf-8)
  target_link_options(hosihikari-symbol-ir-frontend PRIVATE /STACK:67108864)
endif()
target_link_libraries(
  hosihikari-symbol-ir-frontend
  PRIVATE
    LLVMDebugInfoPDB
    LLVMDebugInfoCodeView
    LLVMDemangle
    LLVMObject
    LLVMBinaryFormat
    LLVMSupport
)
