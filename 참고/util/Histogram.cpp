#include "Histogram.h"

histogram::histogram(QWidget *parent)
    : QWidget(parent),
      m_hist(16384, 0),   // 14bit bin 준비
      m_maxCount(1),
      m_minVal(16383),
      m_maxVal(0)
{
    setMinimumSize(400, 200);
}

void histogram::updateData(const QImage &img16) {
    if (img16.format() != QImage::Format_Grayscale16) return;

    int width  = img16.width();
    int height = img16.height();
    const uint16_t *data = reinterpret_cast<const uint16_t*>(img16.constBits());

    m_hist.fill(0, 16384); // 14bit bin (0~16383)
    m_minVal = 16383;
    m_maxVal = 0;

    for (int i = 0; i < width * height; i++) {
        uint16_t v = data[i] & 0x3FFF; // ✅ 상위 2비트 제외, 14bit만 사용
        m_hist[v]++;
        if (v < m_minVal) m_minVal = v;
        if (v > m_maxVal) m_maxVal = v;
    }

    m_maxCount = *std::max_element(m_hist.constBegin(), m_hist.constEnd());
    update();
}


void histogram::paintEvent(QPaintEvent *event)
{
    Q_UNUSED(event);
    QPainter painter(this);
    painter.fillRect(rect(), Qt::white);

    int w = width();
    int h = height();

    int marginLeft = 50;
    int marginBottom = 20;
    int plotW = w - marginLeft - 20;
    int plotH = h - marginBottom - 20;

    if (m_hist.isEmpty() || m_maxCount <= 0)
        return;

    // ======================
    // 1) 축
    // ======================
    painter.setPen(QPen(Qt::black, 2));
    // X축
    painter.drawLine(marginLeft, h - marginBottom, marginLeft + plotW, h - marginBottom);
    // Y축
    painter.drawLine(marginLeft, h - marginBottom, marginLeft, h - marginBottom - plotH);

    // ======================
    // 2) 막대 그래프 (0~16383 전체, 선형 스케일)
    // ======================
    painter.setPen(Qt::NoPen);
    painter.setBrush(QColor(50, 100, 200));

    int range = 16384;  // 항상 14bit 전체
    double binWidth = static_cast<double>(plotW) / range;

    for (int i = 0; i < range; i++) {
        int count = m_hist[i];
        if (count == 0) continue;

        int x = marginLeft + static_cast<int>(i * binWidth);
        // ✅ 선형 스케일
        int barHeight = static_cast<int>((double)count * plotH / m_maxCount);
        int y = h - marginBottom - barHeight;
        int bw = std::max(1, static_cast<int>(binWidth));

        painter.drawRect(x, y, bw, barHeight);
    }

    // ======================
    // 3) X축 라벨
    // ======================
    painter.setFont(QFont("Arial", 8));
    painter.setPen(Qt::black);

    int stepX = 2048;   // X축 8개 구간
    for (int val = 0; val <= 16383; val += stepX) {
        int x = marginLeft + (val * plotW / range);
        painter.drawText(x - 15, h - marginBottom + 15, QString::number(val));

        painter.setPen(QPen(Qt::lightGray, 1, Qt::DotLine));
        painter.drawLine(x, h - marginBottom, x, h - marginBottom - plotH);
        painter.setPen(Qt::black);
    }

    // 마지막 값(16383)
    int xLast = marginLeft + (16383 * plotW / range);
    painter.drawText(xLast - 15, h - marginBottom + 15, QString::number(16383));
    painter.setPen(QPen(Qt::lightGray, 1, Qt::DotLine));
    painter.drawLine(xLast, h - marginBottom, xLast, h - marginBottom - plotH);
    painter.setPen(Qt::black);

    // ======================
    // 4) Y축 라벨 (count)
    // ======================
    painter.setFont(QFont("Arial", 8));
    int yStepCount = m_maxCount / 5; // 5단계로 나눔
    if (yStepCount < 1) yStepCount = 1;

    for (int c = 0; c <= m_maxCount; c += yStepCount) {
        int y = h - marginBottom - (int)((double)c * plotH / m_maxCount);
        painter.drawText(marginLeft - 40, y + 5, QString::number(c)); // Y축 라벨
        painter.setPen(QPen(Qt::lightGray, 1, Qt::DotLine));
        painter.drawLine(marginLeft, y, marginLeft + plotW, y); // 수평 가이드
        painter.setPen(Qt::black);
    }

    // ======================
    // 5) 타이틀
    // ======================
    painter.setFont(QFont("Arial", 12, QFont::Bold));
    painter.drawText(w/2 - 60, 20, "HISTOGRAM");
}

void histogram::mousePressEvent(QMouseEvent *event)
{
    if (event->button() == Qt::LeftButton) {
        emit clicked();   // ✅ 시그널만 발생
    }
    QWidget::mousePressEvent(event);
}

