#ifndef ABOUTDIALOG_H
#define ABOUTDIALOG_H

#pragma once
#include <QDialog>
#include <QtSvg/QSvgWidget>
#include <QPixmap>
#include <QFontDatabase>

namespace Ui {
class AboutDialog;
}

class AboutDialog : public QDialog
{
    Q_OBJECT
public:
    explicit AboutDialog(QWidget *parent = nullptr);
    ~AboutDialog();

private:
    Ui::AboutDialog *ui;
};

#endif // ABOUTDIOG_H
