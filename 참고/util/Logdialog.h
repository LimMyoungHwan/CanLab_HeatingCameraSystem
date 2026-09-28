#ifndef LOGDIALOG_H
#define LOGDIALOG_H

#include <QDialog>

namespace Ui {
class LogDialog;
}

class LogDialog : public QDialog
{
    Q_OBJECT

public:
    explicit LogDialog(QWidget *parent = nullptr);
    ~LogDialog();

public slots:
    void appendLog(const QString& line);  // Viewer에서 받은 로그 추가

private:
    Ui::LogDialog *ui;
};

#endif // LOGDIALOG_H
