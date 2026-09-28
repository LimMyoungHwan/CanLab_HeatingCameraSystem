#include "Mflashtab.h"
#include "ui_Mflashtab.h"
#include <QFileDialog>
#include <QDir>
#include <QtConcurrent/QtConcurrent>

// Canlab_flash.c 의 RX 측 전역 플래그들
extern "C" {
    extern volatile int ackReceived;
    extern volatile int complete;
    extern volatile int writeAbortFlag;   // Write 중단 요청 플래그
}

static const char *kUserFactoryFile  = "data_user_config_factory.bin";

// 펌웨어 파일 구조체
struct FirmwareInfo {
    QString label;
    QCheckBox *check;
    QLineEdit *line;
    QString offset;
};

MFlashTab::MFlashTab(QWidget *parent) :
    QWidget(parent),
    ui(new Ui::MFlashTab)
{
    ui->setupUi(this);

    setupSerialPortList();
    setupBrowseButtons();
    setupControlButtons();
    setupLogAndProgress();
    autoScanFirmwareFiles();
}

MFlashTab::~MFlashTab()
{
    if (serial && serial->isOpen())
        serial->close();
    delete serial;
    delete ui;
}

/* ==================== UI 초기화 ==================== */

// 시리얼 포트 목록 채우기
void MFlashTab::setupSerialPortList()
{
    ui->comboPort->clear();

    for (const QSerialPortInfo &port : QSerialPortInfo::availablePorts()) {
#ifdef _WIN32
        QString portName = port.portName();         // 예: "COM3"
        QString desc     = port.description();      // 장치 설명
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
}

// Browse 버튼 연결
void MFlashTab::setupBrowseButtons()
{
    connect(ui->btnBrowseSBL,      &QPushButton::clicked, this, &MFlashTab::onBrowseSBL);
    connect(ui->btnBrowseAppImage, &QPushButton::clicked, this, &MFlashTab::onBrowseAppImage);
    connect(ui->btnBrowseTecless,  &QPushButton::clicked, this, &MFlashTab::onBrowseTecless);
    connect(ui->btnBrowseUser,     &QPushButton::clicked, this, &MFlashTab::onBrowseUser);
}

// Update / Clear 버튼
void MFlashTab::setupControlButtons()
{
    connect(ui->btnUpdate, &QPushButton::clicked, this, [this](){
        setupSerialPortList();
        logPrint("[PC] Port list updated.");
    });
    connect(ui->btnClearLog, &QPushButton::clicked, this, [this](){
        ui->logOutput->clear();
    });
}

// Progress / Log 콜백
void MFlashTab::setupLogAndProgress()
{
    MFlash_SetProgressCallback([](int percent){
        QMetaObject::invokeMethod(qApp, [percent](){
            for (auto *w : qApp->topLevelWidgets()) {
                auto tab = w->findChild<MFlashTab*>();
                if (tab) tab->logProgress(percent);
            }
        }, Qt::QueuedConnection);
    });
}

/* ==================== 펌웨어 자동 스캔 ==================== */

void MFlashTab::autoScanFirmwareFiles()
{
    QString fwBase = QCoreApplication::applicationDirPath() + "/resources/firmware";

    struct { QString subDir; QCheckBox *check; QLineEdit *line; QString fixedFile; } targets[] = {
        { "SBL",           ui->checkSBL,      ui->lineSBL,      QString() },
        { "APP",           ui->checkAppImage, ui->lineAppImage, QString() },
        { "TEC_DATA",      ui->checkTecless,  ui->lineTecless,  QString() },
        { "USER_CFG_DATA", ui->checkUser,     ui->lineUser,     QString(kUserFactoryFile) },
    };

    for (auto &t : targets) {
        QDir dir(fwBase + "/" + t.subDir);
        if (!dir.exists())
            continue;

        QString filePath;
        if (!t.fixedFile.isEmpty()) {
            QString full = dir.absoluteFilePath(t.fixedFile);
            if (!QFile::exists(full))
                continue;
            filePath = full;
        } else {
            QStringList files = dir.entryList(QDir::Files | QDir::NoDotAndDotDot, QDir::Name);
            if (files.isEmpty())
                continue;
            filePath = dir.absoluteFilePath(files.first());
        }

        t.line->setText(filePath);
        t.check->setChecked(true);
        logPrint(QString("[PC] Auto-detected %1: %2").arg(t.subDir, filePath));
    }
}

/* ==================== 로깅 ==================== */

void MFlashTab::logPrint(const QString &text)
{
    if (QThread::currentThread() == this->thread()) {
        ui->logOutput->append(text);

        if (text.contains("TDAxx flashing utility")) {
            ui->groupFiles->setEnabled(true);
            ui->btnErase->setEnabled(true);
            ui->btnWrite->setEnabled(true);
            ui->btnExit->setEnabled(true);
            logPrint("[PC] Firmware Files panel enabled.");
        }
    } else {
        QMetaObject::invokeMethod(ui->logOutput, "append",
                                  Qt::QueuedConnection,
                                  Q_ARG(QString, text));

        // 🔹 비동기 처리에서 UI 활성화
        if (text.contains("TDAxx flashing utility")) {
            QMetaObject::invokeMethod(this, [this]() {
                ui->groupFiles->setEnabled(true);
                ui->btnErase->setEnabled(true);
                ui->btnWrite->setEnabled(true);
                ui->btnExit->setEnabled(true);
                logPrint("[PC] Firmware Files panel enabled.");
            }, Qt::QueuedConnection);
        }
    }
}


void MFlashTab::logProgress(int percent)
{
    ui->progressBar->setValue(percent);
}

/* ==================== 버튼 핸들러 ==================== */

void MFlashTab::on_btnOpenPort_clicked()
{
#ifdef _WIN32
    bool isOpen = (hComm != INVALID_HANDLE_VALUE && hComm != NULL);
#else
    bool isOpen = (hComm >= 0);
#endif

    if (isOpen) {
        logPrint("[PC] Close Port requested.");
        closePort();
        return;
    }

    QString systemPort = ui->comboPort->currentData().toString();
    if (systemPort.isEmpty()) {
        logPrint("No port selected.");
        return;
    }

    // 전체 Open 시퀀스(115200 open + SBL 전송 + 12Mbaud reopen + handshake)를
    // QtConcurrent 로 수행. termios sleep(2)+sleep(1) 으로 인한 UI freeze 방지.
    logPrint(QString("[PC] Opening %1 ... please wait").arg(systemPort));
    ui->groupFiles->setEnabled(false);
    ui->btnErase->setEnabled(false);
    ui->btnWrite->setEnabled(false);
    ui->btnExit->setEnabled(false);
    ui->btnOpenPort->setEnabled(false);

    const QByteArray portBa  = systemPort.toLocal8Bit();
    const QString    sblPath = QCoreApplication::applicationDirPath() + SBL_MFLASH_FILE;

    QtConcurrent::run([this, portBa, sblPath, systemPort]() {
        char com[128];
        strncpy(com, portBa.constData(), sizeof(com) - 1);
        com[sizeof(com) - 1] = '\0';

        // 실패 시 UI 를 "포트 닫힘" 상태로 되돌리고 사용자에게 알림.
        // (rxThread 가 아직 안 떠 있는 단계이므로 closePort 대신 인라인 복원)
        auto reportFailure = [this](const QString &logMsg, const QString &dialogMsg) {
            QMetaObject::invokeMethod(this, [this, logMsg, dialogMsg]() {
                logPrint(logMsg);
                ui->btnOpenPort->setText("Open Port");
                ui->btnOpenPort->setEnabled(true);
                ui->groupFiles->setEnabled(true);
                ui->btnErase->setEnabled(true);
                ui->btnWrite->setEnabled(true);
                ui->btnExit->setEnabled(true);
                QMessageBox::critical(this, tr("Port Open Failed"), dialogMsg);
            }, Qt::QueuedConnection);
        };

        // 1) 115200 open (sleep(2) 포함)
        MFlash_Descriptor h = MFlash_OpenPort(com, EVENPARITY, BAUD_RATE_115200);
#ifdef _WIN32
        bool ok = (h != INVALID_HANDLE_VALUE && h != NULL);
#else
        bool ok = (h >= 0);
#endif
        if (!ok) {
            reportFailure(
                QString("[PC][ERROR] Open failed: %1").arg(systemPort),
                tr("포트를 열 수 없습니다:\n%1\n\n다른 프로그램이 사용 중이거나 권한이 없을 수 있습니다.").arg(systemPort));
            return;
        }
        hComm = h;

        QMetaObject::invokeMethod(this, [this, systemPort]() {
            logPrint(QString("[PC] Port opened: %1 @115200").arg(systemPort));
            ui->btnOpenPort->setText("Close Port");
        }, Qt::QueuedConnection);

        // 2) ASIC + PERI + SBL_MFLASH 전송 (성공/실패 모두 내부에서 stopPort 수행)
        const QByteArray sblPathBa = sblPath.toLocal8Bit();
        int sblRc = MFlash_RequestAsicPeriAndSendSBL(hComm, sblPathBa);
#ifdef _WIN32
        hComm = INVALID_HANDLE_VALUE;
#else
        hComm = -1;
#endif
        if (sblRc < 0) {
            reportFailure(
                QString("[PC][ERROR] No response from board on %1.").arg(systemPort),
                tr("보드에서 응답이 없습니다.\n\n"
                   "확인 사항:\n"
                   "  • 올바른 포트를 선택했는지\n"
                   "  • USB / UART 케이블 연결 상태\n"
                   "  • 보드 전원 및 부팅 모드 (UART boot)"));
            return;
        }

        QMetaObject::invokeMethod(this, [this]() {
            logPrint("[PC] SBL_MFLASH transfer finished. Reopening port...");
        }, Qt::QueuedConnection);

        // 3) 12Mbaud reopen (sleep(1) 포함)
        h = MFlash_startPort2(com, NOPARITY, BAUD_RATE_12000000);
#ifdef _WIN32
        ok = (h != INVALID_HANDLE_VALUE && h != NULL);
#else
        ok = (h >= 0);
#endif
        if (!ok) {
            reportFailure(
                QString("[PC][ERROR] 12Mbaud reopen failed on %1.").arg(systemPort),
                tr("12Mbaud 재오픈에 실패했습니다.\n\n포트 / 케이블 상태를 확인하세요."));
            return;
        }
        hComm = h;

        // 4) Handshake
        if (MFlash_DoSblHandshake(hComm) < 0) {
            MFlash_stopPort(hComm);
#ifdef _WIN32
            hComm = INVALID_HANDLE_VALUE;
#else
            hComm = -1;
#endif
            reportFailure(
                "[PC][ERROR] SBL handshake timeout.",
                tr("SBL 핸드셰이크에 실패했습니다.\n\n"
                   "보드가 mflash SBL 로 부팅되지 않았을 수 있습니다.\n"
                   "보드 전원 / 부팅 모드를 확인 후 재시도하세요."));
            return;
        }
        QMetaObject::invokeMethod(this, [this]() {
            logPrint("[PC] Handshake completed successfully.");
        }, Qt::QueuedConnection);

        // 5) rxThread 시작
        exitFlag = 0;
#ifdef _WIN32
        hThreadRx = CreateThread(NULL, 0, rxThreadWin, (void*)&hComm, 0, NULL);
        if (hThreadRx == NULL) {
            QMetaObject::invokeMethod(this, [this]() {
                logPrint("[PC] CreateThread failed.");
            }, Qt::QueuedConnection);
        }
#else
        if (pthread_create(&m_tidrx, NULL, rxThread, (void*)&hComm) != 0) {
            QMetaObject::invokeMethod(this, [this]() {
                logPrint("[PC] pthread_create failed.");
            }, Qt::QueuedConnection);
        }
#endif

        // 6) Open Port 버튼 다시 누를 수 있도록 (Close 용도)
        QMetaObject::invokeMethod(this, [this]() {
            ui->btnOpenPort->setEnabled(true);
        }, Qt::QueuedConnection);
    });
}

// Erase
void MFlashTab::on_btnErase_clicked()
{
#ifdef _WIN32
    bool isOpen = (hComm != INVALID_HANDLE_VALUE && hComm != NULL);
#else
    bool isOpen = (hComm >= 0);
#endif

    if (!isOpen) {
        QMessageBox::warning(this, "Port Error",
                             "No port connected.\nPlease open a port first.");
        return;
    }

    // Erase 완료 대기 — UI 잠그고 0x06 ACK 또는 90s 타임아웃까지 hold.
    // 사용자가 Erase 진행 중 Write 를 눌러 충돌하는 시나리오 방지.
    ui->groupSerial->setEnabled(false);
    ui->groupFiles->setEnabled(false);
    ui->btnErase->setEnabled(false);
    ui->btnWrite->setEnabled(false);
    ui->btnExit->setEnabled(false);

    QtConcurrent::run([this]() {
        QMetaObject::invokeMethod(this, [this]() {
            logPrint("[PC] ERASE command sent. Erasing flash, please wait...");
        }, Qt::QueuedConnection);

        ackReceived = 0;
        uint8_t cmd = 1U;
        MFlash_puts(hComm, &cmd, 1);

        const int ERASE_TIMEOUT_S = 90;
        time_t start = time(NULL);
        int loggedSec = 0;
        bool ackOk = false;

        while (true) {
#ifdef _WIN32
            Sleep(100);
#else
            usleep(100000);
#endif
            if (ackReceived) {
                ackReceived = 0;
                ackOk = true;
                break;
            }
            time_t elapsed = time(NULL) - start;
            if (elapsed >= ERASE_TIMEOUT_S)
                break;

            if (elapsed >= loggedSec + 5) {
                loggedSec = (int)(elapsed / 5) * 5;
                int captured = loggedSec;
                QMetaObject::invokeMethod(this, [this, captured]() {
                    logPrint(QString("[PC] Erasing... %1s elapsed").arg(captured));
                }, Qt::QueuedConnection);
            }
        }

        QMetaObject::invokeMethod(this, [this, ackOk]() {
            if (ackOk)
                logPrint("[PC] ERASE complete (ACK received).");
            else
                logPrint(QString("[PC][WARN] ERASE wait timed out (%1s). Resuming UI.").arg(90));

            ui->groupSerial->setEnabled(true);
            ui->groupFiles->setEnabled(true);
            ui->btnErase->setEnabled(true);
            ui->btnWrite->setEnabled(true);
            ui->btnExit->setEnabled(true);
        }, Qt::QueuedConnection);
    });
}

// Write Selected (진행 중에는 Stop 으로 동작)
void MFlashTab::on_btnWrite_clicked()
{
    // === Write 진행 중이면 Stop 으로 처리 ===
    if (m_writeInProgress) {
        auto ret = QMessageBox::warning(this, tr("Stop Write"),
            tr("Write를 중단하면 보드 전원 재인가 후 재플래시가 필요합니다.\n진행할까요?"),
            QMessageBox::Yes | QMessageBox::No, QMessageBox::No);
        if (ret != QMessageBox::Yes)
            return;

        logPrint("[PC] Write abort requested by user.");
        writeAbortFlag = 1;          // worker thread 가 즉시 빠져나옴
        closePort();                  // UART 끊기 → MFlash_puts/read 도 에러로 종료
        ui->btnWrite->setText("Stopping...");
        ui->btnWrite->setEnabled(false);
        return;
    }

#ifdef _WIN32
    bool isOpen = (hComm != INVALID_HANDLE_VALUE && hComm != NULL);
#else
    bool isOpen = (hComm >= 0);
#endif

    if (!isOpen) {
        QMessageBox::warning(this, "Port Error",
                             "No port connected.\nPlease open a port first.");
        return;
    }

    // === Write 진행 시작 ===
    writeAbortFlag = 0;
    m_writeInProgress = true;

    // Write 진행 중 UI 비활성화 (단 Write 버튼은 Stop 으로 활성 유지)
    ui->groupSerial->setEnabled(false);
    ui->groupFiles->setEnabled(false);
    ui->btnErase->setEnabled(false);
    ui->btnExit->setEnabled(false);
    ui->btnWrite->setText("Stop");
    ui->btnWrite->setEnabled(true);

    QtConcurrent::run([this]() {
        QMetaObject::invokeMethod(this, [this](){ logPrint("[PC] WRITE command sent."); }, Qt::QueuedConnection);

        struct TargetFile { QCheckBox* chk; QLineEdit* line; const char* offset; };
        TargetFile targets[4] = {
            { ui->checkSBL, ui->lineSBL, SBL_OFFSET },
            { ui->checkAppImage, ui->lineAppImage, APPIMAGE_OFFSET },
            { ui->checkTecless, ui->lineTecless, TEC_OFFSET },
            { ui->checkUser, ui->lineUser, USER_OFFSET }
        };

        for (int i = 0; i < 4; i++) {
            if (writeAbortFlag) break;  // user aborted between files
            if (targets[i].chk->isChecked()) {
                QString filePath = targets[i].line->text();

                // === 파일 경로 검증 ===
                if (filePath.isEmpty()) {
                    QMetaObject::invokeMethod(this, [this, i]() {
                        logPrint(QString("[PC] ERROR: File path is empty (index %1)").arg(i));
                    }, Qt::QueuedConnection);
                    continue;
                }
                QFile file(filePath);
                if (!file.exists()) {
                    QMetaObject::invokeMethod(this, [this, filePath]() {
                        logPrint("[PC][ERROR] File not found: " + filePath);
                    }, Qt::QueuedConnection);
                    continue;
                }

                // === Write 명령 전송 ===
                uint8_t cmd = 2U;
                MFlash_puts(hComm, &cmd, 1);

                uint32_t ofs = strtoul(targets[i].offset, NULL, 0);
                // 한글 등 비ASCII 경로 대응: Windows fopen 은 시스템 ANSI(CP949 등)
                // 를 기대하므로 UTF-8 인 toStdString() 대신 toLocal8Bit() 사용.
                const QByteArray filePathBa = filePath.toLocal8Bit();
                PC_SendFile(hComm, filePathBa.constData(), ofs);


        #ifndef _WIN32
                usleep(100000); // 100ms delay
        #else
                Sleep(100);
        #endif
            }
        }

        // Write 완료 (또는 abort) 후 UI 복원
        bool aborted = (writeAbortFlag != 0);
        QMetaObject::invokeMethod(this, [this, aborted]() {
            m_writeInProgress = false;
            writeAbortFlag = 0;

            ui->groupSerial->setEnabled(true);
            ui->groupFiles->setEnabled(true);
            ui->btnErase->setEnabled(true);
            ui->btnWrite->setText("Write");
            ui->btnWrite->setEnabled(true);
            ui->btnExit->setEnabled(true);

            if (aborted) {
                logPrint("[PC] WRITE aborted. Power-cycle the board before retry.");
                closePort();
                QMessageBox::warning(this, tr("Write Aborted"),
                    tr("Write가 중단되었습니다.\n보드 전원을 재인가한 후 다시 시도하세요."));
                return;
            }

            logPrint("[PC] WRITE completed.");
            // Write 완료 → UART 포트 자동 close
            closePort();

            QMessageBox::information(this, tr("Success"),
                                     tr("WRITE completed successfully."));
        }, Qt::QueuedConnection);
    });
}

// Exit
void MFlashTab::on_btnExit_clicked()
{
    logPrint("Exit clicked.");

    // 종료 절차를 closePort 로 단일화 — 0x03 EXIT 명령만 사전 송신.
#ifdef _WIN32
    bool isOpen = (hComm != INVALID_HANDLE_VALUE && hComm != NULL);
#else
    bool isOpen = (hComm >= 0);
#endif

    if (isOpen) {
        uint8_t cmd = 3U;   // EXIT
        MFlash_puts(hComm, &cmd, 1);
        logPrint("[PC] EXIT command sent.");
    }

    closePort();   // exitFlag, rxThread join, stopPort, UI 정리 일괄 처리
    qApp->quit();
}

/* ==================== Browse 슬롯 ==================== */

void MFlashTab::browseFirmwareFile(QLineEdit *lineEdit, QCheckBox *checkBox, const QString &label)
{
    QString file = QFileDialog::getOpenFileName(this, "Select " + label + " File");
    if (!file.isEmpty()) {
        lineEdit->setText(file);
        if (checkBox)
            checkBox->setChecked(true);
        logPrint(label + " file selected: " + file);
    }
}

void MFlashTab::onBrowseSBL()      { browseFirmwareFile(ui->lineSBL,      ui->checkSBL,      "SBL"); }
void MFlashTab::onBrowseAppImage() { browseFirmwareFile(ui->lineAppImage, ui->checkAppImage, "AppImage"); }
void MFlashTab::onBrowseTecless()  { browseFirmwareFile(ui->lineTecless,  ui->checkTecless,  "TECLESS"); }
void MFlashTab::onBrowseUser()     { browseFirmwareFile(ui->lineUser,     ui->checkUser,     "USER"); }

/* ==================== 포트 Close 공용 ==================== */

void MFlashTab::closePort()
{
#ifdef _WIN32
    bool isOpen = (hComm != INVALID_HANDLE_VALUE && hComm != NULL);
#else
    bool isOpen = (hComm >= 0);
#endif
    if (!isOpen)
        return;

    exitFlag = 1;
#ifdef _WIN32
    if (hThreadRx) {
        WaitForSingleObject(hThreadRx, INFINITE);
        CloseHandle(hThreadRx);
        hThreadRx = NULL;
    }
#else
    if (m_tidrx) {
        pthread_join(m_tidrx, NULL);
        m_tidrx = 0;
    }
#endif
    exitFlag = 0;

    // 다음 세션이 stale 상태 위에서 동작하지 않도록 RX 측 전역 플래그 초기화.
    // (rxThread 가 ack/complete 를 set 했을 수 있고, 그게 다음 PC_SendFile 의
    //  첫 ACK 대기를 통과시켜 SBL 과 타이밍 어긋남이 발생함)
    ackReceived    = 0;
    complete       = 0;
    writeAbortFlag = 0;

    MFlash_stopPort(hComm);
#ifdef _WIN32
    hComm = INVALID_HANDLE_VALUE;
#else
    hComm = -1;
#endif

    logPrint("Port closed.");

    ui->groupFiles->setEnabled(true);
    ui->btnErase->setEnabled(true);
    ui->btnWrite->setEnabled(true);
    ui->btnExit->setEnabled(true);
    ui->btnOpenPort->setEnabled(true);
    ui->btnOpenPort->setText("Open Port");
}

