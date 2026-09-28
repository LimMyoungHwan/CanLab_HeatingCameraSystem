QT       += core gui serialport multimedia charts
greaterThan(QT_MAJOR_VERSION, 4): QT += widgets

CONFIG += c++17
TARGET = Canlab_Thermal_Viewer

SOURCES  += \
            Aboutdialog.cpp \
            Canlab_flash.c \
            Capture.cpp \
            Clserial.cpp \
            Histogram.cpp \
            Logdialog.cpp \
            Main.cpp \
            Mainwindow.cpp \
            Mflashtab.cpp \
            Viewer.cpp

HEADERS  += \
            Aboutdialog.h \
            Canlab_flash.h \
            Capture.h \
            Capturethread.h \
            Clserial.h \
            DataPacket.h \
            Histogram.h \
            Logdialog.h \
            Mainwindow.h \
            Mflashtab.h \
            Version.h \
            Viewer.h

FORMS    += \
            AboutDialog.ui \
            Logdialog.ui \
            Mainwindow.ui \
            Mflashtab.ui \
            Viewer.ui

DEFINES += USE_AS_LIBRARY

# 활성화: qmake CONFIG+=enable_pwm_tab  또는 아래 줄 주석 해제
CONFIG += enable_pwm_tab

enable_pwm_tab {
    DEFINES += ENABLE_PWM_TAB
    SOURCES += Pwmtab.cpp
    HEADERS += Pwmtab.h
    FORMS   += Pwmtab.ui
    message("[canlab_controller] PWM tab ENABLED")
}

RESOURCES += resources.qrc

# Camera viewer (OpenCV)
win32 {
    INCLUDEPATH += $$PWD/opencv-win/include
    LIBS += -L$$PWD/opencv-win/x64/mingw/lib \
            -lopencv_core4130 \
            -lopencv_imgproc4130 \
            -lopencv_highgui4130 \
            -lopencv_videoio4130
}

unix {
    CONFIG += link_pkgconfig
    PKGCONFIG += opencv4
}

# 기본 배포 규칙
qnx: target.path = /tmp/$${TARGET}/bin
else: unix:!android: target.path = /opt/$${TARGET}/bin
!isEmpty(target.path): INSTALLS += target



# ---- Copy runtime data (sbl, firmware) to exe folder ----
SBL_SRC_DIR = $$PWD/resources/sbl
FW_SRC_DIR  = $$PWD/resources/firmware

win32 {
    CONFIG(debug, debug|release):   DESTDIR = $$OUT_PWD/debug
    CONFIG(release, debug|release): DESTDIR = $$OUT_PWD/release
    SBL_DST_DIR = $$DESTDIR/resources/sbl
    FW_DST_DIR  = $$DESTDIR/resources/firmware
    SBL_SRC = $$system_path($$SBL_SRC_DIR)
    SBL_DST = $$system_path($$SBL_DST_DIR)
    FW_SRC  = $$system_path($$FW_SRC_DIR)
    FW_DST  = $$system_path($$FW_DST_DIR)

    QMAKE_POST_LINK = cmd /c ^ if not exist "$$SBL_DST" mkdir "$$SBL_DST" ^&^& ^ copy /Y "$$SBL_SRC\\*.*" "$$SBL_DST\\" ^&^& ^ xcopy /Y /E /I "$$FW_SRC" "$$FW_DST"
}

unix {
    SBL_DST_DIR = $$OUT_PWD/resources/sbl
    FW_DST_DIR  = $$OUT_PWD/resources/firmware
    QMAKE_POST_LINK += $$quote(mkdir -p "$$SBL_DST_DIR") $$escape_expand(\n\t)
    QMAKE_POST_LINK += $$quote(rsync -a --delete "$$SBL_SRC_DIR/" "$$SBL_DST_DIR/") $$escape_expand(\n\t)
    QMAKE_POST_LINK += $$quote(mkdir -p "$$FW_DST_DIR") $$escape_expand(\n\t)
    QMAKE_POST_LINK += $$quote(rsync -a --delete "$$FW_SRC_DIR/" "$$FW_DST_DIR/") $$escape_expand(\n\t)
}
