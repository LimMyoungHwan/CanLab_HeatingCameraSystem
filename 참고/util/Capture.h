#ifndef CAPTURE_H
#define CAPTURE_H

#include <QString>
#include <QImage>
#include <QDebug>
#include <QFileInfo>
#include <QDir>

#include <opencv2/opencv.hpp>
#include <string>

#ifndef _WIN32
#include <linux/videodev2.h>
#include <sys/ioctl.h>
#include <fcntl.h>
#include <unistd.h>
#endif

class Capture {
public:
    Capture();
    ~Capture();

#ifdef _WIN32
    // Windows: 카메라 index 기반
    bool openDevice(int index, int w, int h);
#else
    // Linux: 장치 경로(/dev/videoX) 기반
    bool openDevice(const std::string& device, int w, int h, int fourcc);
    bool openDefaultCamera(const QString &targetName, int w, int h, int fourcc);
#endif

    void closeDevice();

    // 프레임 읽기
    QImage getFrame();
    bool queryFormat();

    // Getter
    int getWidth() const { return m_width; }
    int getHeight() const { return m_height; }
    int getPixelFormat() const { return m_pixelformat; }

private:
    cv::VideoCapture m_cap;
    int m_width;
    int m_height;
    int m_pixelformat;
};

// ==============================
// Utility Function (Linux 전용)
// ==============================
#ifndef _WIN32
QString getCameraCardName(const QString &devPath);
#endif

#endif // CAPTURE_H
