#ifndef DATAPACKET_H
#define DATAPACKET_H

#include <QWidget>
#include <QRegularExpression>

/* Main CMD */
#define DETECTOR                0x00
#define NUC                     0x10
#define USER_CONFIGURATION      0x20
#define OPERATION_CONTROL       0x30
#define USER_CONFIGURATION2     0x40
#define TEMP50       			0x50
#define TEMP60       			0x60
#define TEMP70       			0x70
#define TEMP80       			0x80
#define DEBUG       			0xF0

/* DETECTOR Sub CMD */
#define DETECTOR_SUB_CMD_SERIAL_NB_A     0x00
#define DETECTOR_SUB_CMD_SERIAL_NB_B     0x01
#define DETECTOR_SUB_CMD_SERIAL_NB_C     0x02
#define DETECTOR_SUB_CMD_SERIAL_NB_D     0x03
#define DETECTOR_SUB_CMD_GFID            0x04
#define DETECTOR_SUB_CMD_GSK_MSB         0x05
#define DETECTOR_SUB_CMD_GSK_LSB         0x06
#define DETECTOR_SUB_CMD_TINT_MSB        0x07
#define DETECTOR_SUB_CMD_TINT_LSB        0x08
#define DETECTOR_SUB_CMD_CINT            0x09
#define DETECTOR_SUB_CMD_FPA_TEMP_MSB    0x0A
#define DETECTOR_SUB_CMD_FPA_TEMP_LSB    0x0B

/* NUC Sub CMD */
#define NUC_SUB_CMD_NUC_MODE            0x00

/* USER_CONFIGURATION Sub CMD */
#define USER_CONFIGURATION_SUB_CMD_CAMERA                   0x00
#define USER_CONFIGURATION_SUB_CMD_SHUTTER                  0x01
#define USER_CONFIGURATION_SUB_CMD_SHUTTER_CYCLE            0x02
#define USER_CONFIGURATION_SUB_CMD_CEM                      0x03
#define USER_CONFIGURATION_SUB_CMD_COLOR_MAP                0x04
#define USER_CONFIGURATION_SUB_CMD_DISPLAY_SHUTTER_IMAGE    0x05
#define USER_CONFIGURATION_SUB_CMD_BRIGHTNESS               0x06
#define USER_CONFIGURATION_SUB_CMD_CONTRAST                 0x07
#define USER_CONFIGURATION_SUB_CMD_CONTRAST_TPHE3_CLIP      0x08
#define USER_CONFIGURATION_SUB_CMD_EDGE_STRENGTH            0x09
#define USER_CONFIGURATION_SUB_CMD_CONTRAST_TPHE3           0x0A
#define USER_CONFIGURATION_SUB_CMD_CLAHE_GRID               0x0B
#define USER_CONFIGURATION_SUB_CMD_CLAHE_THRESHOLD          0x0C
#define USER_CONFIGURATION_SUB_CMD_NR_CTRL                  0x0D
#define USER_CONFIGURATION_SUB_CMD_PGF_EPS                  0x0E
#define USER_CONFIGURATION_SUB_CMD_PGF_ALPHA                0x0F

/* OPERATION_CONTROL Sub CMD */
#define OPERATION_CONTROL_SUB_CMD_CAMERA             0x00
#define OPERATION_CONTROL_SUB_CMD_SHUTTER            0x01
#define OPERATION_CONTROL_SUB_CMD_USER_CFG_SAVE      0x02
#define OPERATION_CONTROL_SUB_CMD_FFC                0x03
#define OPERATION_CONTROL_SUB_CMD_ZOOM               0x04
#define OPERATION_CONTROL_SUB_CMD_TDA3_RESET         0xF0

/* USER_CONFIGURATION2 Sub CMD */
#define USER_CONFIGURATION2_SUB_CMD_POSTNR_SCALE      0x00
#define USER_CONFIGURATION2_SUB_CMD_POSTNR_MIX_RATE   0x01
#define USER_CONFIGURATION2_SUB_CMD_EDGE_THRESHOLD    0x02
#define USER_CONFIGURATION2_SUB_CMD_NOISE_THRESHOLD   0x03
#define USER_CONFIGURATION2_SUB_CMD_TPHE3_CLIP_UP     0x04
#define USER_CONFIGURATION2_SUB_CMD_TPHE3_CLIP_DOWN   0x05

/* DEBUG Sub CMD */
#define DEBUG_SUB_CMD_SOC_TEMP_MSB                  0x00
#define DEBUG_SUB_CMD_SOC_TEMP_LSB                  0x01
#define DEBUG_SUB_CMD_FIRM_VER_MAJOR				0x02
#define DEBUG_SUB_CMD_FIRM_VER_MINOR				0x03
#define DEBUG_SUB_CMD_FIRM_VER_PATCH				0x04
#define DEBUG_SUB_CMD_TECLESS_TMP_RANGE				0x05
#define DEBUG_SUB_CMD_FX3_FIRM_VER_MAJOR			0x06
#define DEBUG_SUB_CMD_FX3_FIRM_VER_MINOR			0x07
#define DEBUG_SUB_CMD_FX3_FIRM_VER_PATCH			0x08
#define DEBUG_SUB_CMD_SOC_INPUT_SRC_CTRL			0x09

#define I2C_WRITE 0x00
#define I2C_RAED 0x01

#define MODE_FACTORY    0x01
#define MODE_NORMAL     0x00

#define OUT_Y16         0x01
#define OUT_UYVY        0x00

#define CAMERA_START    0x01
#define CAMERA_STOP     0x00

#define SHUTTER_OPEN    0x01
#define SHUTTER_CLOSE   0x00

#define NR_MODE_SELECTIVE       0x00
#define NR_MODE_PSEUDO_GUIDED   0x01
#define NR_MODE_FAST_GUIDED     0x02
#define NR_MODE_DDE             0x03
#define NR_MODE_DDE2            0x04
#define NR_MODE_DDE3            0x05
#define NR_MODE_FAST_GUIDED2    0x06

typedef struct ClCmdPkt {
    uint8_t header0 = 0x43;   /* 'C' = 0x43 */
    uint8_t header1 = 0x4C;   /* 'L' = 0x4C */
    uint8_t mainId;
    uint8_t subId;
    uint8_t rw;        /* 1=Read, 0=Write */
    uint8_t reserved = 0x00;
    uint8_t data;      /* Write시 payload, Read시 무시 */
} ClCmdPkt;

typedef struct ClRspPkt {
    uint8_t header0;   /* echo 'C' */
    uint8_t header1;   /* echo 'L' (또는 수신값 그대로 에코) */
    uint8_t mainId;
    uint8_t subId;
    uint8_t rw;        /* 1=Read, 0=Write */
    uint8_t result;    /* 0x00=OK, 0xFF=ERR */
    uint8_t data;      /* Read 결과(1B) 또는 Write 응답(관례상 0) */
} ClRspPkt;

class DataPacket
{
public:
    DataPacket();

    static QByteArray toBytes(const ClCmdPkt& cmd)
    {
        QByteArray tx;
        tx.resize(7);

        tx[0] = static_cast<char>(0x43);
        tx[1] = static_cast<char>(0x4C);
        tx[2] = static_cast<char>(cmd.mainId);
        tx[3] = static_cast<char>(cmd.subId);
        tx[4] = static_cast<char>(cmd.rw);
        tx[5] = static_cast<char>(cmd.reserved);
        tx[6] = static_cast<char>(cmd.data);

        return tx;
    }

    static_assert(sizeof(ClCmdPkt) == 7, "ClCmdPkt must be 7 bytes");
    static_assert(sizeof(ClRspPkt) == 7, "ClRspPkt must be 7 bytes");

    static inline bool tryParse(QByteArray &buf, ClRspPkt &out)
    {
        constexpr int RSP_SIZE = 7;

        // 최소 패킷 길이 확인
        if (buf.size() < RSP_SIZE)
            return false;

        // Header 위치 찾기
        int pos = buf.indexOf(QByteArray("\x43\x4C", 2));

        if (pos < 0)
        {
            // 헤더가 없으면 쓰레기 데이터로 보고 버림
            buf.clear();
            return false;
        }

        // 헤더 앞에 쓰레기 데이터가 있으면 제거
        if (pos > 0)
            buf.remove(0, pos);

        // 헤더 정렬 후에도 길이가 부족하면 더 기다림
        if (buf.size() < RSP_SIZE)
            return false;

        ClRspPkt tmp{};
        tmp.header0 = static_cast<uint8_t>(buf[0]);
        tmp.header1 = static_cast<uint8_t>(buf[1]);
        tmp.mainId  = static_cast<uint8_t>(buf[2]);
        tmp.subId   = static_cast<uint8_t>(buf[3]);
        tmp.rw      = static_cast<uint8_t>(buf[4]);
        tmp.result  = static_cast<uint8_t>(buf[5]);
        tmp.data    = static_cast<uint8_t>(buf[6]);

        // result 값 검증
        if (tmp.result != 0x00 && tmp.result != 0xFF)
        {
            // 현재 헤더가 우연히 데이터 안에 섞인 것일 수 있으므로 1바이트 제거 후 재시도 유도
            buf.remove(0, 1);
            return false;
        }

        out = tmp;

        // 파싱한 패킷 제거
        buf.remove(0, RSP_SIZE);

        return true;
    }

    static inline QString makeSerialString(quint8 A, quint8 B, quint8 C, quint8 D)
    {
        // 비트 조립 (질문 코드 그대로)
        quint16 NB1 = quint16(((A & 0x1F) << 8) | B);
        quint8  NB2 = quint8 ((C >> 2) & 0x1F);
        quint16 NB3 = quint16(((C & 0x03) << 8) | D);

        // 4자리 + 2자리 + 3자리, 0-padding
        return QString("%1%2%3")
                .arg(NB1, 4, 10, QLatin1Char('0'))
                .arg(NB2, 2, 10, QLatin1Char('0'))
                .arg(NB3, 3, 10, QLatin1Char('0'));
    }
    static inline QString toHexLine(const QByteArray& ba)
    {
        QString out;
        out.reserve(5 * ba.size()); // 대략 "0xNN " * N
        for (unsigned char b : ba) {
            out += QString("0x%1 ").arg(b, 2, 16, QLatin1Char('0')).toUpper();
        }
        if (!out.isEmpty()) out.chop(1); // 끝 공백 제거
        return out;
    }
    static inline float ads1115_to_voltage(int16_t raw)
    {
        return (raw * 4.096f) / 32768.0f;   // ±4.096V 설정 기준
    }

    static inline float voltage_to_temp(float voltage)
    {
        return -187.37f * voltage + 412.5f;
    }

    static inline QString toHexUpperNoPad(uint8_t v) {
        return QString::number(v, 16).toUpper();
    }

    static inline bool parseHex8(const QString& s, int& out)
    {
        bool ok = false;
        out = s.toInt(&ok, 16);      // "3A", "A4", "FF" 등
        if (!ok) return false;
        if (out < 0 || out > 255) return false;
        return true;
    }
};


#endif // DATAPACKET_H
