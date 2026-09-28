#include "Viewer.h"
#include "ui_Viewer.h"
#include "Capture.h"

// ==============================
// Impl 구조체
// ==============================
class Viewer::Impl {
public:
    Capture camera;
};

// ==============================
// Constructor & Destructor
// ==============================
Viewer::Viewer(QWidget *parent)
    : QWidget(parent),
      ui(new Ui::Viewer),
      m_cam(new Impl),
      m_videoPopup(nullptr),
      m_popupLabel(nullptr),
      m_showFps(false),
      m_frameCount(0)
{
    ui->setupUi(this);
    ui->leftLayout->setAlignment(Qt::AlignLeft | Qt::AlignTop);
    ui->mainLayout->setStretch(0, 0);
    ui->mainLayout->setStretch(1, 1);

    ui->rightTabWidget->setCurrentIndex(0); // 첫 번째 탭 지정
    //ui->rightTabWidget->setEnabled(false);
    ui->btn_tecless_temp_range->setEnabled(false);

    setupVideoLabel();
    setupConnections();
    setupSerialPortList();
    setupCamPortList();

    m_serial = new CLSerial(this);

    m_capThread = new capturethread(&m_cam->camera, this);
    connect(m_capThread, &capturethread::frameReady, this, &Viewer::onFrameReady);

    connect(ui->horizontalSlider_CINT, &QSlider::valueChanged, this, &Viewer::on_horizontalSlider_CINT_valueChanged);
    connect(ui->lineEdit_CINT, &QLineEdit::textChanged, this, &Viewer::on_lineEdit_CINT_textChanged);

    connect(ui->horizontalSlider_TINT_MSB, &QSlider::valueChanged, this, &Viewer::on_horizontalSlider_TINT_MSB_valueChanged);
    connect(ui->lineEdit_TINT_MSB, &QLineEdit::textChanged, this, &Viewer::on_lineEdit_TINT_MSB_textChanged);

    connect(ui->horizontalSlider_TINT_LSB, &QSlider::valueChanged, this, &Viewer::on_horizontalSlider_TINT_LSB_valueChanged);
    connect(ui->lineEdit_TINT_LSB, &QLineEdit::textChanged, this, &Viewer::on_lineEdit_TINT_LSB_textChanged);

    connect(ui->horizontalSlider_GSK_MSB, &QSlider::valueChanged, this, &Viewer::on_horizontalSlider_GSK_MSB_valueChanged);
    connect(ui->lineEdit_GSK_MSB, &QLineEdit::textChanged, this, &Viewer::on_lineEdit_GSK_MSB_textChanged);

    connect(ui->horizontalSlider_GSK_LSB, &QSlider::valueChanged, this, &Viewer::on_horizontalSlider_GSK_LSB_valueChanged);
    connect(ui->lineEdit_GSK_LSB, &QLineEdit::textChanged, this, &Viewer::on_lineEdit_GSK_LSB_textChanged);

    connect(ui->horizontalSlider_GFID, &QSlider::valueChanged, this, &Viewer::on_horizontalSlider_GFID_valueChanged);
    connect(ui->lineEdit_GFID, &QLineEdit::textChanged, this, &Viewer::on_lineEdit_GFID_textChanged);

}

Viewer::~Viewer() {
    if (m_capThread->isRunning()) {
        m_capThread->stopCapture();
        m_capThread->wait();
    }
    delete m_cam;
    delete ui;
}

// ==============================
// Setup Helpers
// ==============================
void Viewer::setupVideoLabel() {
    ClickableLabel *clickable = new ClickableLabel(this);
    clickable->setObjectName("videoLabel");
    clickable->setFixedSize(ui->videoLabel->minimumSize());
    clickable->setStyleSheet(ui->videoLabel->styleSheet());
    clickable->setText(ui->videoLabel->text());
    clickable->setAlignment(Qt::AlignCenter);
    clickable->setCursor(Qt::PointingHandCursor);
    clickable->setSizePolicy(QSizePolicy::Fixed, QSizePolicy::Fixed);

    QLayoutItem *oldItem = ui->mainLayout->replaceWidget(ui->videoLabel, clickable,
                                                         Qt::FindChildrenRecursively);
    delete oldItem;
    delete ui->videoLabel;
    ui->videoLabel = clickable;

    connect(clickable, &ClickableLabel::clicked, this, &Viewer::onVideoLabelClicked);
}

void Viewer::setupConnections() {
    connect(ui->btnPortUpdate, &QPushButton::clicked, this, &Viewer::setupSerialPortList);
    connect(ui->btnCamUpdate, &QPushButton::clicked, this, &Viewer::setupCamPortList);

    auto comboIndexChanged = QOverload<int>::of(&QComboBox::currentIndexChanged);
    connect(ui->comboBox_autostart, comboIndexChanged, this, &Viewer::onCameraModeComboChanged);
    connect(ui->comboBox_displaymode, comboIndexChanged, this, &Viewer::onCameraModeComboChanged);
    connect(ui->comboBox_outformat, comboIndexChanged, this, &Viewer::onCameraModeComboChanged);
    connect(ui->comboBox_captureType, comboIndexChanged, this, &Viewer::onCameraModeComboChanged);
    connect(ui->comboBox_OPmode, comboIndexChanged, this, &Viewer::onCameraModeComboChanged);

    connect(ui->comboBox_shutterMode, comboIndexChanged, this, &Viewer::onShutterModeComboChanged);
    connect(ui->lineEdit_CycleTime, &QLineEdit::editingFinished, this, &Viewer::onShutterCycleTimeEditingFinished);

    connect(ui->comboBox_NucMode, comboIndexChanged, this, &Viewer::onNucModeComboChanged);

    connect(ui->comboBox_CEM_Edge, comboIndexChanged, this, &Viewer::onCemComboChanged);
    connect(ui->comboBox_CEM_TPHE, comboIndexChanged, this, &Viewer::onCemComboChanged);
    connect(ui->comboBox_CEM_BPC, comboIndexChanged, this, &Viewer::onCemComboChanged);
    connect(ui->comboBox_CEM_CLAHE, comboIndexChanged, this, &Viewer::onCemComboChanged);
    connect(ui->comboBox_CEM_BoxFilter, comboIndexChanged, this, &Viewer::onCemComboChanged);
    connect(ui->comboBox_CEM_PreNr, comboIndexChanged, this, &Viewer::onCemComboChanged);
    connect(ui->comboBox_CEM_PostNr, comboIndexChanged, this, &Viewer::onCemComboChanged);

    connect(ui->comboBox_NrMode, comboIndexChanged, this, &Viewer::onNrCtrlComboChanged);
    connect(ui->comboBox_NR_Threshold, comboIndexChanged, this, &Viewer::onNrCtrlComboChanged);
    connect(ui->comboBox_postNrScale, comboIndexChanged, this, &Viewer::onPostNrScaleComboChanged);

    connect(ui->comboBox_ColorMap, comboIndexChanged, this, &Viewer::onColorMapComboChanged);
    connect(ui->comboBox_DisplayShutterImage, comboIndexChanged, this, &Viewer::onDisplayShutterImageComboChanged);
}

// ==============================
// Serial (PORT)
// ==============================
void Viewer::setupSerialPortList()
{
    ui->portCombo->clear();
    for (const QSerialPortInfo &port : QSerialPortInfo::availablePorts()) {
#ifdef _WIN32
        //Windows: COM 포트명만 표시
        QString portName = port.portName();
        ui->portCombo->addItem(portName, port.systemLocation());
#else
        //Linux: ACM 포트만 필터링
        if (!port.portName().startsWith("ttyACM"))
            continue;

        // ttyACM0 → "ACM0" 만 표시
        QString shortName = port.portName();
        ui->portCombo->addItem(shortName, port.systemLocation());
#endif
    }
}

void Viewer::on_btnPortConnect_clicked()
{
    if(ui->btnPortConnect->text() == "Connect")
    {
        QString SelectPortName = ui->portCombo->currentText();

        if(SelectPortName == "")
        {
            QMessageBox::warning(this, "경고", "선택된 포트가 없습니다!");
            return;
        }
        m_serial->Initialize(SelectPortName, ui->baudCombo->currentText().toUInt());
        m_serial->Open();
        ui->rightTabWidget->setEnabled(true);
        for (int i = 0; i < ui->rightTabWidget->count(); ++i) {
            ui->rightTabWidget->setTabEnabled(i, true);
        }
        ui->btnPortConnect->setText("Disconnect");
        ui->btn_tecless_temp_range->setEnabled(true);

        emit connectionStateChanged(true, SelectPortName);


        FirstConnectGetInfo();

    }
    else
    {
        ErrorDisConnect();
    }
}





// ==============================
// Camera (CAM)
// ==============================
void Viewer::setupCamPortList() {
    ui->camCombo->clear();

#ifdef _WIN32
    // Windows: 카메라 장치 전체 나열
    for (const QCameraInfo &cameraInfo : QCameraInfo::availableCameras()) {
        ui->camCombo->addItem(cameraInfo.description(), cameraInfo.deviceName());
    }
#else
    // Linux: /dev/video* 장치만 나열
    QDir devDir("/dev");
    QStringList filters;
    filters << "video*";
    QFileInfoList videoDevices = devDir.entryInfoList(filters, QDir::System | QDir::Readable);

    for (const QFileInfo &info : videoDevices) {
        QString devPath = info.absoluteFilePath();   // 예: "/dev/video2"
        QString cardName = getCameraCardName(devPath); // 예: "CLTC_T_VGA_G2_S_b100: CLTC_T_VG"

        if (cardName.isEmpty()) continue;

        // 먼저 ':' 뒤쪽 문자열만 추출
        QString shortName = cardName;
        if (cardName.contains(":")) {
            shortName = cardName.section(":", -1).trimmed();  // 예: "CLTC_T_VG"
        }

        int clIndex = shortName.indexOf("CL");
        if (clIndex < 0) {
            continue;
        }

        shortName = shortName.mid(clIndex); // 예: "CLTC_T_VG"

        QString label = QString("%1 (%2)").arg(shortName).arg(devPath);
        ui->camCombo->addItem(label, devPath);
    }
#endif
}

//TODO : 카메라 START Status 체크 후 Connect 버튼 활성화 되게 수정 필요
void Viewer::on_btnCamConnect_clicked()
{
    if (m_capThread->isRunning())
    {
        m_capThread->stopCapture();
        m_cam->camera.closeDevice();
        ui->videoLabel->clear();
        ui->outModeEntry->setText("");
        ui->widthEntry->setText("");
        ui->heightEntry->setText("");
        ui->fpsEntry->setText("");
        ui->btnCamConnect->setText("Connect");
        return;
    }


    // === 장치 리스트 비어 있을 때 경고창 띄우기 ===
    if (ui->camCombo->count() == 0) {
        QMessageBox::warning(this,
                             tr("Camera"),
                             tr("카메라 장치가 없습니다.\n먼저 장치를 연결하거나 새로고침하세요."));
        return;
    }

#ifdef _WIN32
    int camIndex = ui->camCombo->currentIndex();

    if (!m_cam->camera.openDevice(camIndex, 640, 480)) {
        QMessageBox::warning(this, tr("Camera"), tr("카메라 연결에 실패했습니다. \n장치를 재 연결하거나 새로고침하세요."));
        return;
    }
#else
    QString targetName = "CLTC_T_VG";
    int fourcc = cv::VideoWriter::fourcc('Y','1','6',' ');
    if (!m_cam->camera.openDefaultCamera(targetName, 640, 480, fourcc)) {
        QMessageBox::warning(this, tr("Camera"), tr("카메라 연결에 실패했습니다. \n장치를 재 연결하거나 새로고침하세요."));
        return;
    }
#endif
    if (m_cam->camera.queryFormat()) {
        int fmt = m_cam->camera.getPixelFormat();
        QString fmtStr;
        if (fmt == cv::VideoWriter::fourcc('Y','1','6',' '))
            fmtStr = "Y16";
        else if (fmt == cv::VideoWriter::fourcc('U','Y','V','Y'))
            fmtStr = "UYVY";
        else
            fmtStr = QString("0x%1").arg(fmt, 8, 16, QChar('0'));

        ui->outModeEntry->setText(fmtStr);
        ui->widthEntry->setText(QString::number(m_cam->camera.getWidth()));
        ui->heightEntry->setText(QString::number(m_cam->camera.getHeight()));
    }

    m_frameCount = 0;
    m_fpsTimer.restart();
    m_showFps = true;

    m_capThread->startCapture();
    ui->btnCamConnect->setText("Disconnect");
}


void Viewer::onFrameReady(const QImage& frame) {
    QImage displayImg;
    if (frame.format() == QImage::Format_Grayscale16) {
        displayImg = normalize16To8(frame, true);

        // === 히스토그램 팝업 갱신 ===
        if (m_histPopupWidget) {
            m_histPopupWidget->updateData(frame);
        }
    } else {
        displayImg = frame;
    }

    // === 메인 출력 ===
    if (!m_videoPopupOnlyUpdate) {  //  false일 때만 videoLabel 업데이트
        ui->videoLabel->setPixmap(QPixmap::fromImage(displayImg).scaled(
            ui->videoLabel->size(),
            Qt::KeepAspectRatio,
            Qt::SmoothTransformation));
    }

    // === 팝업 출력 ===
    if (m_popupLabel) {
        m_popupLabel->setPixmap(QPixmap::fromImage(displayImg).scaled(
            m_popupLabel->size(),
            Qt::KeepAspectRatio,
            Qt::SmoothTransformation));
    }

    // === FPS 업데이트 ===
    if (m_showFps) updateFps();
}


// ==============================
// Frame Processing
// ==============================
QImage normalize16To8(const QImage &img16, bool autoStretch) {
    if (img16.format() != QImage::Format_Grayscale16) return QImage();
    int width = img16.width(), height = img16.height();
    int pixels = width * height;

    const uint16_t *src = reinterpret_cast<const uint16_t*>(img16.constBits());
    uint16_t minVal = autoStretch ? 65535 : 0;
    uint16_t maxVal = autoStretch ? 0 : 16383;
    if (autoStretch) {
        for (int i = 0; i < pixels; i++) {
            uint16_t v = src[i];
            if (v < minVal) minVal = v;
            if (v > maxVal) maxVal = v;
        }
        if (maxVal == minVal) maxVal++;
    }
    QImage img8(width, height, QImage::Format_Grayscale8);
    uchar *dst = img8.bits();
    for (int i = 0; i < pixels; i++) {
        float norm = float(src[i] - minVal) / float(maxVal - minVal);
        dst[i] = static_cast<uchar>(norm * 255.0f);
    }
    return img8;
}

void Viewer::updateFps() {
    m_frameCount++;
    if (m_fpsTimer.elapsed() >= 1000) {
        double elapsedSec = m_fpsTimer.elapsed() / 1000.0;
        double fps = m_frameCount / elapsedSec;
        ui->fpsEntry->setText(QString::number(fps, 'f', 1));
        m_frameCount = 0;
        m_fpsTimer.restart();
    }
}


// ==============================
// Event
// ==============================
void Viewer::onVideoLabelClicked() {
    if (m_videoPopup) {
        // 이미 열려 있으면 닫기 (토글)
        m_videoPopup->close();
        m_videoPopup = nullptr;
        m_popupLabel = nullptr;
        m_videoPopupOnlyUpdate = false;
        return;
    }

    // 처음 열 때만 생성
    m_videoPopup = new QDialog(this);
    m_videoPopup->setWindowTitle("Camera Video");
    m_videoPopup->setAttribute(Qt::WA_DeleteOnClose);

    m_popupLabel = new QLabel(m_videoPopup);
    m_popupLabel->setScaledContents(true);

    QVBoxLayout *layout = new QVBoxLayout(m_videoPopup);
    layout->addWidget(m_popupLabel);

    connect(m_videoPopup, &QDialog::destroyed, this, [this]() {
        m_videoPopup = nullptr;
        m_popupLabel = nullptr;
        m_videoPopupOnlyUpdate = false;
    });

    m_videoPopup->resize(800, 600);
    m_videoPopup->show();
    m_videoPopup->raise();
    m_videoPopup->activateWindow();

    m_videoPopupOnlyUpdate = true;
}


// ==============================
// Uart
// ==============================
bool Viewer::SendDataPkt(ClCmdPkt& cmd, ClRspPkt& rsp, QString str)
{
    if(m_serial->sendAndRecv(cmd, rsp) == false)
    {
        ErrorDisConnect();
        if(Cam_format_change == 1)
        {

            Cam_format_change = 0;
            if (m_capThread->isRunning())
            {
                m_capThread->stopCapture();
                m_cam->camera.closeDevice();
                ui->videoLabel->clear();
                ui->btnCamConnect->setText("Connect");
            }
            QMessageBox::warning(this, "경고", "출력 포맷 변경으로 통신 재연결을 해야합니다.");
        }
        else
        {
            QMessageBox::warning(this, "경고", "잘못된 대상과 연결되었습니다. 다시 연결해주세요.");
        }
        return false;
    }
    else
    {
        if(rsp.result == 0x00)
        {
            if(rsp.rw == I2C_RAED)
            {
                if(str != "")
                {
                    log(str + " 읽기 성공");
                }
            }
            else
            {
                if(str != "")
                {
                    log(str + " 쓰기 성공");
                }
            }
            return true;
        }
        else
        {
            if(rsp.rw == I2C_RAED)
            {
                if(str != "")
                {
                    log(str + " 읽기 실패");
                }
            }
            else
            {
                if(str != "")
                {
                    log(str + " 쓰기 실패");
                }
            }
            return false;
        }
    }
    return true;
}


void Viewer::onCameraModeComboChanged()
{
    if(m_updatingCameraCombo) return;
    SetCameraMode();
}

void Viewer::onShutterModeComboChanged()
{
    if(m_updatingDeviceCombo) return;
    SetShutterMode();
}

void Viewer::onShutterCycleTimeEditingFinished()
{
    SetShutterCycle();
}

void Viewer::on_btn_ShutterCycle_Read_clicked()
{
    GetShutterCycle();
}

void Viewer::on_btn_ShutterCycle_Write_clicked()
{
    SetShutterCycle();
}

void Viewer::on_btn_CameraStart_clicked()
{
    SetCameraControl(CAMERA_START);
}

void Viewer::on_btn_CameraStop_clicked()
{
    SetCameraControl(CAMERA_STOP);
}

void Viewer::on_btn_shutterOpen_clicked()
{
    SetShutterControl(SHUTTER_OPEN);
}

void Viewer::on_btn_shutterClose_clicked()
{
    SetShutterControl(SHUTTER_CLOSE);
}

void Viewer::onNucModeComboChanged()
{
    if(m_updatingDeviceCombo) return;
    SetNucMode();
}

void Viewer::onCemComboChanged()
{
    if(m_updatingDeviceCombo) return;
    SetCemMode();
}

void Viewer::on_btn_CINT_Read_clicked()
{
    GetBiasSingle(DETECTOR_SUB_CMD_CINT, ui->horizontalSlider_CINT , ui->lineEdit_CINT);
}

void Viewer::on_btn_CINT_Write_clicked()
{
    SetBuasSinble(DETECTOR_SUB_CMD_CINT, ui->lineEdit_CINT);
}

void Viewer::on_btn_TINT_MSB_Read_clicked()
{
    GetBiasSingle(DETECTOR_SUB_CMD_TINT_MSB, ui->horizontalSlider_TINT_MSB, ui->lineEdit_TINT_MSB);
}

void Viewer::on_btn_TINT_MSB_Write_clicked()
{
    SetBuasSinble(DETECTOR_SUB_CMD_TINT_MSB, ui->lineEdit_TINT_MSB);
}

void Viewer::on_btn_TINT_LSB_Read_clicked()
{
    GetBiasSingle(DETECTOR_SUB_CMD_TINT_LSB, ui->horizontalSlider_TINT_LSB, ui->lineEdit_TINT_LSB);
}

void Viewer::on_btn_TINT_LSB_Write_clicked()
{
    SetBuasSinble(DETECTOR_SUB_CMD_TINT_LSB, ui->lineEdit_TINT_LSB);
}

void Viewer::on_btn_GSK_MSB_Read_clicked()
{
    GetBiasSingle(DETECTOR_SUB_CMD_GSK_MSB, ui->horizontalSlider_GSK_MSB, ui->lineEdit_GSK_MSB);
}

void Viewer::on_btn_GSK_MSB_Write_clicked()
{
    SetBuasSinble(DETECTOR_SUB_CMD_GSK_MSB, ui->lineEdit_GSK_MSB);
}

void Viewer::on_btn_GSK_LSB_Read_clicked()
{
    GetBiasSingle(DETECTOR_SUB_CMD_GSK_LSB, ui->horizontalSlider_GSK_LSB, ui->lineEdit_GSK_LSB);
}

void Viewer::on_btn_GSK_LSB_Write_clicked()
{
    SetBuasSinble(DETECTOR_SUB_CMD_GSK_LSB, ui->lineEdit_GSK_LSB);
}

void Viewer::on_btn_GFID_Read_clicked()
{
    GetBiasSingle(DETECTOR_SUB_CMD_GFID, ui->horizontalSlider_GFID, ui->lineEdit_GFID);
}

void Viewer::on_btn_GFID_Write_clicked()
{
    SetBuasSinble(DETECTOR_SUB_CMD_GFID, ui->lineEdit_GFID);
}

void Viewer::GetFPA_Temp()
{
    ClCmdPkt cmdpkt;
    ClRspPkt rsppkt[2];

    cmdpkt.mainId = DETECTOR;
    cmdpkt.subId = DETECTOR_SUB_CMD_FPA_TEMP_MSB;
    cmdpkt.rw = I2C_RAED;
    cmdpkt.data = 0x00;

    if(SendDataPkt(cmdpkt, rsppkt[0], "FPA_TEMP_MSB") == false)
        return;

    cmdpkt.mainId = DETECTOR;
    cmdpkt.subId = DETECTOR_SUB_CMD_FPA_TEMP_LSB;
    cmdpkt.rw = I2C_RAED;
    cmdpkt.data = 0x00;

    if(SendDataPkt(cmdpkt, rsppkt[1], "FPA_TEMP_LSB") == false)
        return;

    uint16_t fpa_temp = ((uint16_t)rsppkt[0].data << 8) | rsppkt[1].data;
    QString value;

    if(ui->checkBox_Celsius->isChecked())
    {
        float ffpa_temp = DataPacket::ads1115_to_voltage(fpa_temp);
        ffpa_temp = DataPacket::voltage_to_temp(ffpa_temp);
        value = QString::number(ffpa_temp);
        appendTempSample(m_fpaChannel, ffpa_temp);
    }
    else
    {
        value = QString::number(fpa_temp);
        appendTempSample(m_fpaChannel, fpa_temp);
    }

    ui->fpaEntry->setText(value);
}

void Viewer::GetTDA3_Temp()
{
    ClCmdPkt cmdpkt;
    ClRspPkt rsppkt[2];

    cmdpkt.mainId = DEBUG;
    cmdpkt.subId = DEBUG_SUB_CMD_SOC_TEMP_MSB;
    cmdpkt.rw = I2C_RAED;
    cmdpkt.data = 0x00;

    if(SendDataPkt(cmdpkt, rsppkt[0], "SOC_TEMP_MSB") == false)
        return;

    cmdpkt.mainId = DEBUG;
    cmdpkt.subId = DEBUG_SUB_CMD_SOC_TEMP_LSB;
    cmdpkt.rw = I2C_RAED;
    cmdpkt.data = 0x00;

    if(SendDataPkt(cmdpkt, rsppkt[1], "SOC_TEMP_LSB") == false)
        return;

    uint16_t soc_temp = ((uint16_t)rsppkt[0].data << 8) | rsppkt[1].data;
    float fsoc_temp = soc_temp * 0.01f;
    ui->tda3tempEntry->setText(QString::number(fsoc_temp));

    appendTempSample(m_socChannel, fsoc_temp);
}

void Viewer::appendTempSample(TempChannel& ch, float v)
{
    if (!ch.series || !ch.axisX || !ch.axisY) return;

    // 온도가 서서히 변할 때 Y축 밴드(TEMP_RANGE 폭)가 값을 따라가도록 갱신한다.
    constexpr int BAND_HALF = TEMP_RANGE;
    if (ch.bandCenter == std::numeric_limits<int>::min()) {
        ch.bandCenter = (static_cast<int>(v) / TEMP_RANGE) * TEMP_RANGE;
        int minY0 = std::max(0, ch.bandCenter - BAND_HALF);
        int maxY0 = ch.bandCenter + BAND_HALF;
        ch.axisY->setRange(minY0, maxY0);
    }

    ch.series->append(ch.xIndex, v);
    ++ch.xIndex;

    if (ch.series->count() > WINDOW_SAMPLES)
        ch.series->removePoints(0, ch.series->count() - WINDOW_SAMPLES);

    if (ch.xIndex > WINDOW_SAMPLES)
        ch.axisX->setRange(ch.xIndex - WINDOW_SAMPLES, ch.xIndex);

    while (v <= ch.bandCenter - BAND_HALF) ch.bandCenter -= TEMP_RANGE;
    while (v >= ch.bandCenter + BAND_HALF) ch.bandCenter += TEMP_RANGE;

    int minY = std::max(0, ch.bandCenter - BAND_HALF);
    int maxY = ch.bandCenter + BAND_HALF;
    ch.axisY->setRange(minY, maxY);
}

void Viewer::openTempChartPopup(TempChannel& ch, const QString& title, void (Viewer::*getter)())
{
    // 이미 열려있으면 토글로 닫는다 (닫힐 때 closeTempChartPopup 이 호출됨).
    if (ch.popup) {
        ch.popup->close();
        return;
    }

    ch.xIndex = 0;
    ch.bandCenter = std::numeric_limits<int>::min();

    ch.series = new QLineSeries();

    ch.chart = new QChart();
    ch.chart->addSeries(ch.series);
    ch.chart->legend()->hide();
    ch.chart->setTitle(title);

    ch.axisX = new QValueAxis();
    ch.axisX->setRange(0, WINDOW_SAMPLES);
    ch.axisX->setLabelFormat("%d");
    ch.axisX->setTitleText("Time (s)");

    ch.axisY = new QValueAxis();
    ch.axisY->setRange(0, TEMP_RANGE * 2);
    ch.axisY->setLabelFormat("%d");

    ch.chart->setAxisX(ch.axisX, ch.series);
    ch.chart->setAxisY(ch.axisY, ch.series);

    ch.popup = new QDialog(this);
    ch.popup->setAttribute(Qt::WA_DeleteOnClose);
    ch.popup->setWindowTitle(title);
    ch.popup->resize(600, 400);

    auto *chartView = new QChartView(ch.chart);
    chartView->setRenderHint(QPainter::Antialiasing);

    QVBoxLayout *layout = new QVBoxLayout(ch.popup);
    layout->addWidget(chartView);

    // 팝업이 열려있는 동안에만 1초 주기로 값을 읽어 그래프에 반영한다.
    ch.timer = new QTimer(this);
    connect(ch.timer, &QTimer::timeout, this, getter);
    ch.timer->start(1000);

    connect(ch.popup, &QDialog::finished, this, [this, &ch]() {
        closeTempChartPopup(ch);
    });

    ch.popup->show();
    (this->*getter)(); // 즉시 1회 갱신
}

void Viewer::closeTempChartPopup(TempChannel& ch)
{
    if (ch.timer) {
        ch.timer->stop();
        ch.timer->deleteLater();
        ch.timer = nullptr;
    }

    // chart/series/axis 는 popup(QDialog, WA_DeleteOnClose)의 QChartView 가 소유하고 있어
    // popup 이 삭제될 때 함께 삭제된다. 여기서는 참조만 끊어준다.
    ch.chart = nullptr;
    ch.series = nullptr;
    ch.axisX = nullptr;
    ch.axisY = nullptr;
    ch.popup = nullptr;
}

void Viewer::on_checkBox_Celsius_stateChanged(int arg1)
{
    Q_UNUSED(arg1);

    // 표시 단위(raw/섭씨)가 바뀌면 기존 누적값과 스케일이 달라지므로 그래프를 초기화한다.
    if (!m_fpaChannel.series) return;

    m_fpaChannel.series->clear();
    m_fpaChannel.xIndex = 0;
    m_fpaChannel.bandCenter = std::numeric_limits<int>::min();
}

void Viewer::on_btn_histogramPopup_clicked()
{
    if (m_histPopup) {
        m_histPopup->close();
        return;
    }

    m_histPopup = new QDialog(this);
    m_histPopup->setAttribute(Qt::WA_DeleteOnClose);
    m_histPopup->setWindowTitle("Histogram");
    m_histPopup->resize(800, 400);

    m_histPopupWidget = new histogram(m_histPopup);
    QVBoxLayout *layout = new QVBoxLayout(m_histPopup);
    layout->addWidget(m_histPopupWidget);

    connect(m_histPopup, &QDialog::finished, this, [this]() {
        m_histPopup = nullptr;
        m_histPopupWidget = nullptr;
    });

    m_histPopup->show();
}

void Viewer::on_btn_fpaChartPopup_clicked()
{
    openTempChartPopup(m_fpaChannel, "FPA Temperature", &Viewer::GetFPA_Temp);
}

void Viewer::on_btn_socChartPopup_clicked()
{
    openTempChartPopup(m_socChannel, "SOC Temperature", &Viewer::GetTDA3_Temp);
}

void Viewer::on_btn_ReadAll_clicked()
{
    FirstConnectGetInfo();
}

void Viewer::GetTEClessTempRange()
{
    ClCmdPkt cmdpkt;
    ClRspPkt rsppkt;

    cmdpkt.mainId = DEBUG;
    cmdpkt.subId = DEBUG_SUB_CMD_TECLESS_TMP_RANGE;
    cmdpkt.rw = I2C_RAED;
    cmdpkt.data = 0x00;

    if(SendDataPkt(cmdpkt, rsppkt, "teclesstemprage") == false)
    {
        return;
    }
    ui->tecEntry->setText(QString::number(rsppkt.data));
}

void Viewer::on_btn_BIAS_Save_clicked()
{
    QString filePath = QFileDialog::getSaveFileName(
        this, tr("JSON 파일로 저장"),
        QDir::homePath() + "/form.json",
        tr("JSON Files (*.json);;All Files (*.*)"));

    if (filePath.isEmpty()) return;
    if (!filePath.endsWith(".json", Qt::CaseInsensitive)) filePath += ".json";

    const QJsonObject obj = toJson();
    const QByteArray bytes = QJsonDocument(obj).toJson(QJsonDocument::Indented);

    QFile f(filePath);
    if (!f.open(QIODevice::WriteOnly | QIODevice::Truncate)) {
        QMessageBox::critical(this, tr("저장 실패"),
                              tr("파일 열기 실패: %1").arg(f.errorString()));
        return;
    }
    if (f.write(bytes) != bytes.size()) {
        QMessageBox::critical(this, tr("저장 실패"),
                              tr("파일 쓰기 실패(디스크/권한 확인)"));
        f.close();
        return;
    }
    f.close();
    QMessageBox::information(this, tr("완료"), tr("저장되었습니다:\n%1").arg(filePath));
}

void Viewer::on_btn_BIAS_Load_clicked()
{
    QString filePath = QFileDialog::getOpenFileName(
        this, tr("JSON 파일 불러오기"),
        QDir::homePath(),
        tr("JSON Files (*.json);;All Files (*.*)"));

    if (filePath.isEmpty()) return;

    QFile f(filePath);
    if (!f.open(QIODevice::ReadOnly)) {
        QMessageBox::critical(this, tr("불러오기 실패"),
                              tr("파일 열기 실패: %1").arg(f.errorString()));
        return;
    }
    const QByteArray bytes = f.readAll();
    f.close();

    QJsonParseError pe;
    QJsonDocument doc = QJsonDocument::fromJson(bytes, &pe);
    if (pe.error != QJsonParseError::NoError || !doc.isObject()) {
        QMessageBox::critical(this, tr("불러오기 실패"),
                              tr("JSON 파싱 오류: %1").arg(pe.errorString()));
        return;
    }

    QString err;
    if (!fromJson(doc.object(), &err)) {
        QMessageBox::warning(this, tr("부분 적용/오류"), err);
        // 정책에 따라: 일부만 적용된 상태로 둘지, 롤백할지는 선택
    } else {
        QMessageBox::information(this, tr("완료"),
                                 tr("불러왔습니다:\n%1").arg(filePath));
    }
}

void Viewer::on_btn_Make_UserConfig_clicked()
{
    QString filePath = QFileDialog::getSaveFileName(
        this, tr("User Config 파일로 저장"),
        QDir::homePath() + "/data_user_config.bin",
        tr("Binary Files (*.bin);;All Files (*.*)"));

    if (filePath.isEmpty()) return;
    if (!filePath.endsWith(".bin", Qt::CaseInsensitive)) filePath += ".bin";

    // 4바이트(32bit LE)씩 묶었을 때 배열 4-3-2-1, 8-7-6-5, 12-11-10-9, 16-15-14-13 순으로 읽히도록
    // 각 그룹 내에서는 낮은 배열 번호가 LSB(파일 상 앞쪽 오프셋)에 오도록 오름차순으로 기록한다.
    // subIds 는 4배수로 동작해야한다.
    // mainId 가 PADDING_MAIN_ID 인 항목은 장치에서 읽지 않고 0xFF 값으로 채운다.
    constexpr uint8_t PADDING_MAIN_ID = 0xFF;
    struct SubIdEntry { uint8_t mainId; uint8_t subId; };
    static const SubIdEntry subIds[24] = {
        { USER_CONFIGURATION, USER_CONFIGURATION_SUB_CMD_CEM },                    // 1번째 (LSB) 0
        { USER_CONFIGURATION, USER_CONFIGURATION_SUB_CMD_SHUTTER_CYCLE },          // 2번째 1
        { USER_CONFIGURATION, USER_CONFIGURATION_SUB_CMD_SHUTTER },                // 3번째 2
        { USER_CONFIGURATION, USER_CONFIGURATION_SUB_CMD_CAMERA },                 // 4번째 (MSB) 3
        { USER_CONFIGURATION, USER_CONFIGURATION_SUB_CMD_CONTRAST },               // 5번째 (LSB) 4
        { USER_CONFIGURATION, USER_CONFIGURATION_SUB_CMD_BRIGHTNESS },             // 6번째 5
        { USER_CONFIGURATION, USER_CONFIGURATION_SUB_CMD_DISPLAY_SHUTTER_IMAGE },  // 7번째 6
        { USER_CONFIGURATION, USER_CONFIGURATION_SUB_CMD_COLOR_MAP },              // 8번째 (MSB) 7
        { USER_CONFIGURATION, USER_CONFIGURATION_SUB_CMD_CLAHE_GRID },             // 9 // 8
        { USER_CONFIGURATION, USER_CONFIGURATION_SUB_CMD_CONTRAST_TPHE3 },         // 10 // 9
        { USER_CONFIGURATION, USER_CONFIGURATION_SUB_CMD_EDGE_STRENGTH },          // 11 // A
        { USER_CONFIGURATION, USER_CONFIGURATION_SUB_CMD_CONTRAST_TPHE3_CLIP },    // 12 // B
        { USER_CONFIGURATION, USER_CONFIGURATION_SUB_CMD_PGF_ALPHA },              // 13번째 (LSB) C
        { USER_CONFIGURATION, USER_CONFIGURATION_SUB_CMD_PGF_EPS },                // 14번째 D
        { USER_CONFIGURATION, USER_CONFIGURATION_SUB_CMD_NR_CTRL },                // 15번째 E
        { USER_CONFIGURATION, USER_CONFIGURATION_SUB_CMD_CLAHE_THRESHOLD },        // 16번째 (MSB) F
        { USER_CONFIGURATION2, USER_CONFIGURATION2_SUB_CMD_NOISE_THRESHOLD },      // 17
        { USER_CONFIGURATION2, USER_CONFIGURATION2_SUB_CMD_EDGE_THRESHOLD },       // 18
        { USER_CONFIGURATION2, USER_CONFIGURATION2_SUB_CMD_POSTNR_MIX_RATE },      // 19
        { USER_CONFIGURATION2, USER_CONFIGURATION2_SUB_CMD_POSTNR_SCALE },         // 20
        { PADDING_MAIN_ID, 0x00 },                                                 // 23번째 16 (빈 값 0xFF)
        { PADDING_MAIN_ID, 0x00 },                                                 // 24번째 (MSB) 17 (빈 값 0xFF)
        { USER_CONFIGURATION2, USER_CONFIGURATION2_SUB_CMD_TPHE3_CLIP_DOWN },      // 22번째 15
        { USER_CONFIGURATION2, USER_CONFIGURATION2_SUB_CMD_TPHE3_CLIP_UP },        // 21번째 (LSB) 14
    };

    QByteArray bytes;
    bytes.reserve(sizeof(subIds) / sizeof(subIds[0]));

    for (const SubIdEntry& entry : subIds)
    {
        if (entry.mainId == PADDING_MAIN_ID)
        {
            bytes.append(static_cast<char>(0xFF));
            continue;
        }

        cmd.mainId = entry.mainId;
        cmd.subId = entry.subId;
        cmd.rw = I2C_RAED;
        cmd.data = 0x00; // Not Used

        if (SendDataPkt(cmd, rsp, "USER_CONFIG READ") == false)
        {
            QMessageBox::critical(this, tr("생성 실패"),
                                  tr("설정 값을 읽는 중 오류가 발생했습니다. (mainId: 0x%1, subId: 0x%2)")
                                  .arg(entry.mainId, 2, 16, QChar('0'))
                                  .arg(entry.subId, 2, 16, QChar('0')));
            return;
        }
        bytes.append(static_cast<char>(rsp.data));
    }

    QFile f(filePath);
    if (!f.open(QIODevice::WriteOnly | QIODevice::Truncate)) {
        QMessageBox::critical(this, tr("저장 실패"),
                              tr("파일 열기 실패: %1").arg(f.errorString()));
        return;
    }
    if (f.write(bytes) != bytes.size()) {
        QMessageBox::critical(this, tr("저장 실패"),
                              tr("파일 쓰기 실패(디스크/권한 확인)"));
        f.close();
        return;
    }
    f.close();
    QMessageBox::information(this, tr("완료"), tr("생성되었습니다:\n%1").arg(filePath));
}

QVector<QPair<QString, QLineEdit*>> Viewer::fields() const
{
    return {
        { "CINT",  ui->lineEdit_CINT  },
        { "TINT_MSB", ui->lineEdit_TINT_MSB },
        { "TINT_LSB",  ui->lineEdit_TINT_LSB  },
        { "GSK_MSB",  ui->lineEdit_GSK_MSB  },
        { "GSK_LSB", ui->lineEdit_GSK_LSB },
        { "GFID",  ui->lineEdit_GFID  }
    };
}

QJsonObject Viewer::toJson() const
{
    QJsonObject obj;
    obj["schemaVersion"] = 1; // 선택: 호환성용 버전
    for (const auto& kv : fields()) {
        if (!kv.second) continue;
        obj[kv.first] = kv.second->text();
    }
    return obj;
}

bool Viewer::fromJson(const QJsonObject& obj, QString* err)
{
    QStringList missing, wrongType;
    for (const auto& kv : fields()) {
        const QString& key = kv.first;
        QLineEdit* edit = kv.second;
        if (!edit) continue;

        if (!obj.contains(key)) { missing << key; continue; }
        if (!obj.value(key).isString()) { wrongType << key; continue; }

        edit->setText(obj.value(key).toString());
    }

    if (!missing.isEmpty() || !wrongType.isEmpty()) {
        if (err) {
            *err = tr("누락 키: [%1]\n타입 오류 키: [%2]")
                     .arg(missing.join(", "))
                     .arg(wrongType.join(", "));
        }
        // 모두 채워져야 성공으로 볼지, 일부만 채워도 성공으로 볼지는 정책에 따라 결정
        // 여기선 일부 문제 있으면 false
        return missing.isEmpty() && wrongType.isEmpty();
    }
    return true;
}

void Viewer::ErrorDisConnect()
{

    if(TEClessTempRangetimer)
    {
        TEClessTempRangetimer->stop();
        TEClessTempRangetimer = nullptr;
        ui->btn_tecless_temp_range->setText("Start");
        disconnect(TEClessTempRangetimer, &QTimer::timeout, this, &Viewer::GetTEClessTempRange);
    }

    const int connectionsIndex = ui->rightTabWidget->indexOf(ui->tabConnections);
    if (connectionsIndex >= 0) {
        ui->rightTabWidget->setCurrentIndex(connectionsIndex);
    }
    for (int i = 0; i < ui->rightTabWidget->count(); ++i) {
        ui->rightTabWidget->setTabEnabled(i, i == connectionsIndex);
    }
    ui->btn_tecless_temp_range->setEnabled(false);
    ui->btnPortConnect->setText("Connect");
    m_serial->Close();
    emit connectionStateChanged(false, QString());
}

void Viewer::FirstConnectGetInfo()
{
    if(GetSerialNumber() == false) return;
    if(GetShutterControl()== false) return;
    if(GetCameraMode()== false) return;
    if(GetShutterMode()== false) return;
    if(GetShutterCycle()== false) return;
    if(GetCameraControl()== false) return;
    if(GetNucMode()== false) return;
    if(GetCemMode()== false) return;
    if(GetAllBias()== false) return;
    if(GetTDA3Ver()== false) return;
    if(GetFX3Ver()== false) return;
    if(GetColorMap()== false) return;
    if(GetDisplayShutterImage()== false) return;
    if(GetBrightnessValue() == false) return;
    if(GetContrastValue() == false) return;
    if(GetEdgeStrengthValue() == false) return;
    if(GetZoomFactor() == false) return;
    if(GetClaheGrid() == false) return;
    if(GetClaheThreshold() == false) return;
    if(GetNrCtrl() == false) return;
    if(GetPgfEps() == false) return;
    if(GetPgfAlpha() == false) return;
    if(GetPostNrScale() == false) return;
    if(GetPostNrMixRate() == false) return;
    if(GetNoiseThresHold() == false) return;
    if(GetEdgeThresHold() == false) return;
    if(GetTphe3ClipUp() == false) return;
    if(GetTphe3ClipDown() == false) return;
}
// serial read
bool Viewer::GetSerialNumber()
{
    ClCmdPkt cmdPkt;
    ClRspPkt rspPkt[4];
    cmdPkt.mainId = DETECTOR;
    cmdPkt.subId = DETECTOR_SUB_CMD_SERIAL_NB_A;
    cmdPkt.rw = I2C_RAED;
    cmdPkt.data = 0x00; // Not Used

    if(SendDataPkt(cmdPkt, rspPkt[0], "SERIAL_NB_A") == false)
    {
        return false;
    }

    cmdPkt.mainId = DETECTOR;
    cmdPkt.subId = DETECTOR_SUB_CMD_SERIAL_NB_B;
    cmdPkt.rw = I2C_RAED;
    cmdPkt.data = 0x00; // Not Used

    if(SendDataPkt(cmdPkt, rspPkt[1], "SERIAL_NB_B") == false)
    {
        return false;
    }

    cmdPkt.mainId = DETECTOR;
    cmdPkt.subId = DETECTOR_SUB_CMD_SERIAL_NB_C;
    cmdPkt.rw = I2C_RAED;
    cmdPkt.data = 0x00; // Not Used

    if(SendDataPkt(cmdPkt, rspPkt[2], "SERIAL_NB_C") == false)
    {
        return false;
    }

    cmdPkt.mainId = DETECTOR;
    cmdPkt.subId = DETECTOR_SUB_CMD_SERIAL_NB_D;
    cmdPkt.rw = I2C_RAED;
    cmdPkt.data = 0x00; // Not Used

    if(SendDataPkt(cmdPkt, rspPkt[3], "SERIAL_NB_D") == false)
    {
        return false;
    }

    uint8_t SERIAL_NB_A = rspPkt[0].data;
    uint8_t SERIAL_NB_B = rspPkt[1].data;
    uint8_t SERIAL_NB_C = rspPkt[2].data;
    uint8_t SERIAL_NB_D = rspPkt[3].data;

    QString SensorSerialNumber = DataPacket::makeSerialString(SERIAL_NB_A, SERIAL_NB_B, SERIAL_NB_C, SERIAL_NB_D);
    ui->serialEntry->setText(SensorSerialNumber);
    return true;
}
bool Viewer::GetCameraMode()
{
    cmd.mainId = USER_CONFIGURATION;
    cmd.subId = USER_CONFIGURATION_SUB_CMD_CAMERA;
    cmd.rw = I2C_RAED;
    cmd.data = 0x00; // Not Used

    if(SendDataPkt(cmd, rsp, "카메라 설정") == false)
        return false;
    uint8_t autustart = ((rsp.data >> 7) & 0x01);
    uint8_t displaymode = ((rsp.data >> 4) & 0x07);
    uint8_t outformat = ((rsp.data >> 2) & 0x03);
    uint8_t captureType = ((rsp.data >> 1) & 0x01);
    uint8_t opmode = (rsp.data & 0x01);

    if(displaymode > 0)
    {
        displaymode -= 1;
    }

    m_updatingCameraCombo = true;
    ui->comboBox_autostart->setCurrentIndex(autustart);
    ui->comboBox_displaymode->setCurrentIndex(displaymode);
    ui->comboBox_outformat->setCurrentIndex(outformat);
    ui->comboBox_captureType->setCurrentIndex(captureType);
    ui->comboBox_OPmode->setCurrentIndex(opmode);
    m_updatingCameraCombo = false;

    if(opmode == 0x01)
    {
        ui->groupBox_NUC->setDisabled(true);
        ui->groupBox_CEM->setDisabled(true);
    }
    else
    {
        ui->groupBox_NUC->setDisabled(false);
        ui->groupBox_CEM->setDisabled(false);
    }

    ShutterControlCheck();

    return true;
}
bool Viewer::GetShutterMode()
{
    cmd.mainId = USER_CONFIGURATION;
    cmd.subId = USER_CONFIGURATION_SUB_CMD_SHUTTER;
    cmd.rw = I2C_RAED;
    cmd.data = 0x00; // Not Used

    if(SendDataPkt(cmd, rsp, "셔터 모드") == false)
        return false;

    uint8_t shuttermode = (rsp.data & 0x03);
    m_updatingDeviceCombo = true;
    ui->comboBox_shutterMode->setCurrentIndex(shuttermode);
    m_updatingDeviceCombo = false;

    FFCCheck();
    ShutterControlCheck();

    return true;
}
bool Viewer::GetShutterCycle()
{
    cmd.mainId = USER_CONFIGURATION;
    cmd.subId = USER_CONFIGURATION_SUB_CMD_SHUTTER_CYCLE;
    cmd.rw = I2C_RAED;
    cmd.data = 0x00; // Not Used

    if(SendDataPkt(cmd, rsp, "셔터 싸이클 시간") == false)
        return false;
    QString time = QString::number(rsp.data);
    ui->lineEdit_CycleTime->setText(time);

    return true;
}
bool Viewer::GetCameraControl()
{
    cmd.mainId = OPERATION_CONTROL;
    cmd.subId = OPERATION_CONTROL_SUB_CMD_CAMERA;
    cmd.rw = I2C_RAED;
    cmd.data = 0x00; // Not Used

    if(SendDataPkt(cmd, rsp, "") == false)
        return false;
    if(rsp.data == 0x00) // stop
    {
        ui->btn_CameraStart->setEnabled(true);
        ui->btn_CameraStop->setEnabled(false);
    }
    else if(rsp.data == 0x01) // start
    {
        ui->btn_CameraStart->setEnabled(false);
        ui->btn_CameraStop->setEnabled(true);
    }
    return true;
}
bool Viewer::GetShutterControl()
{
    cmd.mainId = OPERATION_CONTROL;
    cmd.subId = OPERATION_CONTROL_SUB_CMD_SHUTTER;
    cmd.rw = I2C_RAED;
    cmd.data = 0x00; // Not Used

    if(SendDataPkt(cmd, rsp, "") == false)
        return false;
    if(rsp.data == 0x00) // close
    {
        ui->btn_shutterOpen->setEnabled(true);
        ui->btn_shutterClose->setEnabled(false);
    }
    else if(rsp.data == 0x01) // open
    {
        ui->btn_shutterOpen->setEnabled(false);
        ui->btn_shutterClose->setEnabled(true);
    }
    return true;
}
bool Viewer::GetNucMode()
{
    cmd.mainId = NUC;
    cmd.subId = NUC_SUB_CMD_NUC_MODE;
    cmd.rw = I2C_RAED;
    cmd.data = 0x00; // Not Used

    if(SendDataPkt(cmd, rsp, "NUC MODE") == false)
        return false;

    uint8_t nucmode = (rsp.data & 0x03);

    m_updatingDeviceCombo = true;
    ui->comboBox_NucMode->setCurrentIndex(nucmode);
    m_updatingDeviceCombo = false;

    FFCCheck();

    return true;
}
bool Viewer::GetCemMode()
{
    cmd.mainId = USER_CONFIGURATION;
    cmd.subId = USER_CONFIGURATION_SUB_CMD_CEM;
    cmd.rw = I2C_RAED;
    cmd.data = 0x00; // Not Used

    if(SendDataPkt(cmd, rsp, "CEM") == false)
        return false;

    uint8_t edge = (rsp.data & 0x01);
    uint8_t postNr = ((rsp.data >> 1) & 0x01);
    uint8_t tphe = ((rsp.data >> 2) & 0x03);
    uint8_t bpc = ((rsp.data >> 4) & 0x01);
    uint8_t preNr = ((rsp.data >> 5) & 0x01);
    uint8_t clahe = ((rsp.data >> 6) & 0x01);
    uint8_t boxF = ((rsp.data >> 7) & 0x01);

    m_updatingDeviceCombo = true;
    ui->comboBox_CEM_Edge->setCurrentIndex(edge);
    ui->comboBox_CEM_TPHE->setCurrentIndex(tphe);
    ui->comboBox_CEM_BPC->setCurrentIndex(bpc);
    ui->comboBox_CEM_CLAHE->setCurrentIndex(clahe);
    ui->comboBox_CEM_BoxFilter->setCurrentIndex(boxF);
    ui->comboBox_CEM_PreNr->setCurrentIndex(preNr);
    ui->comboBox_CEM_PostNr->setCurrentIndex(postNr);
    m_updatingDeviceCombo = false;

    tphe_mode = tphe;

    if(tphe_mode == 0) // Normalization
    {
        ui->groupBox_contrast->setEnabled(0);
    }

    if(tphe_mode == 3) // TPHE3
    {
        ui->radioButton_adpativeThrEn->setEnabled(1);
    }
    else
    {
        ui->radioButton_adpativeThrEn->setEnabled(0);
    }

    if(edge == 0)
    {
        ui->groupBox_edgeStrength->setEnabled(0);
    }
    if(clahe == 0)
    {
        ui->groupBox_CLAHE->setEnabled(0);
    }
    if(postNr == 0)
    {
        ui->groupBox_NR_Ctrl->setEnabled(0);
    }

    return true;
}

bool Viewer::GetAllBias()
{
    if(GetBiasSingle(DETECTOR_SUB_CMD_CINT, ui->horizontalSlider_CINT, ui->lineEdit_CINT) == false) return false;
    if(GetBiasSingle(DETECTOR_SUB_CMD_TINT_MSB, ui->horizontalSlider_TINT_MSB, ui->lineEdit_TINT_MSB) == false) return false;
    if(GetBiasSingle(DETECTOR_SUB_CMD_TINT_LSB, ui->horizontalSlider_TINT_LSB, ui->lineEdit_TINT_LSB) == false) return false;
    if(GetBiasSingle(DETECTOR_SUB_CMD_GSK_MSB, ui->horizontalSlider_GSK_MSB, ui->lineEdit_GSK_MSB) == false) return false;
    if(GetBiasSingle(DETECTOR_SUB_CMD_GSK_LSB, ui->horizontalSlider_GSK_LSB, ui->lineEdit_GSK_LSB) == false) return false;
    if(GetBiasSingle(DETECTOR_SUB_CMD_GFID, ui->horizontalSlider_GFID, ui->lineEdit_GFID) == false) return false;

    return true;
}

bool Viewer::GetTDA3Ver()
{
    ClCmdPkt cmdPkt;
    ClRspPkt rspPkt[3];
    cmdPkt.mainId = DEBUG;
    cmdPkt.subId = DEBUG_SUB_CMD_FIRM_VER_MAJOR;
    cmdPkt.rw = I2C_RAED;
    cmdPkt.data = 0x00; // Not Used

    if(SendDataPkt(cmdPkt, rspPkt[0], "DEBUG_SUB_CMD_FIRM_VER_MAJOR") == false)
    {
        return false;
    }

    cmdPkt.mainId = DEBUG;
    cmdPkt.subId = DEBUG_SUB_CMD_FIRM_VER_MINOR;
    cmdPkt.rw = I2C_RAED;
    cmdPkt.data = 0x00; // Not Used

    if(SendDataPkt(cmdPkt, rspPkt[1], "DEBUG_SUB_CMD_FIRM_VER_MINOR") == false)
    {
        return false;
    }

    cmdPkt.mainId = DEBUG;
    cmdPkt.subId = DEBUG_SUB_CMD_FIRM_VER_PATCH;
    cmdPkt.rw = I2C_RAED;
    cmdPkt.data = 0x00; // Not Used

    if(SendDataPkt(cmdPkt, rspPkt[2], "DEBUG_SUB_CMD_FIRM_VER_PATCH") == false)
    {
        return false;
    }

    QString Major = QString::number(rspPkt[0].data);
    QString Minor = QString::number(rspPkt[1].data);
    QString Patch = QString::number(rspPkt[2].data);

    ui->TDA3Entry->setText(Major + "." + Minor + "." + Patch);
    return true;
}

bool Viewer::GetFX3Ver()
{
    ClCmdPkt cmdPkt;
    ClRspPkt rspPkt[3];
    cmdPkt.mainId = DEBUG;
    cmdPkt.subId = DEBUG_SUB_CMD_FX3_FIRM_VER_MAJOR;
    cmdPkt.rw = I2C_RAED;
    cmdPkt.data = 0x00; // Not Used

    if(SendDataPkt(cmdPkt, rspPkt[0], "DEBUG_SUB_CMD_FX3_FIRM_VER_MAJOR") == false)
    {
        return false;
    }

    cmdPkt.mainId = DEBUG;
    cmdPkt.subId = DEBUG_SUB_CMD_FX3_FIRM_VER_MINOR;
    cmdPkt.rw = I2C_RAED;
    cmdPkt.data = 0x00; // Not Used

    if(SendDataPkt(cmdPkt, rspPkt[1], "DEBUG_SUB_CMD_FX3_FIRM_VER_MINOR") == false)
    {
        return false;
    }

    cmdPkt.mainId = DEBUG;
    cmdPkt.subId = DEBUG_SUB_CMD_FX3_FIRM_VER_PATCH;
    cmdPkt.rw = I2C_RAED;
    cmdPkt.data = 0x00; // Not Used

    if(SendDataPkt(cmdPkt, rspPkt[2], "DEBUG_SUB_CMD_FX3_FIRM_VER_PATCH") == false)
    {
        return false;
    }

    QString Major = QString::number(rspPkt[0].data);
    QString Minor = QString::number(rspPkt[1].data);
    QString Patch = QString::number(rspPkt[2].data);

    ui->FX3Entry->setText(Major + "." + Minor + "." + Patch);
    return true;
}

bool Viewer::GetFFC()
{
    cmd.mainId = OPERATION_CONTROL;
    cmd.subId = OPERATION_CONTROL_SUB_CMD_FFC;
    cmd.rw = I2C_WRITE;
    cmd.data = 0x01;

    if(SendDataPkt(cmd, rsp, "OPERATION_CONTROL_SUB_CMD_FFC") == false)
    {
        return false;
    }
    return true;
}


// serial write
bool Viewer::SetCameraMode()
{
    cmd.mainId = USER_CONFIGURATION;
    cmd.subId = USER_CONFIGURATION_SUB_CMD_CAMERA;
    cmd.rw = I2C_RAED;
    cmd.data = 0x00; // Not Used

    if(SendDataPkt(cmd, rsp, "") == false)
        return false;

    uint8_t r_outformat = ((rsp.data >> 2) & 0x03);
    uint8_t r_opmode = (rsp.data & 0x01);

    int autoStart = ui->comboBox_autostart->currentIndex();
    int dispMode = ui->comboBox_displaymode->currentIndex();
    int outFormat = ui->comboBox_outformat->currentIndex();
    int captureType = ui->comboBox_captureType->currentIndex();
    int opMode = ui->comboBox_OPmode->currentIndex();

    quint8 reg = 0;

    if(dispMode > 0)
    {
        dispMode += 1;
    }

    reg |= ( (autoStart   & 0x1) << 7 );
    reg |= ( (dispMode    & 0x7) << 4 );
    reg |= ( (outFormat   & 0x3) << 2 );
    reg |= ( (captureType & 0x1) << 1 );
    reg |= ( (opMode      & 0x1) << 0 );

    cmd.rw = I2C_WRITE;
    cmd.data = reg;

    if(r_opmode == MODE_NORMAL && opMode == MODE_FACTORY)
    {
        if(r_outformat == OUT_UYVY)
        {
            Cam_format_change = 1;
        }
    }
    else if(opMode == MODE_NORMAL)
    {
        if(r_outformat != outFormat)
        {
            Cam_format_change = 1;
        }
    }

    SendDataPkt(cmd, rsp, "카메라 설정");

    if(opMode == 0x01)
    {
        ui->groupBox_NUC->setDisabled(true);
        ui->groupBox_CEM->setDisabled(true);
    }
    else
    {
        ui->groupBox_NUC->setDisabled(false);
        ui->groupBox_CEM->setDisabled(false);
    }

    ShutterControlCheck();

    return true;
}
bool Viewer::SetShutterMode()
{
    uint8_t shutterMode = ui->comboBox_shutterMode->currentIndex();
    cmd.mainId = USER_CONFIGURATION;
    cmd.subId = USER_CONFIGURATION_SUB_CMD_SHUTTER;
    cmd.rw = I2C_WRITE;
    cmd.data = shutterMode; // Not Used

    if(SendDataPkt(cmd, rsp, "셔터 모드") == false)
        return false;

    FFCCheck();
    ShutterControlCheck();

    return true;
}
bool Viewer::SetShutterCycle()
{
    uint8_t cycletime = ui->lineEdit_CycleTime->text().toUInt();
    cmd.mainId = USER_CONFIGURATION;
    cmd.subId = USER_CONFIGURATION_SUB_CMD_SHUTTER_CYCLE;
    cmd.rw = I2C_WRITE;
    cmd.data = cycletime; // Not Used

    if(SendDataPkt(cmd, rsp, "셔터 싸이클 시간") == false)
        return false;

    return true;
}
bool Viewer::SetCameraControl(uint8_t mode)
{
    cmd.mainId = OPERATION_CONTROL;
    cmd.subId = OPERATION_CONTROL_SUB_CMD_CAMERA;
    cmd.rw = I2C_WRITE;

    if(mode == CAMERA_START)
    {
        cmd.data = CAMERA_START;
        ui->btn_CameraStart->setEnabled(false);
        ui->btn_CameraStop->setEnabled(true);
    }
    else
    {
        cmd.data = CAMERA_STOP;
        ui->btn_CameraStart->setEnabled(true);
        ui->btn_CameraStop->setEnabled(false);
    }
    if(SendDataPkt(cmd, rsp, "카메라 START") == false)
        return false;
    return true;
}
bool Viewer::SetShutterControl(uint8_t mode)
{
    cmd.mainId = OPERATION_CONTROL;
    cmd.subId = OPERATION_CONTROL_SUB_CMD_SHUTTER;
    cmd.rw = I2C_WRITE;
    if(mode == SHUTTER_OPEN)
    {
        cmd.data = SHUTTER_OPEN;
        ui->btn_shutterOpen->setEnabled(false);
        ui->btn_shutterClose->setEnabled(true);
    }
    else
    {
        cmd.data = SHUTTER_CLOSE;
        ui->btn_shutterOpen->setEnabled(true);
        ui->btn_shutterClose->setEnabled(false);
    }
    if(SendDataPkt(cmd, rsp, "") == false)
        return false;
    return true;
}
bool Viewer::SetNucMode()
{
    uint8_t NucMode = ui->comboBox_NucMode->currentIndex();

    quint8 reg = 0;
    reg |= ( (NucMode  & 0x3) << 0 );

    cmd.mainId = NUC;
    cmd.subId = NUC_SUB_CMD_NUC_MODE;
    cmd.rw = I2C_WRITE;
    cmd.data = reg;

    if(SendDataPkt(cmd, rsp, "NUC_MODE") == false)
        return false;

    FFCCheck();

    return true;
}
bool Viewer::SetCemMode()
{
    uint8_t Edge = ui->comboBox_CEM_Edge->currentIndex();
    uint8_t Tphe = ui->comboBox_CEM_TPHE->currentIndex();
    uint8_t Bpc = ui->comboBox_CEM_BPC->currentIndex();
    uint8_t clahe = ui->comboBox_CEM_CLAHE->currentIndex();
    uint8_t boxF = ui->comboBox_CEM_BoxFilter->currentIndex();
    uint8_t preNr = ui->comboBox_CEM_PreNr->currentIndex();
    uint8_t postNr = ui->comboBox_CEM_PostNr->currentIndex();

    quint8 reg = 0;
    reg |= ((boxF & 0x1) << 7);
    reg |= ( (clahe & 0x1) << 6 );
    reg |= ( (preNr  & 0x1) << 5 );
    reg |= ( (Bpc & 0x1) << 4 );
    reg |= ( (Tphe  & 0x3) << 2 );
    reg |= ( (postNr  & 0x1) << 1 );
    reg |= ( (Edge  & 0x1) << 0 );

    cmd.mainId = USER_CONFIGURATION;
    cmd.subId = USER_CONFIGURATION_SUB_CMD_CEM;
    cmd.rw = I2C_WRITE;
    cmd.data = reg;

    if(SendDataPkt(cmd, rsp, "CEM") == false)
        return false;

    tphe_mode = Tphe;
    if(tphe_mode == 0) // Normalization
    {
        ui->groupBox_contrast->setEnabled(0);
        ui->radioButton_adpativeThrEn->setEnabled(0);
    }
    else if(tphe_mode == 3) // TPHE3
    {
        ui->groupBox_contrast->setEnabled(1);
        ui->radioButton_adpativeThrEn->setEnabled(1);
    }
    else
    {
        ui->groupBox_contrast->setEnabled(1);
        ui->radioButton_adpativeThrEn->setEnabled(0);
    }

    if(Edge == 0)
    {
        ui->groupBox_edgeStrength->setEnabled(0);
    }
    else
    {
        ui->groupBox_edgeStrength->setEnabled(1);
    }

    if(clahe == 0)
    {
        ui->groupBox_CLAHE->setEnabled(0);
    }
    else
    {
        ui->groupBox_CLAHE->setEnabled(1);
    }

    if(postNr == 0)
    {
        ui->groupBox_NR_Ctrl->setEnabled(0);
    }
    else
    {
        ui->groupBox_NR_Ctrl->setEnabled(1);
    }

    GetContrastValue();
    return true;
}
bool Viewer::GetBiasSingle(uint8_t Sub, QSlider* slider, QLineEdit* lineedit)
{
    cmd.mainId = DETECTOR;
    cmd.subId = Sub;
    cmd.rw = I2C_RAED;
    cmd.data = 0x00; // Not Used

    if(SendDataPkt(cmd, rsp, "") == false)
        return false;

    applyBiasValue(slider, lineedit, rsp.data);

    return true;
}
bool Viewer::SetBuasSinble(uint8_t Sub, QLineEdit* lineedit)
{
    bool ok;
    uint8_t data = lineedit->text().toInt(&ok, 16);

    cmd.mainId = DETECTOR;
    cmd.subId = Sub;
    cmd.rw = I2C_WRITE;
    cmd.data = data;
    if(SendDataPkt(cmd, rsp, "") == false)
        return false;
    return true;
}

void Viewer::applyBiasValue(QSlider* slider, QLineEdit* linedit, int v)
{
    v = std::clamp(v, slider->minimum(), slider->maximum());
    QSignalBlocker b1(linedit);
    QSignalBlocker b2(slider);
    linedit->setText(DataPacket::toHexUpperNoPad(static_cast<uint8_t>(v)));
    slider->setValue(v);
}

void Viewer::on_horizontalSlider_CINT_valueChanged(int value)
{
    applyBiasValue(ui->horizontalSlider_CINT, ui->lineEdit_CINT, value);
}

void Viewer::on_lineEdit_CINT_textChanged(const QString &arg1)
{
    int v;
    if (DataPacket::parseHex8(arg1, v))
        applyBiasValue(ui->horizontalSlider_CINT, ui->lineEdit_CINT, v);
}

void Viewer::on_horizontalSlider_TINT_MSB_valueChanged(int value)
{
    applyBiasValue(ui->horizontalSlider_TINT_MSB, ui->lineEdit_TINT_MSB, value);
}

void Viewer::on_lineEdit_TINT_MSB_textChanged(const QString &arg1)
{
    int v;
    if (DataPacket::parseHex8(arg1, v))
        applyBiasValue(ui->horizontalSlider_TINT_MSB, ui->lineEdit_TINT_MSB, v);
}

void Viewer::on_horizontalSlider_TINT_LSB_valueChanged(int value)
{
    applyBiasValue(ui->horizontalSlider_TINT_LSB, ui->lineEdit_TINT_LSB, value);
}

void Viewer::on_lineEdit_TINT_LSB_textChanged(const QString &arg1)
{
    int v;
    if (DataPacket::parseHex8(arg1, v))
        applyBiasValue(ui->horizontalSlider_TINT_LSB, ui->lineEdit_TINT_LSB, v);
}

void Viewer::on_horizontalSlider_GSK_MSB_valueChanged(int value)
{
    applyBiasValue(ui->horizontalSlider_GSK_MSB, ui->lineEdit_GSK_MSB, value);
}

void Viewer::on_lineEdit_GSK_MSB_textChanged(const QString &arg1)
{
    int v;
    if (DataPacket::parseHex8(arg1, v))
        applyBiasValue(ui->horizontalSlider_GSK_MSB, ui->lineEdit_GSK_MSB, v);
}

void Viewer::on_horizontalSlider_GSK_LSB_valueChanged(int value)
{
    applyBiasValue(ui->horizontalSlider_GSK_LSB, ui->lineEdit_GSK_LSB, value);
}

void Viewer::on_lineEdit_GSK_LSB_textChanged(const QString &arg1)
{
    int v;
    if (DataPacket::parseHex8(arg1, v))
        applyBiasValue(ui->horizontalSlider_GSK_LSB, ui->lineEdit_GSK_LSB, v);
}

void Viewer::on_horizontalSlider_GFID_valueChanged(int value)
{
    applyBiasValue(ui->horizontalSlider_GFID, ui->lineEdit_GFID, value);
}

void Viewer::on_lineEdit_GFID_textChanged(const QString &arg1)
{
    int v;
    if (DataPacket::parseHex8(arg1, v))
        applyBiasValue(ui->horizontalSlider_GFID, ui->lineEdit_GFID, v);
}

void UpDownFunction(uint8_t updown, QSlider* slider, QLineEdit* lineedit)
{
    bool ok = false;
    QString v = lineedit->text();
    int vv = v.toInt(&ok, 16);

    if(updown == UP)
    {
        vv++;
    }
    else
    {
        vv--;
    }

    if(slider->maximum() < vv || slider->minimum() > vv)
        return;

    lineedit->setText(QString::number(vv, 16).toUpper());
}

void Viewer::on_btn_CINT_countup_clicked()
{
    UpDownFunction(UP, ui->horizontalSlider_CINT, ui->lineEdit_CINT);
}

void Viewer::on_btn_CINT_countdown_clicked()
{
    UpDownFunction(DOWN, ui->horizontalSlider_CINT, ui->lineEdit_CINT);
}

void Viewer::on_btn_TINT_MSB_countup_clicked()
{
    UpDownFunction(UP, ui->horizontalSlider_TINT_MSB, ui->lineEdit_TINT_MSB);
}

void Viewer::on_btn_TINT_MSB_countdown_clicked()
{
    UpDownFunction(DOWN, ui->horizontalSlider_TINT_MSB, ui->lineEdit_TINT_MSB);
}

void Viewer::on_btn_TINT_LSB_countup_clicked()
{
    UpDownFunction(UP, ui->horizontalSlider_TINT_LSB, ui->lineEdit_TINT_LSB);
}

void Viewer::on_btn_TINT_LSB_countdown_clicked()
{
    UpDownFunction(DOWN, ui->horizontalSlider_TINT_LSB, ui->lineEdit_TINT_LSB);
}

void Viewer::on_btn_GSK_MSB_countup_clicked()
{
    UpDownFunction(UP, ui->horizontalSlider_GSK_MSB, ui->lineEdit_GSK_MSB);
}

void Viewer::on_btn_GSK_MSB_countdown_clicked()
{
    UpDownFunction(DOWN, ui->horizontalSlider_GSK_MSB, ui->lineEdit_GSK_MSB);
}

void Viewer::on_btn_GSK_LSB_countup_clicked()
{
    UpDownFunction(UP, ui->horizontalSlider_GSK_LSB, ui->lineEdit_GSK_LSB);
}

void Viewer::on_btn_GSK_LSB_countdown_clicked()
{
    UpDownFunction(DOWN, ui->horizontalSlider_GSK_LSB, ui->lineEdit_GSK_LSB);
}

void Viewer::on_btn_GFID_countup_clicked()
{
    UpDownFunction(UP, ui->horizontalSlider_GFID, ui->lineEdit_GFID);
}

void Viewer::on_btn_GFID_countdown_clicked()
{
    UpDownFunction(DOWN, ui->horizontalSlider_GFID, ui->lineEdit_GFID);
}

void Viewer::on_btn_OpenDebug_clicked()
{
    if (!m_log) {
        m_log = new LogDialog(this);
        connect(this, &Viewer::logMessage, m_log, &LogDialog::appendLog, Qt::QueuedConnection);
    }
    m_log->show();
    m_log->raise();
    m_log->activateWindow();
}

void Viewer::log(const QString& s)
{
    const QString ts = QDateTime::currentDateTime().toString("HH:mm:ss.zzz");
    emit logMessage(ts + " | " + s);
}

void Viewer::on_btn_GetFFC_clicked()
{
    GetFFC();
}

void Viewer::on_btn_tecless_temp_range_clicked()
{
    if(TEClessTempRangetimer)
    {
        TEClessTempRangetimer->stop();
        TEClessTempRangetimer = nullptr;
        ui->btn_tecless_temp_range->setText("Start");
        disconnect(TEClessTempRangetimer, &QTimer::timeout, this, &Viewer::GetTEClessTempRange);
        return;
    }
    else
    {
        TEClessTempRangetimer = new QTimer(this);
        ui->btn_tecless_temp_range->setText("Stop");
        connect(TEClessTempRangetimer, &QTimer::timeout, this, &Viewer::GetTEClessTempRange);
    }

    // 1초마다(myFunction) 호출
    TEClessTempRangetimer->start(1000);
}

bool Viewer::FFCCheck()
{
    const bool enabled = ui->comboBox_shutterMode->currentIndex() == 1
        && (ui->comboBox_NucMode->currentIndex() == 0
            || ui->comboBox_NucMode->currentIndex() == 1);
    ui->btn_GetFFC->setEnabled(enabled);
    return enabled;
}

bool Viewer::ShutterControlCheck()
{
    const bool enabled = ui->comboBox_shutterMode->currentIndex() == 1
        || ui->comboBox_OPmode->currentIndex() == 1;
    ui->btn_shutterOpen->setEnabled(enabled);
    ui->btn_shutterClose->setEnabled(enabled);
    return enabled;
}

void Viewer::on_btn_TDA3_RESET_clicked()
{
    cmd.mainId = OPERATION_CONTROL;
    cmd.subId = OPERATION_CONTROL_SUB_CMD_TDA3_RESET;
    cmd.rw = I2C_WRITE;
    cmd.data = 0x00;

    SendDataPkt(cmd, rsp, "OPERATION_CONTROL_SUB_CMD_TDA3_RESET");
}

void Viewer::onColorMapComboChanged()
{
    if(m_updatingDeviceCombo) return;
    SetColorMap();
}

bool Viewer::GetColorMap()
{
    cmd.mainId = USER_CONFIGURATION;
    cmd.subId = USER_CONFIGURATION_SUB_CMD_COLOR_MAP;
    cmd.rw = I2C_RAED;

    if(SendDataPkt(cmd, rsp, "COLORMAP") == false)
        return false;

    uint8_t colormap = rsp.data;

    m_updatingDeviceCombo = true;
    ui->comboBox_ColorMap->setCurrentIndex(colormap);
    m_updatingDeviceCombo = false;

    return true;
}

bool Viewer::SetColorMap()
{
    uint8_t colormap = ui->comboBox_ColorMap->currentIndex();
    quint8 reg = colormap;

    cmd.mainId = USER_CONFIGURATION;
    cmd.subId = USER_CONFIGURATION_SUB_CMD_COLOR_MAP;
    cmd.rw = I2C_WRITE;
    cmd.data = reg;

    if(SendDataPkt(cmd, rsp, "COLORMAP") == false)
        return false;
    return true;
}

void Viewer::onDisplayShutterImageComboChanged()
{
    if(m_updatingDeviceCombo) return;
    SetDisplayShutterImage();
}

bool Viewer::GetDisplayShutterImage()
{
    cmd.mainId = USER_CONFIGURATION;
    cmd.subId = USER_CONFIGURATION_SUB_CMD_DISPLAY_SHUTTER_IMAGE;
    cmd.rw = I2C_RAED;

    if(SendDataPkt(cmd, rsp, "DISPLAY_SHUTTER_IMAGE") == false)
        return false;

    uint8_t displayshutterimage = rsp.data;

    m_updatingDeviceCombo = true;
    ui->comboBox_DisplayShutterImage->setCurrentIndex(displayshutterimage);
    m_updatingDeviceCombo = false;

    return true;
}

bool Viewer::SetDisplayShutterImage()
{
    uint8_t displayshutterimage = ui->comboBox_DisplayShutterImage->currentIndex();
    quint8 reg = displayshutterimage;

    cmd.mainId = USER_CONFIGURATION;
    cmd.subId = USER_CONFIGURATION_SUB_CMD_DISPLAY_SHUTTER_IMAGE;
    cmd.rw = I2C_WRITE;
    cmd.data = reg;

    if(SendDataPkt(cmd, rsp, "DISPLAY_SHUTTER_IMAGE") == false)
        return false;
    return true;
}

bool Viewer::GetBrightnessValue()
{
    cmd.mainId = USER_CONFIGURATION;
    cmd.subId = USER_CONFIGURATION_SUB_CMD_BRIGHTNESS;
    cmd.rw = I2C_RAED;
    //cmd.data = 0x00; // Not Used

    if(SendDataPkt(cmd, rsp, "BRIGHTNESS") == false)
        return false;

    ApplyIQValue(ui->horizontalSlider_brightness, ui->lineEdit_brightness, rsp.data);

    return true;
}

bool Viewer::SetBrightnessValue(uint8_t value)
{
    cmd.mainId = USER_CONFIGURATION;
    cmd.subId = USER_CONFIGURATION_SUB_CMD_BRIGHTNESS;
    cmd.rw = I2C_WRITE;
    cmd.data = value;

    if(SendDataPkt(cmd, rsp, "BRIGHTNESS") == false)
        return false;

    return true;
}

bool Viewer::GetContrastValue()
{
    uint8_t subId = USER_CONFIGURATION_SUB_CMD_CONTRAST;
    uint8_t contrastValue;
    uint8_t adaptiveThresholdEn = 0;
    if(tphe_mode == 1) // TPHE
    {
        ui->horizontalSlider_contrast->setMinimum(0);
        ui->horizontalSlider_contrast->setMaximum(255);
    }
    else if(tphe_mode == 2) // TPHE3_CLIP (구 FAST TPHE)
    {
        subId = USER_CONFIGURATION_SUB_CMD_CONTRAST_TPHE3_CLIP;
        ui->horizontalSlider_contrast->setMinimum(0);
        ui->horizontalSlider_contrast->setMaximum(40);
    }
    else if(tphe_mode == 3) // TPHE3
    {
        subId = USER_CONFIGURATION_SUB_CMD_CONTRAST_TPHE3;
        ui->horizontalSlider_contrast->setMinimum(0);
        ui->horizontalSlider_contrast->setMaximum(40);
    }
    else
    {
        /* nothing to do */
    }
    cmd.mainId = USER_CONFIGURATION;
    cmd.subId = subId;
    cmd.rw = I2C_RAED;
    //cmd.data = 0x00; // Not Used

    if(SendDataPkt(cmd, rsp, "CONTRAST") == false)
        return false;

    if(tphe_mode == 3) // TPHE3
    {
        contrastValue = rsp.data >> 1 & 0x7F;
        adaptiveThresholdEn = rsp.data & 0x01;
    }
    else
    {
        contrastValue = rsp.data;
    }

    ApplyIQValue(ui->horizontalSlider_contrast, ui->lineEdit_contrast, contrastValue);
    ui->radioButton_adpativeThrEn->setChecked(adaptiveThresholdEn);

    return true;
}

bool Viewer::SetContrastValue(uint8_t value)
{
    uint8_t subId = USER_CONFIGURATION_SUB_CMD_CONTRAST;
    uint8_t adaptiveThresholdEn;

    if(tphe_mode == 2) // TPHE3_CLIP (구 FAST TPHE)
    {
        subId = USER_CONFIGURATION_SUB_CMD_CONTRAST_TPHE3_CLIP;
        cmd.data = value;
    }
    else if(tphe_mode == 3) // TPHE3
    {
        subId = USER_CONFIGURATION_SUB_CMD_CONTRAST_TPHE3;
        adaptiveThresholdEn = ui->radioButton_adpativeThrEn->isChecked();
        cmd.data = value << 1 | adaptiveThresholdEn;
    }
    else
    {
        /* nothing to do */
        cmd.data = value;
    }

    cmd.mainId = USER_CONFIGURATION;
    cmd.subId = subId;
    cmd.rw = I2C_WRITE;
    //cmd.data = value;

    if(SendDataPkt(cmd, rsp, "CONTRAST") == false)
        return false;    

    return true;
}

bool Viewer::GetEdgeStrengthValue()
{
    int edgeStrength;
    int edgeMode;

    cmd.mainId = USER_CONFIGURATION;
    cmd.subId = USER_CONFIGURATION_SUB_CMD_EDGE_STRENGTH;
    cmd.rw = I2C_RAED;
    //cmd.data = 0x00; // Not Used

    if(SendDataPkt(cmd, rsp, "EDGE_STRENGTH") == false)
        return false;

    edgeStrength = (rsp.data >> 3) & 0x1F;
    edgeMode = (rsp.data >> 0) & 0x07;

    ApplyIQValue(ui->horizontalSlider_edgeStrength, ui->lineEdit_edgeStrength, edgeStrength);

    ui->comboBox_EdgeMode->setCurrentIndex(edgeMode);
    return true;
}

bool Viewer::SetEdgeStrengthValue(uint8_t value)
{
    int data;

    data = value << 3;
    data |= ui->comboBox_EdgeMode->currentIndex();
    cmd.mainId = USER_CONFIGURATION;
    cmd.subId = USER_CONFIGURATION_SUB_CMD_EDGE_STRENGTH;
    cmd.rw = I2C_WRITE;
    cmd.data = data;

    if(SendDataPkt(cmd, rsp, "EDGE_STRENGTH") == false)
        return false;

    return true;
}

void Viewer::ApplyIQValue(QSlider* slider, QLineEdit* linedit, int v)
{
    v = std::clamp(v, slider->minimum(), slider->maximum());
    QSignalBlocker b1(linedit);
    QSignalBlocker b2(slider);
    linedit->setText(QString::number(v));
    slider->setValue(v);
}

void Viewer::on_pushButton_contrast_write_clicked()
{
    bool ok;
    int value;
    value = ui->lineEdit_contrast->text().toInt(&ok, 10);
    SetContrastValue(value);
}

void Viewer::on_horizontalSlider_contrast_valueChanged(int value)
{
    SetContrastValue(value);
    ApplyIQValue(ui->horizontalSlider_contrast, ui->lineEdit_contrast, value);
}

void Viewer::on_lineEdit_contrast_textChanged(const QString &arg1)
{
    int value;

    value = arg1.toUInt();
    if(value >= ui->horizontalSlider_contrast->minimum() && value <= ui->horizontalSlider_contrast->maximum())
    {
        ApplyIQValue(ui->horizontalSlider_contrast, ui->lineEdit_contrast, value);
        ui->pushButton_contrast_write->setEnabled(1);
    }
    else
    {
        ui->pushButton_contrast_write->setEnabled(0);
    }
}

void Viewer::on_pushButton_contrast_read_clicked()
{
    GetContrastValue();
}

void UpDownFunctionDec(uint8_t updown, QSlider* slider, QLineEdit* lineedit)
{
    bool ok = false;
    QString v = lineedit->text();
    int vv = v.toInt(&ok, 10);

    if(updown == UP)
    {
        vv++;
    }
    else
    {
        vv--;
    }

    if(slider->maximum() < vv || slider->minimum() > vv)
        return;

    lineedit->setText(QString::number(vv, 10).toUpper());
}

void Viewer::on_pushButton_contrast_up_clicked()
{
    UpDownFunctionDec(UP, ui->horizontalSlider_contrast, ui->lineEdit_contrast);
}

void Viewer::on_pushButton_contrast_down_clicked()
{
    UpDownFunctionDec(DOWN, ui->horizontalSlider_contrast, ui->lineEdit_contrast);
}

void Viewer::on_horizontalSlider_brightness_valueChanged(int value)
{
    SetBrightnessValue(value);
    ApplyIQValue(ui->horizontalSlider_brightness, ui->lineEdit_brightness, value);
}

void Viewer::on_lineEdit_brightness_textChanged(const QString &arg1)
{
    int value;

    value = arg1.toUInt();
    if(value >= ui->horizontalSlider_brightness->minimum() && value <= ui->horizontalSlider_brightness->maximum())
    {
        ApplyIQValue(ui->horizontalSlider_brightness, ui->lineEdit_brightness, value);
        ui->pushButton_brightness_write->setEnabled(1);
    }
    else
    {
        ui->pushButton_brightness_write->setEnabled(0);
    }
}

void Viewer::on_pushButton_brightness_up_clicked()
{
    UpDownFunctionDec(UP, ui->horizontalSlider_brightness, ui->lineEdit_brightness);
}

void Viewer::on_pushButton_brightness_down_clicked()
{
    UpDownFunctionDec(DOWN, ui->horizontalSlider_brightness, ui->lineEdit_brightness);
}

void Viewer::on_pushButton_brightness_read_clicked()
{
    GetBrightnessValue();
}

void Viewer::on_pushButton_brightness_write_clicked()
{
    bool ok;
    int value;
    value = ui->lineEdit_brightness->text().toInt(&ok, 10);
    SetBrightnessValue(value);
}

void Viewer::on_horizontalSlider_edgeStrength_valueChanged(int value)
{
    SetEdgeStrengthValue(value);
    ApplyIQValue(ui->horizontalSlider_edgeStrength, ui->lineEdit_edgeStrength, value);
}

void Viewer::on_lineEdit_edgeStrength_textChanged(const QString &arg1)
{
    int value;

    value = arg1.toUInt();
    if(value >= ui->horizontalSlider_edgeStrength->minimum() && value <= ui->horizontalSlider_edgeStrength->maximum())
    {
        ApplyIQValue(ui->horizontalSlider_edgeStrength, ui->lineEdit_edgeStrength, value);
        ui->pushButton_edgeStrength_write->setEnabled(1);
    }
    else
    {
        ui->pushButton_edgeStrength_write->setEnabled(0);
    }
}

void Viewer::on_pushButton_edgeStrength_up_clicked()
{
    UpDownFunctionDec(UP, ui->horizontalSlider_edgeStrength, ui->lineEdit_edgeStrength);
}

void Viewer::on_pushButton_edgeStrength_down_clicked()
{
    UpDownFunctionDec(DOWN, ui->horizontalSlider_edgeStrength, ui->lineEdit_edgeStrength);
}

void Viewer::on_pushButton_edgeStrength_read_clicked()
{
    GetEdgeStrengthValue();
}

void Viewer::on_pushButton_edgeStrength_write_clicked()
{
    bool ok;
    int value;
    value = ui->lineEdit_edgeStrength->text().toInt(&ok, 10);
    SetEdgeStrengthValue(value);
}

void Viewer::on_btn_ConfigureSave_clicked()
{
    cmd.mainId = OPERATION_CONTROL;
    cmd.subId = OPERATION_CONTROL_SUB_CMD_USER_CFG_SAVE;
    cmd.rw = I2C_WRITE;
    cmd.data = 0x01; // Not Used

    SendDataPkt(cmd, rsp, "저장");
}

void Viewer::ApplyZoomValue(QSlider* slider, QLineEdit* linedit, int v)
{
    v = std::clamp(v, slider->minimum(), slider->maximum());
    QSignalBlocker b1(linedit);
    QSignalBlocker b2(slider);
    linedit->setText(QString::number(v));
    slider->setValue(v);
}

bool Viewer::SetZoomFactor(uint8_t value)
{
    cmd.mainId = OPERATION_CONTROL;
    cmd.subId = OPERATION_CONTROL_SUB_CMD_ZOOM;
    cmd.rw = I2C_WRITE;
    cmd.data = value;

    if(SendDataPkt(cmd, rsp, "ZOOM") == false)
        return false;

    return true;
}

bool Viewer::GetZoomFactor()
{
    cmd.mainId = OPERATION_CONTROL;
    cmd.subId = OPERATION_CONTROL_SUB_CMD_ZOOM;
    cmd.rw = I2C_RAED;
    //cmd.data = 0x00; // Not Used

    if(SendDataPkt(cmd, rsp, "ZOOM") == false)
        return false;

    ApplyZoomValue(ui->horizontalSlider_zoom, ui->lineEdit_zoom, rsp.data);

    return true;
}

void Viewer::on_horizontalSlider_zoom_valueChanged(int value)
{
    SetZoomFactor(value);
    ApplyZoomValue(ui->horizontalSlider_zoom, ui->lineEdit_zoom, value);
}

void Viewer::on_lineEdit_zoom_textChanged(const QString &arg1)
{
    int value;
    bool ok;

    value = arg1.toInt(&ok, 10);

    if(value >= ui->horizontalSlider_zoom->minimum() && value <= ui->horizontalSlider_zoom->maximum())
    {
        ApplyZoomValue(ui->horizontalSlider_zoom, ui->lineEdit_zoom, value);
        ui->pushButton_zoomWrite->setEnabled(1);
    }
    else
    {
        ui->pushButton_zoomWrite->setEnabled(0);
    }
}

void Viewer::on_pushButton_zoomUp_clicked()
{
    UpDownFunctionDec(UP, ui->horizontalSlider_zoom, ui->lineEdit_zoom);
}

void Viewer::on_pushButton_zoomDown_clicked()
{
    UpDownFunctionDec(DOWN, ui->horizontalSlider_zoom, ui->lineEdit_zoom);
}

void Viewer::on_pushButton_zoomRead_clicked()
{
    GetZoomFactor();
}

void Viewer::on_pushButton_zoomWrite_clicked()
{
    bool ok;
    int value;

    value = ui->lineEdit_zoom->text().toInt(&ok, 10);

    SetZoomFactor(value);
}

bool Viewer::SetClaheGrid(uint8_t value)
{
    cmd.mainId = USER_CONFIGURATION;
    cmd.subId = USER_CONFIGURATION_SUB_CMD_CLAHE_GRID;
    cmd.rw = I2C_WRITE;
    cmd.data = value;

    if(SendDataPkt(cmd, rsp, "CLAHE_GRID") == false)
        return false;

    return true;
}

bool Viewer::GetClaheGrid()
{
    cmd.mainId = USER_CONFIGURATION;
    cmd.subId = USER_CONFIGURATION_SUB_CMD_CLAHE_GRID;
    cmd.rw = I2C_RAED;
    //cmd.data = 0x00; // Not Used

    if(SendDataPkt(cmd, rsp, "CLAHE_GRID") == false)
        return false;

    ui->comboBox_CLAHE_Grid->setCurrentIndex(rsp.data - 1);

    return true;
}

bool Viewer::SetClaheThreshold(uint8_t value)
{
    cmd.mainId = USER_CONFIGURATION;
    cmd.subId = USER_CONFIGURATION_SUB_CMD_CLAHE_THRESHOLD;
    cmd.rw = I2C_WRITE;
    cmd.data = value;

    if(SendDataPkt(cmd, rsp, "CLAHE_GRID") == false)
        return false;

    return true;
}

bool Viewer::GetClaheThreshold()
{
    cmd.mainId = USER_CONFIGURATION;
    cmd.subId = USER_CONFIGURATION_SUB_CMD_CLAHE_THRESHOLD;
    cmd.rw = I2C_RAED;
    //cmd.data = 0x00; // Not Used

    if(SendDataPkt(cmd, rsp, "CLAHE_THRE") == false)
        return false;

    ApplyIQValue(ui->horizontalSlider_CLAHE_Thr, ui->lineEdit_CLAHE_Thr, rsp.data);

    return true;
}

void Viewer::on_pushButton_CLAHE_Write_clicked()
{
    uint8_t claheGrid;
    uint8_t claheThreshold;
    bool ok;

    claheGrid = ui->comboBox_CLAHE_Grid->currentIndex() + 1;
    claheThreshold = ui->lineEdit_CLAHE_Thr->text().toInt(&ok, 10);

    SetClaheGrid(claheGrid);
    SetClaheThreshold(claheThreshold);
}

void Viewer::on_horizontalSlider_CLAHE_Thr_valueChanged(int value)
{
    SetClaheThreshold(value);
    ApplyIQValue(ui->horizontalSlider_CLAHE_Thr, ui->lineEdit_CLAHE_Thr, value);
}

void Viewer::on_lineEdit_CLAHE_Thr_textChanged(const QString &arg1)
{
    int value;

    value = arg1.toUInt();
    if(value >= ui->horizontalSlider_CLAHE_Thr->minimum() && value <= ui->horizontalSlider_CLAHE_Thr->maximum())
    {
        ApplyIQValue(ui->horizontalSlider_CLAHE_Thr, ui->lineEdit_CLAHE_Thr, value);
        ui->pushButton_CLAHE_Write->setEnabled(1);
    }
    else
    {
        ui->pushButton_CLAHE_Write->setEnabled(0);
    }
}

void Viewer::on_pushButton_CLAHE_Thr_Up_clicked()
{
    UpDownFunctionDec(UP, ui->horizontalSlider_CLAHE_Thr, ui->lineEdit_CLAHE_Thr);
}

void Viewer::on_pushButton_CLAHE_Thr_Down_clicked()
{
    UpDownFunctionDec(DOWN, ui->horizontalSlider_CLAHE_Thr, ui->lineEdit_CLAHE_Thr);
}

void Viewer::on_pushButton_CLAHE_Read_clicked()
{
    GetClaheGrid();
    GetClaheThreshold();
}

void Viewer::onNrCtrlComboChanged()
{
    if(m_updatingDeviceCombo) return;
    SetNrCtrl();
}

void Viewer::onPostNrScaleComboChanged()
{
    if(m_updatingDeviceCombo) return;
    SetPostNrScale(ui->comboBox_postNrScale->currentIndex());
}

void Viewer::postNrMenuCtrl(uint8_t pgf_alpha_en, uint8_t scale_en, uint8_t mixRate_en)
{
    ui->horizontalSlider_pgf_alpha->setEnabled(pgf_alpha_en);
    ui->lineEdit_pgf_alpha->setEnabled(pgf_alpha_en);
    ui->pushButton_pgf_alpha_up->setEnabled(pgf_alpha_en);
    ui->pushButton_pgf_alpha_down->setEnabled(pgf_alpha_en);

    ui->comboBox_postNrScale->setEnabled(scale_en);

    ui->horizontalSlider_postNrMixRate->setEnabled(mixRate_en);
    ui->lineEdit_postNr_MixRate->setEnabled(mixRate_en);
    ui->pushButton_postNr_MixRate_up->setEnabled(mixRate_en);
    ui->pushButton_postNr_MixRate_down->setEnabled(mixRate_en);
}

bool Viewer::GetNrCtrl()
{
    cmd.mainId = USER_CONFIGURATION;
    cmd.subId = USER_CONFIGURATION_SUB_CMD_NR_CTRL;
    cmd.rw = I2C_RAED;

    if(SendDataPkt(cmd, rsp, "NR_CTRL") == false)
        return false;

    uint8_t nrMode = rsp.data & 0x07;
    uint8_t nrThreshold = 2 - ((rsp.data >> 3) & 3);//((rsp.data & 0x0C) >> 3);

    m_updatingDeviceCombo = true;
    ui->comboBox_NrMode->setCurrentIndex(nrMode);
    ui->comboBox_NR_Threshold->setCurrentIndex(nrThreshold);
    m_updatingDeviceCombo = false;

    if(nrMode == NR_MODE_SELECTIVE)
    {
        ui->comboBox_NR_Threshold->setEnabled(1);
        ui->comboBox_EdgeMode->setEnabled(1);
        ui->groupBox_Pseudo->setEnabled(0);
    }
    else if(nrMode == NR_MODE_DDE)
    {
        ui->comboBox_NR_Threshold->setEnabled(0);
        ui->comboBox_EdgeMode->setEnabled(0);
        ui->groupBox_Pseudo->setEnabled(1);
        postNrMenuCtrl(true, false, false);
    }
    else if(nrMode == NR_MODE_FAST_GUIDED)
    {
        ui->comboBox_NR_Threshold->setEnabled(0);
        ui->comboBox_EdgeMode->setEnabled(1);
        ui->groupBox_Pseudo->setEnabled(1);
        postNrMenuCtrl(false, true, false);
    }
    else if(nrMode == NR_MODE_PSEUDO_GUIDED)
    {
        ui->comboBox_NR_Threshold->setEnabled(0);
        ui->comboBox_EdgeMode->setEnabled(1);
        ui->groupBox_Pseudo->setEnabled(1);
        postNrMenuCtrl(true, false, false);
    }
    else if(nrMode == NR_MODE_DDE2) // NR_MODE_DDE2
    {
        ui->comboBox_NR_Threshold->setEnabled(0);
        ui->comboBox_EdgeMode->setEnabled(1);
        ui->groupBox_Pseudo->setEnabled(1);
        postNrMenuCtrl(false, true, true);
    }
    else if(nrMode == NR_MODE_DDE3)
    {
        ui->comboBox_NR_Threshold->setEnabled(1);
        ui->comboBox_EdgeMode->setEnabled(1);
        ui->groupBox_Pseudo->setEnabled(1);
        postNrMenuCtrl(true, true, true);
    }
    else if(nrMode == NR_MODE_FAST_GUIDED2)
    {
        ui->comboBox_NR_Threshold->setEnabled(1);
        ui->comboBox_EdgeMode->setEnabled(1);
        ui->groupBox_Pseudo->setEnabled(1);
        postNrMenuCtrl(true, true, true);
    }

    return true;
}

bool Viewer::SetNrCtrl()
{
    uint8_t nrMode = ui->comboBox_NrMode->currentIndex();
    uint8_t nrThreshold = 2- ui->comboBox_NR_Threshold->currentIndex();
    quint8 reg = nrMode | (nrThreshold << 3);

    cmd.mainId = USER_CONFIGURATION;
    cmd.subId = USER_CONFIGURATION_SUB_CMD_NR_CTRL;
    cmd.rw = I2C_WRITE;
    cmd.data = reg;

    if(SendDataPkt(cmd, rsp, "NR_CTRL") == false)
        return false;

    if(nrMode == NR_MODE_SELECTIVE)
    {
        ui->comboBox_NR_Threshold->setEnabled(1);
        ui->comboBox_EdgeMode->setEnabled(1);
        ui->groupBox_Pseudo->setEnabled(0);
    }
    else if(nrMode == NR_MODE_DDE)
    {
        ui->comboBox_NR_Threshold->setEnabled(0);
        ui->comboBox_EdgeMode->setEnabled(0);
        ui->groupBox_Pseudo->setEnabled(1);
        postNrMenuCtrl(true, false, false);
    }
    else if(nrMode == NR_MODE_FAST_GUIDED)
    {
        ui->comboBox_NR_Threshold->setEnabled(0);
        ui->comboBox_EdgeMode->setEnabled(1);
        ui->groupBox_Pseudo->setEnabled(1);
        postNrMenuCtrl(false, true, false);
    }
    else if(nrMode == NR_MODE_PSEUDO_GUIDED)
    {
        ui->comboBox_NR_Threshold->setEnabled(0);
        ui->comboBox_EdgeMode->setEnabled(1);
        ui->groupBox_Pseudo->setEnabled(1);
        postNrMenuCtrl(true, false, false);
    }
    else if(nrMode == NR_MODE_DDE2)
    {
        ui->comboBox_NR_Threshold->setEnabled(0);
        ui->comboBox_EdgeMode->setEnabled(1);
        ui->groupBox_Pseudo->setEnabled(1);
        postNrMenuCtrl(false, true, true);
    }
    else if(nrMode == NR_MODE_DDE3)
    {
        ui->comboBox_NR_Threshold->setEnabled(1);
        ui->comboBox_EdgeMode->setEnabled(1);
        ui->groupBox_Pseudo->setEnabled(1);
        postNrMenuCtrl(true, true, true);
    }
    else if(nrMode == NR_MODE_FAST_GUIDED2)
    {
        ui->comboBox_NR_Threshold->setEnabled(1);
        ui->comboBox_EdgeMode->setEnabled(1);
        ui->groupBox_Pseudo->setEnabled(1);
        postNrMenuCtrl(true, true, true);
    }
    return true;
}

void Viewer::on_horizontalSlider_pgf_eps_valueChanged(int value)
{
    SetPgfEps(value);
    ApplyPostNrValue(ui->horizontalSlider_pgf_eps, ui->lineEdit_pgf_eps, value);
}

void Viewer::on_horizontalSlider_pgf_alpha_valueChanged(int value)
{
    SetPgfAlpha(value);
    ApplyPostNrValue(ui->horizontalSlider_pgf_alpha, ui->lineEdit_pgf_alpha, value);
}

bool Viewer::GetPgfEps()
{
    cmd.mainId = USER_CONFIGURATION;
    cmd.subId = USER_CONFIGURATION_SUB_CMD_PGF_EPS;
    cmd.rw = I2C_RAED;
    //cmd.data = 0x00; // Not Used

    if(SendDataPkt(cmd, rsp, "PGF_EPS") == false)
        return false;

    ApplyPostNrValue(ui->horizontalSlider_pgf_eps, ui->lineEdit_pgf_eps, rsp.data);

    return true;
}

bool Viewer::SetPgfEps(uint8_t value)
{
    cmd.mainId = USER_CONFIGURATION;
    cmd.subId = USER_CONFIGURATION_SUB_CMD_PGF_EPS;
    cmd.rw = I2C_WRITE;
    cmd.data = value;

    if(SendDataPkt(cmd, rsp, "PGF_EPS") == false)
        return false;

    return true;
}

bool Viewer::GetPgfAlpha()
{
    cmd.mainId = USER_CONFIGURATION;
    cmd.subId = USER_CONFIGURATION_SUB_CMD_PGF_ALPHA;
    cmd.rw = I2C_RAED;
    //cmd.data = 0x00; // Not Used

    if(SendDataPkt(cmd, rsp, "PGF_ALPHA") == false)
        return false;

    ApplyPostNrValue(ui->horizontalSlider_pgf_alpha, ui->lineEdit_pgf_alpha, rsp.data);

    return true;
}

bool Viewer::SetPgfAlpha(uint8_t value)
{
    cmd.mainId = USER_CONFIGURATION;
    cmd.subId = USER_CONFIGURATION_SUB_CMD_PGF_ALPHA;
    cmd.rw = I2C_WRITE;
    cmd.data = value;

    if(SendDataPkt(cmd, rsp, "PGF_EPS") == false)
        return false;

    return true;
}

void Viewer::ApplyPostNrValue(QSlider *slider, QLineEdit *linedit, int v)
{
    v = std::clamp(v, slider->minimum(), slider->maximum());
    QSignalBlocker b1(linedit);
    QSignalBlocker b2(slider);
    linedit->setText(QString::number(v));
    slider->setValue(v);
}

void Viewer::on_pushButton_pgf_eps_up_clicked()
{
    UpDownFunctionDec(UP, ui->horizontalSlider_pgf_eps, ui->lineEdit_pgf_eps);
}

void Viewer::on_pushButton_pgf_eps_down_clicked()
{
    UpDownFunctionDec(DOWN, ui->horizontalSlider_pgf_eps, ui->lineEdit_pgf_eps);
}

void Viewer::on_pushButton_pgf_alpha_up_clicked()
{
    UpDownFunctionDec(UP, ui->horizontalSlider_pgf_alpha, ui->lineEdit_pgf_alpha);
}

void Viewer::on_pushButton_pgf_alpha_down_clicked()
{
    UpDownFunctionDec(DOWN, ui->horizontalSlider_pgf_alpha, ui->lineEdit_pgf_alpha);
}

void Viewer::on_comboBox_InputSource_currentIndexChanged(int index)
{
    uint8_t inputSrcCtrl;
    uint8_t inputImgNum;

    inputImgNum = ui->comboBox_TestImage->currentIndex();
    inputSrcCtrl = (inputImgNum << 2) | index;

    cmd.mainId = DEBUG;
    cmd.subId = DEBUG_SUB_CMD_SOC_INPUT_SRC_CTRL;
    cmd.rw = I2C_WRITE;
    cmd.data = inputSrcCtrl;

    SendDataPkt(cmd, rsp, "INPUT_SRC_CTRL");
}

void Viewer::on_comboBox_TestImage_currentIndexChanged(int index)
{
    uint8_t inputSrcCtrl;
    uint8_t inputSrcMode;

    inputSrcMode = ui->comboBox_InputSource->currentIndex();
    inputSrcCtrl = (index << 2) | inputSrcMode;

    cmd.mainId = DEBUG;
    cmd.subId = DEBUG_SUB_CMD_SOC_INPUT_SRC_CTRL;
    cmd.rw = I2C_WRITE;
    cmd.data = inputSrcCtrl;

    SendDataPkt(cmd, rsp, "INPUT_SRC_CTRL");
}

bool Viewer::GetPostNrScale()
{
    cmd.mainId = USER_CONFIGURATION2;
    cmd.subId = USER_CONFIGURATION2_SUB_CMD_POSTNR_SCALE;
    cmd.rw = I2C_RAED;
    //cmd.data = 0x00; // Not Used

    if(SendDataPkt(cmd, rsp, "POST_NR_SCALE") == false)
        return false;

    //ApplyPostNrValue(ui->horizontalSlider_postNrScale, ui->lineEdit_postNr_Scale, rsp.data);
    m_updatingDeviceCombo = true;
    ui->comboBox_postNrScale->setCurrentIndex(rsp.data);
    m_updatingDeviceCombo = false;
    return true;
}

bool Viewer::SetPostNrScale(uint8_t value)
{
    cmd.mainId = USER_CONFIGURATION2;
    cmd.subId = USER_CONFIGURATION2_SUB_CMD_POSTNR_SCALE;
    cmd.rw = I2C_WRITE;
    cmd.data = value;

    if(SendDataPkt(cmd, rsp, "POST_NR_SCALE") == false)
        return false;

    return true;
}

bool Viewer::GetPostNrMixRate()
{
    cmd.mainId = USER_CONFIGURATION2;
    cmd.subId = USER_CONFIGURATION2_SUB_CMD_POSTNR_MIX_RATE;
    cmd.rw = I2C_RAED;
    //cmd.data = 0x00; // Not Used

    if(SendDataPkt(cmd, rsp, "POST_NR_MIX") == false)
        return false;

    ApplyPostNrValue(ui->horizontalSlider_postNrMixRate, ui->lineEdit_postNr_MixRate, rsp.data);

    return true;
}

bool Viewer::SetPostNrMixRate(uint8_t value)
{
    cmd.mainId = USER_CONFIGURATION2;
    cmd.subId = USER_CONFIGURATION2_SUB_CMD_POSTNR_MIX_RATE;
    cmd.rw = I2C_WRITE;
    cmd.data = value;

    if(SendDataPkt(cmd, rsp, "POST_NR_MIX") == false)
        return false;

    return true;
}

void Viewer::on_horizontalSlider_postNrMixRate_valueChanged(int value)
{
    SetPostNrMixRate(value);
    ApplyPostNrValue(ui->horizontalSlider_postNrMixRate, ui->lineEdit_postNr_MixRate, value);
}


void Viewer::on_pushButton_postNr_MixRate_up_clicked()
{
    UpDownFunctionDec(UP, ui->horizontalSlider_postNrMixRate, ui->lineEdit_postNr_MixRate);
}

void Viewer::on_pushButton_postNr_MixRate_down_clicked()
{
    UpDownFunctionDec(DOWN, ui->horizontalSlider_postNrMixRate, ui->lineEdit_postNr_MixRate);
}

void Viewer::on_radioButton_adpativeThrEn_clicked(bool checked)
{
    uint8_t tphe3Contrast;
    bool ok;
    uint8_t value;

    tphe3Contrast = ui->lineEdit_contrast->text().toInt(&ok, 10);
    value = tphe3Contrast; // << 1 | checked;
    SetContrastValue(value);
}

bool Viewer::GetEdgeThresHold()
{
    cmd.mainId = USER_CONFIGURATION2;
    cmd.subId = USER_CONFIGURATION2_SUB_CMD_EDGE_THRESHOLD;
    cmd.rw = I2C_RAED;
    cmd.data = 0x00;

    if(SendDataPkt(cmd, rsp, "EDGE_THRESHOLD") == false)
        return false;

    ApplyIQValue(ui->horizontalSlider_edge_threshold, ui->lineEdit_edge_threshold, rsp.data);

    return true;
}

void Viewer::on_btn_edge_threshold_read_clicked()
{
    GetEdgeThresHold();
}

bool Viewer::GetNoiseThresHold()
{
    cmd.mainId = USER_CONFIGURATION2;
    cmd.subId = USER_CONFIGURATION2_SUB_CMD_NOISE_THRESHOLD;
    cmd.rw = I2C_RAED;
    cmd.data = 0x00;

    if(SendDataPkt(cmd, rsp, "NOISE_THRESHOLD") == false)
        return false;

    ApplyIQValue(ui->horizontalSlider_noise_threshold, ui->lineEdit_noise_threshold, rsp.data);
    return true;
}

void Viewer::on_btn_noise_threshold_read_clicked()
{
    GetNoiseThresHold();
}

bool Viewer::SetEdgeThresHold(uint8_t value)
{
    cmd.mainId = USER_CONFIGURATION2;
    cmd.subId = USER_CONFIGURATION2_SUB_CMD_EDGE_THRESHOLD;
    cmd.rw = I2C_WRITE;
    cmd.data = value;

    if(SendDataPkt(cmd, rsp, "EDGE_THRESHOLD") == false)
        return false;

    return true;
}

void Viewer::on_btn_edge_threshold_write_clicked()
{
    bool ok;
    uint8_t value;
    value = ui->lineEdit_edge_threshold->text().toInt(&ok, 10);
    SetEdgeThresHold(value);
}

bool Viewer::SetNoiseThresHold(uint8_t value)
{
    cmd.mainId = USER_CONFIGURATION2;
    cmd.subId = USER_CONFIGURATION2_SUB_CMD_NOISE_THRESHOLD;
    cmd.rw = I2C_WRITE;
    cmd.data = value;

    if(SendDataPkt(cmd, rsp, "NOISE_THRESHOLD") == false)
        return false;

    return true;
}

void Viewer::on_btn_noise_threshold_write_clicked()
{
    bool ok;
    uint8_t value;
    value = ui->lineEdit_noise_threshold->text().toInt(&ok, 10);
    SetNoiseThresHold(value);
}

void Viewer::on_horizontalSlider_edge_threshold_valueChanged(int value)
{
    SetEdgeThresHold(value);
    ApplyIQValue(ui->horizontalSlider_edge_threshold, ui->lineEdit_edge_threshold, value);
}

void Viewer::on_lineEdit_edge_threshold_textChanged(const QString &arg1)
{
    int value;

    value = arg1.toUInt();
    if(value >= ui->horizontalSlider_edge_threshold->minimum() && value <= ui->horizontalSlider_edge_threshold->maximum())
    {
        ApplyIQValue(ui->horizontalSlider_edge_threshold, ui->lineEdit_edge_threshold, value);
        ui->btn_edge_threshold_write->setEnabled(1);
    }
    else
    {
        ui->btn_edge_threshold_write->setEnabled(0);
    }
}

void Viewer::on_pushButton_edge_threshold_up_clicked()
{
    UpDownFunctionDec(UP, ui->horizontalSlider_edge_threshold, ui->lineEdit_edge_threshold);
}

void Viewer::on_pushButton_edge_threshold_down_clicked()
{
    UpDownFunctionDec(DOWN, ui->horizontalSlider_edge_threshold, ui->lineEdit_edge_threshold);
}

void Viewer::on_horizontalSlider_noise_threshold_valueChanged(int value)
{
    SetNoiseThresHold(value);
    ApplyIQValue(ui->horizontalSlider_noise_threshold, ui->lineEdit_noise_threshold, value);
}

void Viewer::on_lineEdit_noise_threshold_textChanged(const QString &arg1)
{
    int value;

    value = arg1.toUInt();
    if(value >= ui->horizontalSlider_noise_threshold->minimum() && value <= ui->horizontalSlider_noise_threshold->maximum())
    {
        ApplyIQValue(ui->horizontalSlider_noise_threshold, ui->lineEdit_noise_threshold, value);
        ui->btn_noise_threshold_write->setEnabled(1);
    }
    else
    {
        ui->btn_noise_threshold_write->setEnabled(0);
    }
}

void Viewer::on_pushButton_noise_threshold_up_clicked()
{
    UpDownFunctionDec(UP, ui->horizontalSlider_noise_threshold, ui->lineEdit_noise_threshold);
}

void Viewer::on_pushButton_noise_threshold_down_clicked()
{
    UpDownFunctionDec(DOWN, ui->horizontalSlider_noise_threshold, ui->lineEdit_noise_threshold);
}

bool Viewer::GetTphe3ClipUp()
{
    cmd.mainId = USER_CONFIGURATION2;
    cmd.subId = USER_CONFIGURATION2_SUB_CMD_TPHE3_CLIP_UP;
    cmd.rw = I2C_RAED;
    cmd.data = 0x00;

    if(SendDataPkt(cmd, rsp, "TPHE3_CLIP_UP") == false)
        return false;

    ApplyIQValue(ui->horizontalSlider_tphe3_clip_up, ui->lineEdit_tphe3_clip_up, rsp.data);

    return true;
}

void Viewer::on_btn_tphe3_clip_up_read_clicked()
{
    GetTphe3ClipUp();
}

bool Viewer::GetTphe3ClipDown()
{
    cmd.mainId = USER_CONFIGURATION2;
    cmd.subId = USER_CONFIGURATION2_SUB_CMD_TPHE3_CLIP_DOWN;
    cmd.rw = I2C_RAED;
    cmd.data = 0x00;

    if(SendDataPkt(cmd, rsp, "TPHE3_CLIP_DOWN") == false)
        return false;

    ApplyIQValue(ui->horizontalSlider_tphe3_clip_down, ui->lineEdit_tphe3_clip_down, rsp.data);
    return true;
}

void Viewer::on_btn_tphe3_clip_down_read_clicked()
{
    GetTphe3ClipDown();
}

bool Viewer::SetTphe3ClipUp(uint8_t value)
{
    cmd.mainId = USER_CONFIGURATION2;
    cmd.subId = USER_CONFIGURATION2_SUB_CMD_TPHE3_CLIP_UP;
    cmd.rw = I2C_WRITE;
    cmd.data = value;

    if(SendDataPkt(cmd, rsp, "TPHE3_CLIP_UP") == false)
        return false;

    return true;
}

void Viewer::on_btn_tphe3_clip_up_write_clicked()
{
    bool ok;
    uint8_t value;
    value = ui->lineEdit_tphe3_clip_up->text().toInt(&ok, 10);
    SetTphe3ClipUp(value);
}

bool Viewer::SetTphe3ClipDown(uint8_t value)
{
    cmd.mainId = USER_CONFIGURATION2;
    cmd.subId = USER_CONFIGURATION2_SUB_CMD_TPHE3_CLIP_DOWN;
    cmd.rw = I2C_WRITE;
    cmd.data = value;

    if(SendDataPkt(cmd, rsp, "TPHE3_CLIP_DOWN") == false)
        return false;

    return true;
}

void Viewer::on_btn_tphe3_clip_down_write_clicked()
{
    bool ok;
    uint8_t value;
    value = ui->lineEdit_tphe3_clip_down->text().toInt(&ok, 10);
    SetTphe3ClipDown(value);
}

void Viewer::on_horizontalSlider_tphe3_clip_up_valueChanged(int value)
{
    SetTphe3ClipUp(value);
    ApplyIQValue(ui->horizontalSlider_tphe3_clip_up, ui->lineEdit_tphe3_clip_up, value);
}

void Viewer::on_lineEdit_tphe3_clip_up_textChanged(const QString &arg1)
{
    int value;

    value = arg1.toUInt();
    if(value >= ui->horizontalSlider_tphe3_clip_up->minimum() && value <= ui->horizontalSlider_tphe3_clip_up->maximum())
    {
        ApplyIQValue(ui->horizontalSlider_tphe3_clip_up, ui->lineEdit_tphe3_clip_up, value);
        ui->btn_tphe3_clip_up_write->setEnabled(1);
    }
    else
    {
        ui->btn_tphe3_clip_up_write->setEnabled(0);
    }
}

void Viewer::on_pushButton_tphe3_clip_up_up_clicked()
{
    UpDownFunctionDec(UP, ui->horizontalSlider_tphe3_clip_up, ui->lineEdit_tphe3_clip_up);
}

void Viewer::on_pushButton_tphe3_clip_up_down_clicked()
{
    UpDownFunctionDec(DOWN, ui->horizontalSlider_tphe3_clip_up, ui->lineEdit_tphe3_clip_up);
}

void Viewer::on_horizontalSlider_tphe3_clip_down_valueChanged(int value)
{
    SetTphe3ClipDown(value);
    ApplyIQValue(ui->horizontalSlider_tphe3_clip_down, ui->lineEdit_tphe3_clip_down, value);
}

void Viewer::on_lineEdit_tphe3_clip_down_textChanged(const QString &arg1)
{
    int value;

    value = arg1.toUInt();
    if(value >= ui->horizontalSlider_tphe3_clip_down->minimum() && value <= ui->horizontalSlider_tphe3_clip_down->maximum())
    {
        ApplyIQValue(ui->horizontalSlider_tphe3_clip_down, ui->lineEdit_tphe3_clip_down, value);
        ui->btn_tphe3_clip_down_write->setEnabled(1);
    }
    else
    {
        ui->btn_tphe3_clip_down_write->setEnabled(0);
    }
}

void Viewer::on_pushButton_tphe3_clip_down_up_clicked()
{
    UpDownFunctionDec(UP, ui->horizontalSlider_tphe3_clip_down, ui->lineEdit_tphe3_clip_down);
}

void Viewer::on_pushButton_tphe3_clip_down_down_clicked()
{
    UpDownFunctionDec(DOWN, ui->horizontalSlider_tphe3_clip_down, ui->lineEdit_tphe3_clip_down);
}
