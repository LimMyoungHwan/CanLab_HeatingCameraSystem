#ifndef CLSERIAL_H
#define CLSERIAL_H

#include <QObject>
#include <QSerialPort>
#include <QByteArray>
#include <QString>
#include <QElapsedTimer>
#include "DataPacket.h"

class CLSerial : public QObject
{
    Q_OBJECT
public:
    explicit CLSerial(QObject* parent = nullptr);
    ~CLSerial() override;

    void Initialize(const QString& portName, int baudRate);
    bool Open();
    void Close();

    bool isOpen() const { return m_port && m_port->isOpen(); }
    QString portName() const { return m_portName; }
    int baudRate() const { return m_baudRate; }

    bool sendAndRecv(const ClCmdPkt& cmd, ClRspPkt& rsp, int timeoutMs = 200);

signals:
    void DataReceived(const QByteArray bytes);
    void Opened(const QString& portName);
    void Closed();
    void ErrorOccured(const QString& message);

private slots:
    void onReadyRead();
    void onErrorOccurred(QSerialPort::SerialPortError err);

private:
    QSerialPort* m_port = nullptr;

    QString m_portName;
    int m_baudRate = 0;
    QByteArray m_rxBuf;
};

#endif // CLSERIAL_H
