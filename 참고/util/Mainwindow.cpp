#include "Mainwindow.h"
#include "ui_Mainwindow.h"

#include "Aboutdialog.h"
#include "Mflashtab.h"
#ifdef ENABLE_PWM_TAB
#include "Pwmtab.h"
#endif
#include "Viewer.h"

#include <QAction>
#include <QMenu>
#include <QMenuBar>
#include <QPushButton>
#include <QScreen>
#include <QShowEvent>
#include <QStyle>
#include <QWindow>

namespace {
const char *kPageTitles[] = {
    "Thermal Camera",
    "Firmware Update",
    "PWM Control"
};

const char *kPageSubtitles[] = {
    "Connect the device and tune camera, detector, and image-processing settings.",
    "Update device firmware and review the programming log.",
    "Adjust and verify PWM output for EO/IR hardware tests."
};
}

MainWindow::MainWindow(QWidget *parent)
    : QMainWindow(parent)
    , ui(new Ui::MainWindow)
{
    ui->setupUi(this);

    auto *viewer = new Viewer(ui->pageCamera);
    ui->pageCameraLayout->addWidget(viewer);
    connect(viewer, &Viewer::connectionStateChanged,
            this, &MainWindow::updateConnectionState);

    ui->pageFirmwareLayout->addWidget(new MFlashTab(ui->pageFirmware));

#ifdef ENABLE_PWM_TAB
    ui->pagePwmLayout->addWidget(new PwmTab(ui->pagePwm));
#else
    ui->navPwmButton->hide();
    ui->mainPages->removeWidget(ui->pagePwm);
#endif

    m_navigationButtons = {
        ui->navCameraButton,
        ui->navFirmwareButton,
        ui->navPwmButton
    };

    connect(ui->navCameraButton, &QPushButton::clicked, this, [this]() { selectPage(0); });
    connect(ui->navFirmwareButton, &QPushButton::clicked, this, [this]() { selectPage(1); });
#ifdef ENABLE_PWM_TAB
    connect(ui->navPwmButton, &QPushButton::clicked, this, [this]() { selectPage(2); });
#endif

    connect(ui->helpButton, &QPushButton::clicked, this, [this]() {
        AboutDialog dialog(this);
        dialog.exec();
    });

    auto *helpMenu = menuBar()->addMenu(tr("Help"));
    auto *aboutAction = helpMenu->addAction(tr("About CANLAB Thermal Studio"));
    connect(aboutAction, &QAction::triggered, ui->helpButton, &QPushButton::click);

    selectPage(0);
}

MainWindow::~MainWindow()
{
    delete ui;
}

void MainWindow::showEvent(QShowEvent *event)
{
    QMainWindow::showEvent(event);

    if (m_initialGeometryApplied)
        return;
    m_initialGeometryApplied = true;

    QScreen *screen = windowHandle() ? windowHandle()->screen() : nullptr;
    if (!screen)
        return;

    const QRect available = screen->availableGeometry();
    const QSize uiSize = size();
    const int targetWidth = qMin(uiSize.width(), available.width());
    const int targetHeight = qMin(uiSize.height(), available.height());

    // Keep the size defined in Mainwindow.ui. Only relax the minimum size
    // when the current monitor is smaller than the designed window.
    setMinimumSize(qMin(minimumWidth(), targetWidth),
                   qMin(minimumHeight(), targetHeight));

    resize(targetWidth, targetHeight);
    move(available.center() - rect().center());
}

void MainWindow::selectPage(int index)
{
    if (index < 0 || index >= ui->mainPages->count())
        return;

    ui->mainPages->setCurrentIndex(index);
    ui->pageTitle->setText(kPageTitles[index]);
    ui->pageSubtitle->setText(kPageSubtitles[index]);
    for (int i = 0; i < m_navigationButtons.size(); ++i)
        m_navigationButtons[i]->setChecked(i == index);
}

void MainWindow::updateConnectionState(bool connected, const QString &portName)
{
    ui->connectionBadge->setText(connected
        ? QString("ONLINE · %1").arg(portName)
        : QString("OFFLINE"));
    ui->connectionBadge->setProperty("connected", connected);
    ui->connectionBadge->style()->unpolish(ui->connectionBadge);
    ui->connectionBadge->style()->polish(ui->connectionBadge);
}
