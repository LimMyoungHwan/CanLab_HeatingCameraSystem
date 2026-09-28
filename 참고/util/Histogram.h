#ifndef HISTOGRAM_H
#define HISTOGRAM_H

#include <QWidget>
#include <QVector>
#include <QPainter>
#include <QPen>
#include <QFont>
#include <QDialog>
#include <QVBoxLayout>
#include <QMouseEvent>

#include <cmath>

class histogram : public QWidget {
    Q_OBJECT
public:
    explicit histogram(QWidget *parent = nullptr);

    void updateData(const QImage &img16);

signals:
    void clicked();   // ✅ Click 이벤트 시그널 추가

protected:
    void paintEvent(QPaintEvent *event) override;
    void mousePressEvent(QMouseEvent *event) override;

private:
    QVector<int> m_hist;
    int m_maxCount;
    int m_minVal, m_maxVal;
};


#endif // HISTOGRAM_H
