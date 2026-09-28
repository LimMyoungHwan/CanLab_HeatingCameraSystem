#include "Aboutdialog.h"
#include "ui_AboutDialog.h"
#include "Version.h"

AboutDialog::AboutDialog(QWidget *parent) :
    QDialog(parent),
    ui(new Ui::AboutDialog)
{
    ui->setupUi(this);

    ui->labelProgram->setStyleSheet("font-size: 18px; font-weight: bold;");
    ui->labelVersion->setText(QString("Version: %1").arg(APP_VERSION));
    ui->labelDescription->setText("Canlab utility for TDA3 devices.\n\nDeveloped by CAN-Lab Inc.");
    ui->labelCopyright->setText("© 2025 CAN-Lab Inc. Licensed under GNU GPLv3.");

    // PNG 로드 (절대 경로)
    QPixmap pix(":/canlab.png");
    if (!pix.isNull()) {
        QPixmap scaled = pix.scaled(280, 100, Qt::KeepAspectRatio, Qt::SmoothTransformation);
        ui->labelImage->setPixmap(scaled);
    }
}

AboutDialog::~AboutDialog()
{
    delete ui;
}
