#ifndef capturethread_H
#define capturethread_H
#pragma once
#include <QThread>
#include <QImage>
#include "Capture.h"

class capturethread : public QThread {
    Q_OBJECT
public:
    capturethread(Capture* cap, QObject* parent = nullptr)
        : QThread(parent), m_cap(cap), m_running(false) {}

    void startCapture() { m_running = true; start(); }
    void stopCapture() {
        if (!m_running) {
            qWarning() << "⚠️ stopCapture called but already stopped";
            return;
        }

        m_running = false;

        try {
            wait(2000);
        } catch (...) {
            qWarning() << "⚠️ stopCapture wait timeout or exception";
        }
    }


signals:
    void frameReady(const QImage&);

protected:
    void run() override {
        while (m_running) {
            QImage frame = m_cap->getFrame();
            if (!frame.isNull()) {
                emit frameReady(frame.copy());
            }
            msleep(1);
        }
    }

private:
    Capture* m_cap;
    bool m_running;
};
#endif // capturethread_H
