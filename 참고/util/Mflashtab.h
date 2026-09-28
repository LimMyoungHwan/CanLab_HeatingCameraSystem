#ifndef MFLASHTAB_H
#define MFLASHTAB_H

#include <QWidget>
#include <QSerialPort>
#include <QSerialPortInfo>
#include <QCheckBox>
#include <QLineEdit>
#include <QTextEdit>
#include <QThread>
#include <QMessageBox>
#include "Version.h"

extern "C" {
#include "Canlab_flash.h"   // MFlash_xxx C API
}

QT_BEGIN_NAMESPACE
namespace Ui { class MFlashTab; }
QT_END_NAMESPACE

class MFlashTab : public QWidget
{
    Q_OBJECT

public:
    explicit MFlashTab(QWidget *parent = nullptr);
    ~MFlashTab();

    void logPrint(const QString &text);
    void logProgress(int percent);

private slots:
    // UI 버튼 핸들러
    void on_btnOpenPort_clicked();
    void on_btnErase_clicked();
    void on_btnWrite_clicked();
    void on_btnExit_clicked();

    // Browse 버튼
    void onBrowseSBL();
    void onBrowseAppImage();
    void onBrowseTecless();
    void onBrowseUser();

private:
    Ui::MFlashTab *ui;
    QSerialPort *serial = nullptr;
#ifdef _WIN32
    HANDLE hComm = INVALID_HANDLE_VALUE;   // Windows에서는 HANDLE
#else
    int hComm = -1;                        // Linux/Unix에서는 fd(int)
#endif
    pthread_t m_tidrx = 0;  // RX thread (extern 전역 tidrx 와 충돌 방지)

    // Write 진행 중 여부 (true 면 Write 버튼이 Stop 으로 동작)
    bool m_writeInProgress = false;

    // UI 초기화
    void setupSerialPortList();
    void setupBrowseButtons();
    void setupControlButtons();
    void setupLogAndProgress();
    void autoScanFirmwareFiles();

    // 파일 선택 (선택 성공 시 연결된 체크박스 자동 ON)
    void browseFirmwareFile(QLineEdit *lineEdit, QCheckBox *checkBox, const QString &label);

    // UART 포트 close (Write 완료 후 / 수동 Close 공용)
    void closePort();
};

#endif // MFLASHTAB_H
