#ifndef VIEWER_H
#define VIEWER_H

#include <QWidget>
#include <QDialog>
#include <QLabel>
#include <QSerialPort>
#include <QSerialPortInfo>
#include <QElapsedTimer>
#include <QMouseEvent>
#include <QVBoxLayout>
#include <QPixmap>
#include <QDebug>
#include <QCameraInfo>
#include <QMessageBox>
#include <QTimer>
#include <QFileDialog>
#include <QJsonDocument>
#include <QJsonObject>
#include <QLineEdit>
#include <QSlider>
#include <QDateTime>
#include <QtCharts/QChartView>
#include <QtCharts/QLineSeries>
#include <QtCharts/QValueAxis>

QT_CHARTS_USE_NAMESPACE

#include "Capture.h"
#include "Capturethread.h"
#include "Clserial.h"
#include "DataPacket.h"
#include "Histogram.h"
#include "Logdialog.h"

#include "stdlib.h"
#include <limits>

#define UP          1
#define DOWN        0

#define TEMP_RANGE 50

// ==============================
// ClickableLabel 정의 (영상)
// ==============================
class ClickableLabel : public QLabel {
    Q_OBJECT
public:
    explicit ClickableLabel(QWidget *parent = nullptr) : QLabel(parent) {}
    ~ClickableLabel() {}

signals:
    void clicked();

protected:
    void mousePressEvent(QMouseEvent *event) override {
        if (event->button() == Qt::LeftButton) emit clicked();
        QLabel::mousePressEvent(event);
    }
};

// ==============================
// Viewer 클래스
// ==============================
namespace Ui {
class Viewer;
}

class Viewer : public QWidget {
    Q_OBJECT

public:
    explicit Viewer(QWidget *parent = nullptr);
    ~Viewer();

    bool SendDataPkt(ClCmdPkt& cmd, ClRspPkt& rsp, QString str);
    void ErrorDisConnect();
    void applyBiasValue(QSlider* slider, QLineEdit* linedit, int v);

signals:
    void logMessage(const QString& line);   // ← Viewer에서 로그 발생시 보낼 신호
    void connectionStateChanged(bool connected, const QString& portName);

private slots:
    // === Serial 관련 ===
    void on_btnPortConnect_clicked();

    // === Camera 관련 ===
    void on_btnCamConnect_clicked();
    void onFrameReady(const QImage& frame);

    // === UI/Display 관련 ===
    void onVideoLabelClicked();

    void onCameraModeComboChanged();

    void onShutterModeComboChanged();

    void onShutterCycleTimeEditingFinished();

    void on_btn_ShutterCycle_Read_clicked();

    void on_btn_ShutterCycle_Write_clicked();

    void on_btn_CameraStart_clicked();

    void on_btn_CameraStop_clicked();

    void on_btn_shutterOpen_clicked();

    void on_btn_shutterClose_clicked();

    void onNucModeComboChanged();

    void onCemComboChanged();

    void on_btn_CINT_Read_clicked();

    void on_btn_CINT_Write_clicked();

    void on_btn_TINT_MSB_Read_clicked();

    void on_btn_TINT_MSB_Write_clicked();

    void on_btn_TINT_LSB_Read_clicked();

    void on_btn_TINT_LSB_Write_clicked();

    void on_btn_GSK_MSB_Read_clicked();

    void on_btn_GSK_MSB_Write_clicked();

    void on_btn_GSK_LSB_Read_clicked();

    void on_btn_GSK_LSB_Write_clicked();

    void on_btn_GFID_Read_clicked();

    void on_btn_GFID_Write_clicked();

    void on_btn_BIAS_Save_clicked();

    void on_btn_BIAS_Load_clicked();

    void on_btn_Make_UserConfig_clicked();

    void on_horizontalSlider_CINT_valueChanged(int value);

    void on_lineEdit_CINT_textChanged(const QString &arg1);

    void on_horizontalSlider_TINT_MSB_valueChanged(int value);

    void on_lineEdit_TINT_MSB_textChanged(const QString &arg1);

    void on_horizontalSlider_TINT_LSB_valueChanged(int value);

    void on_lineEdit_TINT_LSB_textChanged(const QString &arg1);

    void on_horizontalSlider_GSK_MSB_valueChanged(int value);

    void on_lineEdit_GSK_MSB_textChanged(const QString &arg1);

    void on_horizontalSlider_GSK_LSB_valueChanged(int value);

    void on_lineEdit_GSK_LSB_textChanged(const QString &arg1);

    void on_horizontalSlider_GFID_valueChanged(int value);

    void on_lineEdit_GFID_textChanged(const QString &arg1);

    void on_btn_CINT_countup_clicked();

    void on_btn_CINT_countdown_clicked();

    void on_btn_TINT_MSB_countup_clicked();

    void on_btn_TINT_MSB_countdown_clicked();

    void on_btn_TINT_LSB_countup_clicked();

    void on_btn_TINT_LSB_countdown_clicked();

    void on_btn_GSK_MSB_countup_clicked();

    void on_btn_GSK_MSB_countdown_clicked();

    void on_btn_GSK_LSB_countup_clicked();

    void on_btn_GSK_LSB_countdown_clicked();

    void on_btn_GFID_countup_clicked();

    void on_btn_GFID_countdown_clicked();

    void on_btn_OpenDebug_clicked();

    void on_btn_GetFFC_clicked();

    void on_btn_tecless_temp_range_clicked();

    void on_checkBox_Celsius_stateChanged(int arg1);

    void on_btn_histogramPopup_clicked();

    void on_btn_fpaChartPopup_clicked();

    void on_btn_socChartPopup_clicked();

    void on_btn_ReadAll_clicked();

    void on_btn_TDA3_RESET_clicked();

    void onColorMapComboChanged();

    void onDisplayShutterImageComboChanged();

    void on_pushButton_contrast_write_clicked();

    void on_horizontalSlider_contrast_valueChanged(int value);

    void on_lineEdit_contrast_textChanged(const QString &arg1);

    void on_pushButton_contrast_read_clicked();

    void on_pushButton_contrast_up_clicked();

    void on_pushButton_contrast_down_clicked();

    void on_horizontalSlider_edgeStrength_valueChanged(int value);

    void on_pushButton_brightness_up_clicked();

    void on_pushButton_brightness_down_clicked();

    void on_pushButton_brightness_read_clicked();

    void on_pushButton_brightness_write_clicked();

    void on_lineEdit_edgeStrength_textChanged(const QString &arg1);

    void on_pushButton_edgeStrength_up_clicked();

    void on_pushButton_edgeStrength_down_clicked();

    void on_pushButton_edgeStrength_read_clicked();

    void on_pushButton_edgeStrength_write_clicked();

    void on_horizontalSlider_brightness_valueChanged(int value);

    void on_lineEdit_brightness_textChanged(const QString &arg1);

    void on_btn_ConfigureSave_clicked();

    void on_horizontalSlider_zoom_valueChanged(int value);

    void on_lineEdit_zoom_textChanged(const QString &arg1);

    void on_pushButton_zoomUp_clicked();

    void on_pushButton_zoomDown_clicked();

    void on_pushButton_zoomRead_clicked();

    void on_pushButton_zoomWrite_clicked();

    void on_pushButton_CLAHE_Write_clicked();

    void on_horizontalSlider_CLAHE_Thr_valueChanged(int value);

    void on_lineEdit_CLAHE_Thr_textChanged(const QString &arg1);

    void on_pushButton_CLAHE_Thr_Up_clicked();

    void on_pushButton_CLAHE_Thr_Down_clicked();

    void on_pushButton_CLAHE_Read_clicked();

    void onNrCtrlComboChanged();

    void onPostNrScaleComboChanged();

    void on_horizontalSlider_pgf_eps_valueChanged(int value);

    void on_horizontalSlider_pgf_alpha_valueChanged(int value);

    void on_pushButton_pgf_eps_up_clicked();

    void on_pushButton_pgf_eps_down_clicked();

    void on_pushButton_pgf_alpha_up_clicked();

    void on_pushButton_pgf_alpha_down_clicked();

    void on_comboBox_InputSource_currentIndexChanged(int index);

    void on_comboBox_TestImage_currentIndexChanged(int index);

    void on_horizontalSlider_postNrMixRate_valueChanged(int value);

    void on_pushButton_postNr_MixRate_up_clicked();

    void on_pushButton_postNr_MixRate_down_clicked();

    void on_radioButton_adpativeThrEn_clicked(bool checked);

    void on_btn_edge_threshold_read_clicked();

    void on_btn_noise_threshold_read_clicked();

    void on_btn_edge_threshold_write_clicked();

    void on_btn_noise_threshold_write_clicked();

    void on_horizontalSlider_edge_threshold_valueChanged(int value);

    void on_lineEdit_edge_threshold_textChanged(const QString &arg1);

    void on_pushButton_edge_threshold_up_clicked();

    void on_pushButton_edge_threshold_down_clicked();

    void on_horizontalSlider_noise_threshold_valueChanged(int value);

    void on_lineEdit_noise_threshold_textChanged(const QString &arg1);

    void on_pushButton_noise_threshold_up_clicked();

    void on_pushButton_noise_threshold_down_clicked();

    void on_btn_tphe3_clip_up_read_clicked();

    void on_btn_tphe3_clip_down_read_clicked();

    void on_btn_tphe3_clip_up_write_clicked();

    void on_btn_tphe3_clip_down_write_clicked();

    void on_horizontalSlider_tphe3_clip_up_valueChanged(int value);

    void on_lineEdit_tphe3_clip_up_textChanged(const QString &arg1);

    void on_pushButton_tphe3_clip_up_up_clicked();

    void on_pushButton_tphe3_clip_up_down_clicked();

    void on_horizontalSlider_tphe3_clip_down_valueChanged(int value);

    void on_lineEdit_tphe3_clip_down_textChanged(const QString &arg1);

    void on_pushButton_tphe3_clip_down_up_clicked();

    void on_pushButton_tphe3_clip_down_down_clicked();

private:
    Ui::Viewer *ui;
    class Impl;
    Impl *m_cam;

    // Video 관련
    QDialog *m_videoPopup;
    QLabel *m_popupLabel;

    bool m_videoPopupOnlyUpdate = false; // m_videoPopup 체크

    // Serial
    CLSerial* m_serial = nullptr;
    ClCmdPkt cmd;
    ClRspPkt rsp;

    // Camera 그룹 콤보박스가 GetCameraMode()에 의해 프로그램적으로 설정되는 동안
    // currentIndexChanged로 인한 자동 Write 재귀 호출을 막기 위한 플래그
    bool m_updatingCameraCombo = false;
    // Shutter/NUC/CEM/ColorMap/DisplayShutterImage 콤보박스가 Get*()에 의해
    // 프로그램적으로 설정되는 동안 자동 Write 재귀 호출을 막기 위한 플래그
    bool m_updatingDeviceCombo = false;

    // FPS
    QElapsedTimer m_fpsTimer;
    bool m_showFps;
    int m_frameCount;

    // Capture Thread
    capturethread* m_capThread;

    // Setup helpers
    void setupVideoLabel();
    void setupConnections();
    void setupSerialPortList();
    void setupPortControlButtons();
    void setupCamPortList();
    void setupCamControlButtons();

    // FPS 계산
    void updateFps();



    QTimer *TEClessTempRangetimer = nullptr;

    void GetTEClessTempRange();

    // === 히스토그램 팝업 ===
    QDialog   *m_histPopup = nullptr;
    histogram *m_histPopupWidget = nullptr;

    // === 온도 그래프 팝업 (FPA / SOC) ===
    // 팝업이 닫혀있어도 background 로 값 자체는 누적하지 않고,
    // 팝업이 열려 있는 동안에만 timer 로 값을 읽어 그래프에 반영한다.
    struct TempChannel {
        QDialog     *popup   = nullptr;
        QChart      *chart   = nullptr;
        QLineSeries *series  = nullptr;
        QValueAxis  *axisX   = nullptr;
        QValueAxis  *axisY   = nullptr;
        QTimer      *timer   = nullptr;
        int xIndex = 0;
        int bandCenter = std::numeric_limits<int>::min(); // Y축 밴드 중심(TEMP_RANGE 단위), 미설정 시 최소값
    };
    static const int WINDOW_SAMPLES = 120; // 화면에 보일 최근 N초

    TempChannel m_fpaChannel;
    TempChannel m_socChannel;

    void openTempChartPopup(TempChannel& ch, const QString& title, void (Viewer::*getter)());
    void closeTempChartPopup(TempChannel& ch);
    void appendTempSample(TempChannel& ch, float v);

    void GetFPA_Temp();
    void GetTDA3_Temp();

    //save load
    QVector<QPair<QString, QLineEdit*>> fields() const;
    QJsonObject toJson() const;                         // lineEdit -> JSON
    bool fromJson(const QJsonObject&, QString* err);    // JSON -> lineEdit (검증 포함)

    void FirstConnectGetInfo();
    bool GetSerialNumber();
    bool GetCameraMode();
    bool GetShutterMode();
    bool GetShutterCycle();
    bool GetCameraControl();
    bool GetShutterControl();
    bool GetNucMode();
    bool GetCemMode();
    bool GetAllBias();
    bool GetTDA3Ver();
    bool GetFX3Ver();
    bool GetFFC();
    bool GetColorMap();
    bool GetDisplayShutterImage();
    bool GetNrCtrl();

    bool SetCameraMode();
    bool SetShutterMode();
    bool SetShutterCycle();
    bool SetCameraControl(uint8_t mode);
    bool SetShutterControl(uint8_t mode);
    bool SetNucMode();
    bool SetCemMode();
    bool SetColorMap();
    bool SetDisplayShutterImage();
    bool SetNrCtrl();

    bool FFCCheck();
    bool ShutterControlCheck();

    bool GetBiasSingle(uint8_t Sub, QSlider* slider, QLineEdit* lineedit);
    bool SetBuasSinble(uint8_t Sub, QLineEdit* lineedit);

    bool GetBrightnessValue();
    bool SetBrightnessValue(uint8_t value);
    bool GetContrastValue();
    bool SetContrastValue(uint8_t value);
    bool GetEdgeStrengthValue();
    bool SetEdgeStrengthValue(uint8_t value);
    void ApplyIQValue(QSlider* slider, QLineEdit* linedit, int v);
    void ApplyZoomValue(QSlider* slider, QLineEdit* linedit, int v);
    bool SetZoomFactor(uint8_t value);
    bool GetZoomFactor();
    bool SetClaheGrid(uint8_t value);
    bool GetClaheGrid();
    bool SetClaheThreshold(uint8_t value);
    bool GetClaheThreshold();
    void UpDownFunctionFloat(uint8_t updown, QSlider* slider, QLineEdit* lineedit);

    bool GetPgfEps();
    bool SetPgfEps(uint8_t value);
    bool GetPgfAlpha();
    bool SetPgfAlpha(uint8_t value);
    void ApplyPostNrValue(QSlider* slider, QLineEdit* linedit, int v);

    void postNrMenuCtrl(uint8_t pgf_alpha_en, uint8_t scale_en, uint8_t mixRate_en);
    bool GetPostNrScale();
    bool SetPostNrScale(uint8_t value);
    bool GetPostNrMixRate();
    bool SetPostNrMixRate(uint8_t value);
    bool GetEdgeThresHold();
    bool SetEdgeThresHold(uint8_t value);
    bool GetNoiseThresHold();
    bool SetNoiseThresHold(uint8_t value);
    bool GetTphe3ClipUp();
    bool SetTphe3ClipUp(uint8_t value);
    bool GetTphe3ClipDown();
    bool SetTphe3ClipDown(uint8_t value);

    uint8_t Cam_format_change = 0;

    void log(const QString& s);             // ← 타임스탬프 붙여 로그 보내기
    LogDialog* m_log = nullptr;             // ← 팝업 포인터

    uint8_t tphe_mode;
};

// ==============================
// Utility Function
// ==============================
QImage normalize16To8(const QImage &img16, bool autoStretch);

#endif // VIEWER_H
