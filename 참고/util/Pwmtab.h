#ifndef PWMTAB_H
#define PWMTAB_H

#include <QWidget>
#include <QSerialPort>

QT_BEGIN_NAMESPACE
namespace Ui { class PwmTab; }
QT_END_NAMESPACE

class PwmTab : public QWidget
{
    Q_OBJECT

public:
    explicit PwmTab(QWidget *parent = nullptr);
    ~PwmTab();

    void logPrint(const QString &text);

private slots:
    void on_btnUpdatePort_clicked();
    void on_btnOpenPort_clicked();
    void on_btnEO_clicked();
    void on_btnIR_clicked();
    void on_btnDeadzone_clicked();
    void on_btnClearLog_clicked();
    void on_btnExit_clicked();

private:
    Ui::PwmTab *ui;
    QSerialPort *serial = nullptr;

    void refreshPortList();
    bool isPortOpen() const;
    bool sendCmd(const QByteArray &cmd, const QString &tag);
    void setModeButtonsEnabled(bool enabled);
    void closePort(bool sendSafe);
};

#endif // PWMTAB_H
