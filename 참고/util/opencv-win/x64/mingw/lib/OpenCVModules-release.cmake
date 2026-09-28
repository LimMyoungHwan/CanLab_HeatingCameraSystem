#----------------------------------------------------------------
# Generated CMake target import file for configuration "Release".
#----------------------------------------------------------------

# Commands may need to know the format version.
set(CMAKE_IMPORT_FILE_VERSION 1)

# Import target "opencv_core" for configuration "Release"
set_property(TARGET opencv_core APPEND PROPERTY IMPORTED_CONFIGURATIONS RELEASE)
set_target_properties(opencv_core PROPERTIES
  IMPORTED_IMPLIB_RELEASE "${_IMPORT_PREFIX}/x64/mingw/lib/libopencv_core4130.dll.a"
  IMPORTED_LOCATION_RELEASE "${_IMPORT_PREFIX}/x64/mingw/bin/libopencv_core4130.dll"
  )

list(APPEND _IMPORT_CHECK_TARGETS opencv_core )
list(APPEND _IMPORT_CHECK_FILES_FOR_opencv_core "${_IMPORT_PREFIX}/x64/mingw/lib/libopencv_core4130.dll.a" "${_IMPORT_PREFIX}/x64/mingw/bin/libopencv_core4130.dll" )

# Import target "opencv_imgproc" for configuration "Release"
set_property(TARGET opencv_imgproc APPEND PROPERTY IMPORTED_CONFIGURATIONS RELEASE)
set_target_properties(opencv_imgproc PROPERTIES
  IMPORTED_IMPLIB_RELEASE "${_IMPORT_PREFIX}/x64/mingw/lib/libopencv_imgproc4130.dll.a"
  IMPORTED_LOCATION_RELEASE "${_IMPORT_PREFIX}/x64/mingw/bin/libopencv_imgproc4130.dll"
  )

list(APPEND _IMPORT_CHECK_TARGETS opencv_imgproc )
list(APPEND _IMPORT_CHECK_FILES_FOR_opencv_imgproc "${_IMPORT_PREFIX}/x64/mingw/lib/libopencv_imgproc4130.dll.a" "${_IMPORT_PREFIX}/x64/mingw/bin/libopencv_imgproc4130.dll" )

# Import target "opencv_photo" for configuration "Release"
set_property(TARGET opencv_photo APPEND PROPERTY IMPORTED_CONFIGURATIONS RELEASE)
set_target_properties(opencv_photo PROPERTIES
  IMPORTED_IMPLIB_RELEASE "${_IMPORT_PREFIX}/x64/mingw/lib/libopencv_photo4130.dll.a"
  IMPORTED_LOCATION_RELEASE "${_IMPORT_PREFIX}/x64/mingw/bin/libopencv_photo4130.dll"
  )

list(APPEND _IMPORT_CHECK_TARGETS opencv_photo )
list(APPEND _IMPORT_CHECK_FILES_FOR_opencv_photo "${_IMPORT_PREFIX}/x64/mingw/lib/libopencv_photo4130.dll.a" "${_IMPORT_PREFIX}/x64/mingw/bin/libopencv_photo4130.dll" )

# Import target "opencv_imgcodecs" for configuration "Release"
set_property(TARGET opencv_imgcodecs APPEND PROPERTY IMPORTED_CONFIGURATIONS RELEASE)
set_target_properties(opencv_imgcodecs PROPERTIES
  IMPORTED_IMPLIB_RELEASE "${_IMPORT_PREFIX}/x64/mingw/lib/libopencv_imgcodecs4130.dll.a"
  IMPORTED_LOCATION_RELEASE "${_IMPORT_PREFIX}/x64/mingw/bin/libopencv_imgcodecs4130.dll"
  )

list(APPEND _IMPORT_CHECK_TARGETS opencv_imgcodecs )
list(APPEND _IMPORT_CHECK_FILES_FOR_opencv_imgcodecs "${_IMPORT_PREFIX}/x64/mingw/lib/libopencv_imgcodecs4130.dll.a" "${_IMPORT_PREFIX}/x64/mingw/bin/libopencv_imgcodecs4130.dll" )

# Import target "opencv_videoio" for configuration "Release"
set_property(TARGET opencv_videoio APPEND PROPERTY IMPORTED_CONFIGURATIONS RELEASE)
set_target_properties(opencv_videoio PROPERTIES
  IMPORTED_IMPLIB_RELEASE "${_IMPORT_PREFIX}/x64/mingw/lib/libopencv_videoio4130.dll.a"
  IMPORTED_LOCATION_RELEASE "${_IMPORT_PREFIX}/x64/mingw/bin/libopencv_videoio4130.dll"
  )

list(APPEND _IMPORT_CHECK_TARGETS opencv_videoio )
list(APPEND _IMPORT_CHECK_FILES_FOR_opencv_videoio "${_IMPORT_PREFIX}/x64/mingw/lib/libopencv_videoio4130.dll.a" "${_IMPORT_PREFIX}/x64/mingw/bin/libopencv_videoio4130.dll" )

# Import target "opencv_highgui" for configuration "Release"
set_property(TARGET opencv_highgui APPEND PROPERTY IMPORTED_CONFIGURATIONS RELEASE)
set_target_properties(opencv_highgui PROPERTIES
  IMPORTED_IMPLIB_RELEASE "${_IMPORT_PREFIX}/x64/mingw/lib/libopencv_highgui4130.dll.a"
  IMPORTED_LINK_DEPENDENT_LIBRARIES_RELEASE "Qt5::Core;Qt5::Gui;Qt5::Widgets;Qt5::Test;Qt5::Concurrent;Qt5::OpenGL"
  IMPORTED_LOCATION_RELEASE "${_IMPORT_PREFIX}/x64/mingw/bin/libopencv_highgui4130.dll"
  )

list(APPEND _IMPORT_CHECK_TARGETS opencv_highgui )
list(APPEND _IMPORT_CHECK_FILES_FOR_opencv_highgui "${_IMPORT_PREFIX}/x64/mingw/lib/libopencv_highgui4130.dll.a" "${_IMPORT_PREFIX}/x64/mingw/bin/libopencv_highgui4130.dll" )

# Commands beyond this point should not need to know the version.
set(CMAKE_IMPORT_FILE_VERSION)
