#include "Capture.h"

Capture::Capture()
    : m_width(0), m_height(0), m_pixelformat(0) {}

Capture::~Capture() {
    closeDevice();
}

#ifdef _WIN32
// ===== Windows =====
bool Capture::openDevice(int index, int w, int h)
{
    if (!m_cap.open(index, cv::CAP_DSHOW)) {
        qWarning() << "❌ Failed to open camera index" << index << "(DShow)";
        return false;
    }

    m_cap.set(cv::CAP_PROP_FRAME_WIDTH,  w);
    m_cap.set(cv::CAP_PROP_FRAME_HEIGHT, h);
    m_cap.set(cv::CAP_PROP_CONVERT_RGB, 0);
    m_cap.set(cv::CAP_PROP_FOURCC, cv::VideoWriter::fourcc('Y','1','6',' '));

    return true;
}
#else
// ===== Linux =====
bool Capture::openDevice(const std::string& device, int w, int h, int fourcc)
{
    m_cap.open(device, cv::CAP_V4L2);
    if (!m_cap.isOpened()) {
        qWarning() << "❌ Failed to open device:" << QString::fromStdString(device);
        return false;
    }

    m_cap.set(cv::CAP_PROP_FRAME_WIDTH,  w);
    m_cap.set(cv::CAP_PROP_FRAME_HEIGHT, h);

    if (fourcc == 0) fourcc = cv::VideoWriter::fourcc('Y','1','6',' ');
    m_cap.set(cv::CAP_PROP_FOURCC, fourcc);
    m_cap.set(cv::CAP_PROP_CONVERT_RGB, 0);

    return true;
}

QString getCameraCardName(const QString &devPath) {
    int fd = open(devPath.toStdString().c_str(), O_RDWR);
    if (fd < 0) return QString();

    struct v4l2_capability cap;
    memset(&cap, 0, sizeof(cap));

    if (ioctl(fd, VIDIOC_QUERYCAP, &cap) < 0) {
        close(fd);
        return QString();
    }
    close(fd);

    return QString::fromUtf8(reinterpret_cast<char*>(cap.card));
}

bool Capture::openDefaultCamera(const QString &targetName, int w, int h, int fourcc) {
    QDir devDir("/dev");
    QStringList filters;
    filters << "video*";
    QFileInfoList videoDevices = devDir.entryInfoList(filters, QDir::System | QDir::Readable);

    for (const QFileInfo &info : videoDevices) {
        QString devPath = info.absoluteFilePath();
        QString cardName = getCameraCardName(devPath);

        if (cardName.contains(targetName)) {
            qDebug() << "✅ Auto-selecting camera:" << devPath;
            return openDevice(devPath.toStdString(), w, h, fourcc);
        }
    }

    qWarning() << "❌ No matching camera found for:" << targetName;
    return false;
}
#endif

void Capture::closeDevice() {
    if (m_cap.isOpened()) {
        m_cap.release();
    }
}

QImage Capture::getFrame() {
    if (!m_cap.isOpened()) return QImage();

    cv::Mat frame;
    if (!m_cap.read(frame)) {
        qWarning() << "⚠️ Failed to grab frame";
        return QImage();
    }

    if (frame.type() == CV_16UC1) {
        // === Y16 ===
        return QImage((const uchar*)frame.data, frame.cols, frame.rows,
                      frame.step, QImage::Format_Grayscale16).copy();
    }
    else if (frame.type() == CV_8UC2) {
        // === UYVY (YUV422 → RGB) ===
        cv::Mat rgb;
        cv::cvtColor(frame, rgb, cv::COLOR_YUV2RGB_UYVY);
        QImage img(rgb.cols, rgb.rows, QImage::Format_RGB888);
        for (int y = 0; y < rgb.rows; ++y)
            memcpy(img.scanLine(y), rgb.ptr(y), rgb.cols * 3);
        return img;
    }
    else if (frame.type() == CV_8UC3) {
        // === 이미 BGR/RGB 3채널인 경우 ===
        QImage img(frame.cols, frame.rows, QImage::Format_BGR888);
        for (int y = 0; y < frame.rows; ++y)
            memcpy(img.scanLine(y), frame.ptr(y), frame.cols * 3);
        return img;
    }else{
        qWarning() << "⚠️ Unexpected frame type:" << frame.type()
                   << " depth:" << frame.depth()
                   << " channels:" << frame.channels();
    }
    return QImage();
}


bool Capture::queryFormat() {
    if (!m_cap.isOpened()) {
        qWarning("queryFormat: device not open");
        return false;
    }

    m_width       = static_cast<int>(m_cap.get(cv::CAP_PROP_FRAME_WIDTH));
    m_height      = static_cast<int>(m_cap.get(cv::CAP_PROP_FRAME_HEIGHT));
    m_pixelformat = static_cast<int>(m_cap.get(cv::CAP_PROP_FOURCC));

    qDebug() << "📷 Current format:" << m_width << "x" << m_height
             << "pixelformat=0x" << Qt::hex << m_pixelformat;

    return true;
}
