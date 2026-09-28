#include "Pwmtab.h"
#include "ui_Pwmtab.h"

#include <QSerialPortInfo>
#include <QThread>
#include <QDateTime>
#include <QMessageBox>

namespace {
// PWM 디바이스 (SY-LD213) 시리얼 명령 — 9600 8N1 ASCII
constexpr int  PWM_BAUD          = 9600;
constexpr int  PWM_CMD_DELAY_MS  = 100;

constexpr const char *CMD_INIT_FREQ   = "F050";   // 50Hz
constexpr const char *CMD_INIT_DUTY   = "D000";   // duty 0%
constexpr const char *CMD_MODE_EO     = "D007";
constexpr const char *CMD_MODE_IR     = "D009";
constexpr const char *CMD_MODE_DEAD   = "D008";

// 모드 버튼의 활성/비활성 시각 강조 (작업자가 현재 모드 인지 가능)
// Main.cpp 의 글로벌 stylesheet 에서 font-size: 10pt 가 강제되므로
// 여기서 font-size 를 명시해 override 한다.
const char *MODE_BUTTON_QSS =
    "QPushButton {"
    "  border: 1px solid #888;"
    "  border-radius: 4px;"
    "  background: #ffffff;"
    "  font-size: 18pt;"
    "  font-weight: bold;"
    "}"
    "QPushButton:checked {"
    "  background: #1976D2;"
    "  color: white;"
    "  border: 2px solid #0D47A1;"
    "}"
    "QPushButton:disabled {"
    "  color: #888;"
    "}";
}

PwmTab::PwmTab(QWidget *parent)
    : QWidget(parent)
    , ui(new Ui::PwmTab)
{
    ui->setupUi(this);

    serial = new QSerialPort(this);

    // 모드 버튼 시각 스타일 (active = 파란색 강조)
    ui->btnEO->setStyleSheet(MODE_BUTTON_QSS);
    ui->btnIR->setStyleSheet(MODE_BUTTON_QSS);
    ui->btnDeadzone->setStyleSheet(MODE_BUTTON_QSS);

    refreshPortList();
    setModeButtonsEnabled(false);

    // 슬롯은 on_<objectName>_clicked 네이밍으로 connectSlotsByName 자동 연결됨.
    // 수동 connect 추가 금지 (중복 호출 → open 직후 D008 safe-exit 가 발생).
}

PwmTab::~PwmTab()
{
    if (isPortOpen())
        closePort(true);
    delete ui;
}

// ---------- helpers ----------

void PwmTab::logPrint(const QString &text)
{
    QString ts = QDateTime::currentDateTime().toString("HH:mm:ss.zzz");
    ui->textLog->append(QString("[%1] %2").arg(ts, text));
}

bool PwmTab::isPortOpen() const
{
    return serial && serial->isOpen();
}

void PwmTab::refreshPortList()
{
    ui->comboPort->clear();

    for (const QSerialPortInfo &port : QSerialPortInfo::availablePorts()) {
#ifdef _WIN32
        QString portName = port.portName();         // "COM3"
        QString desc     = port.description();
        ui->comboPort->addItem(
            desc.isEmpty() ? portName : portName + " (" + desc + ")",
            port.systemLocation()                   // \\.\COM3
        );
#else
        if (!port.portName().startsWith("ttyUSB"))
            continue;

        QString shortName = port.portName().mid(3); // "ttyUSB0" → "0"
        QString desc      = port.description();
        ui->comboPort->addItem(
            desc.isEmpty() ? shortName : shortName + " (" + desc + ")",
            port.systemLocation()                   // /dev/ttyUSB0
        );
#endif
    }

    if (ui->comboPort->count() == 0)
        logPrint("[WARN] No serial ports found.");
}

void PwmTab::setModeButtonsEnabled(bool enabled)
{
    ui->btnEO->setEnabled(enabled);
    ui->btnIR->setEnabled(enabled);
    ui->btnDeadzone->setEnabled(enabled);

    if (!enabled) {
        // 포트 닫힘 → 활성 표시 초기화
        // autoExclusive 가 켜져 있어 setChecked(false) 직접 호출 필요.
        ui->btnEO->setAutoExclusive(false);
        ui->btnIR->setAutoExclusive(false);
        ui->btnDeadzone->setAutoExclusive(false);
        ui->btnEO->setChecked(false);
        ui->btnIR->setChecked(false);
        ui->btnDeadzone->setChecked(false);
        ui->btnEO->setAutoExclusive(true);
        ui->btnIR->setAutoExclusive(true);
        ui->btnDeadzone->setAutoExclusive(true);
    }
}

bool PwmTab::sendCmd(const QByteArray &cmd, const QString &tag)
{
    if (!isPortOpen()) {
        logPrint("[ERR] Port not open.");
        return false;
    }

    qint64 n = serial->write(cmd);
    if (n != cmd.size() || !serial->waitForBytesWritten(500)) {
        logPrint(QString("[ERR] %1 send fail (%2).").arg(tag, serial->errorString()));
        return false;
    }

    logPrint(QString("[TX] %1  (%2)").arg(QString::fromLatin1(cmd), tag));
    return true;
}

void PwmTab::closePort(bool sendSafe)
{
    if (!isPortOpen()) {
        setModeButtonsEnabled(false);
        ui->btnOpenPort->setText("Open Port");
        return;
    }

    if (sendSafe) {
        // 양산 안전: 닫기 전 DEADZONE(D008) 강제 송신
        sendCmd(CMD_MODE_DEAD, "DEADZONE (safe exit)");
        QThread::msleep(PWM_CMD_DELAY_MS);
    }

    serial->close();
    setModeButtonsEnabled(false);
    ui->btnOpenPort->setText("Open Port");
    ui->comboPort->setEnabled(true);
    ui->btnUpdatePort->setEnabled(true);
    logPrint("[INFO] Port closed.");
}

// ---------- slots ----------

void PwmTab::on_btnUpdatePort_clicked()
{
    refreshPortList();
}

void PwmTab::on_btnOpenPort_clicked()
{
    // 이미 열려 있으면 Close 동작
    if (isPortOpen()) {
        closePort(true);
        return;
    }

    const QString displayName = ui->comboPort->currentText();
    const QString systemPort  = ui->comboPort->currentData().toString();
    if (systemPort.isEmpty()) {
        QMessageBox::warning(this, tr("PWM"), tr("Select a serial port."));
        return;
    }

    serial->setPortName(systemPort);
    serial->setBaudRate(PWM_BAUD);
    serial->setDataBits(QSerialPort::Data8);
    serial->setParity(QSerialPort::NoParity);
    serial->setStopBits(QSerialPort::OneStop);
    serial->setFlowControl(QSerialPort::NoFlowControl);

    if (!serial->open(QIODevice::ReadWrite)) {
        logPrint(QString("[ERR] Open fail: %1 (%2)").arg(displayName, serial->errorString()));
        QMessageBox::critical(this, tr("PWM"),
            tr("Failed to open %1:\n%2").arg(displayName, serial->errorString()));
        return;
    }

    logPrint(QString("[INFO] Port opened: %1 @ %2 8N1").arg(displayName).arg(PWM_BAUD));

    // 초기화 시퀀스: F050 → 100ms → D000 → 100ms (PWM.sh 와 동일)
    sendCmd(CMD_INIT_FREQ, "init 50Hz");
    QThread::msleep(PWM_CMD_DELAY_MS);
    sendCmd(CMD_INIT_DUTY, "init duty 0");
    QThread::msleep(PWM_CMD_DELAY_MS);

    setModeButtonsEnabled(true);
    ui->btnOpenPort->setText("Close Port");
    ui->comboPort->setEnabled(false);
    ui->btnUpdatePort->setEnabled(false);
}

void PwmTab::on_btnEO_clicked()
{
    logPrint("[MODE] EO");
    if (!sendCmd(CMD_MODE_EO, "EO"))
        ui->btnEO->setChecked(false);   // 송신 실패 시 활성 표시 해제
}

void PwmTab::on_btnIR_clicked()
{
    logPrint("[MODE] IR");
    if (!sendCmd(CMD_MODE_IR, "IR"))
        ui->btnIR->setChecked(false);
}

void PwmTab::on_btnDeadzone_clicked()
{
    logPrint("[MODE] DEADZONE");
    if (!sendCmd(CMD_MODE_DEAD, "DEADZONE"))
        ui->btnDeadzone->setChecked(false);
}

void PwmTab::on_btnClearLog_clicked()
{
    ui->textLog->clear();
}

void PwmTab::on_btnExit_clicked()
{
    logPrint("Exit clicked.");
    closePort(true);

    // 최상위 창 닫기 → 앱 종료
    if (QWidget *top = window())
        top->close();
}
