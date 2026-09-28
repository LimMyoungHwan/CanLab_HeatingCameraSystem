#include "Clserial.h"
#include <QMessageBox>

CLSerial::CLSerial(QObject* parent)
    : QObject(parent),
      m_port(new QSerialPort(this))
{

}

CLSerial::~CLSerial()
{
    Close();
}

void CLSerial::Initialize(const QString& portName, int baudRate)
{
    if (m_port && m_port->isOpen())
        m_port->close();

    m_portName = portName;
    m_baudRate = baudRate;

    m_port->setPortName(m_portName);
    m_port->setBaudRate(m_baudRate);
    m_port->setDataBits(QSerialPort::Data8);
    m_port->setParity(QSerialPort::NoParity);
    m_port->setStopBits(QSerialPort::OneStop);
    m_port->setFlowControl(QSerialPort::NoFlowControl);

    //m_port->setDataTerminalReady(false);
    //m_port->setRequestToSend(false);
}

bool CLSerial::Open()
{
    if (!m_port) return false;
    if (m_port->isOpen()) return true;

    const bool ok = m_port->open(QIODevice::ReadWrite);
    if (ok) {
        emit Opened(m_portName);
    } else {
        emit ErrorOccured(QString("Failed to open %1: %2")
                          .arg(m_portName, m_port->errorString()));
    }
    return ok;
}

void CLSerial::Close()
{
    if (m_port && m_port->isOpen()) {
        m_port->close();
        emit Closed();
    }
}

bool CLSerial::sendAndRecv(const ClCmdPkt& cmd, ClRspPkt& rsp, int timeoutMs)
{
    if (!m_port || !m_port->isOpen())
        return false;

    rsp = ClRspPkt{};

    // 이전에 남아있던 수신 데이터 제거
    m_port->clear(QSerialPort::Input);

    const QByteArray tx = DataPacket::toBytes(cmd);

    qint64 written = m_port->write(tx);
    if (written != tx.size())
        return false;

    if (!m_port->waitForBytesWritten(100))
        return false;

    QByteArray buf;

    QElapsedTimer t;
    t.start();

    while (t.elapsed() < timeoutMs)
    {
        int remain = timeoutMs - static_cast<int>(t.elapsed());
        if (remain <= 0)
            break;

        if (!m_port->waitForReadyRead(remain))
            continue;

        buf += m_port->readAll();

        while (DataPacket::tryParse(buf, rsp))
        {
            // 내가 보낸 명령에 대한 응답인지 확인
            if (rsp.header0 == 0x43 &&
                rsp.header1 == 0x4C &&
                rsp.mainId  == cmd.mainId &&
                rsp.subId   == cmd.subId &&
                rsp.rw      == cmd.rw)
            {
                return true;
            }

            // 다른 응답이면 버리고 계속 대기
            rsp = ClRspPkt{};
        }
    }

    return false;
}

void CLSerial::onReadyRead()
{
    m_rxBuf = m_port->readAll();
    emit DataReceived(m_rxBuf);
}

void CLSerial::onErrorOccurred(QSerialPort::SerialPortError err)
{
    if (err == QSerialPort::NoError) return;

    // 일부 오류는 포트가 자동으로 닫힐 수 있음
    emit ErrorOccured(QString("Serial error (%1): %2")
                      .arg(int(err)).arg(m_port->errorString()));
}


