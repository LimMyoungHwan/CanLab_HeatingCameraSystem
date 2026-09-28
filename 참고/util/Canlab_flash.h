#ifndef CANLAB_FLASH_H
#define CANLAB_FLASH_H

/* ========================================================================== */
/*                             Include Files                                  */
/* ========================================================================== */
#include <stdio.h>
#include <stdint.h>
#include <stdlib.h>
#include <stdarg.h>
#include <string.h>
#include <time.h>
#include <errno.h>
#include <sys/stat.h>

#ifdef _WIN32
    #include <windows.h>
    typedef HANDLE MFlash_Descriptor;
    typedef DWORD  MFlash_Word;

    extern HANDLE hThreadRx;
#else
    #include <pthread.h>
    #include <termios.h>
    #include <linux/serial.h>
    #include <sys/ioctl.h>
    #include <fcntl.h>
    #include <unistd.h>

    typedef int32_t  MFlash_Descriptor;
    typedef uint32_t MFlash_Word;

    extern pthread_t tidrx;
#endif

/* ========================================================================== */
/*                         Global Variables (extern)                          */
/* ========================================================================== */
extern volatile int exitFlag;
extern volatile int writeAbortFlag;   /* GUI 에서 Write 중단 요청 시 1 */

/* ========================================================================== */
/*                       Logging & Progress Callback                          */
/* ========================================================================== */
typedef void (*MFlashLogCallback)(const char *msg);
typedef void (*MFlashProgressCallback)(int percent);

void MFlash_SetLogCallback(MFlashLogCallback cb);
void MFlash_SetProgressCallback(MFlashProgressCallback cb);

#ifdef __cplusplus
extern "C" {
#endif

/* 로그 출력 매크로 */
#define MFLASH_PRINTF(fmt, ...) MFlash_LogPrintf(fmt, ##__VA_ARGS__)
void MFlash_LogPrintf(const char *fmt, ...);

#ifdef __cplusplus
}
#endif

/* ========================================================================== */
/*                                Defines                                     */
/* ========================================================================== */

/* --- RBL commands --- */
#define PERI_REQ    0xF0030002
#define ASIC_REQ    0xF0030003

/* --- Baudrates --- */
#ifdef _WIN32
#  define BAUD_RATE_115200   (115200U)
#  define BAUD_RATE_921600   (921600U)
#else
#  define BAUD_RATE_115200   B115200
#  define BAUD_RATE_921600   B921600
#endif
#define BAUD_RATE_3686400    (3686400U)
#define BAUD_RATE_12000000   (12000000U)

/* --- Parity --- */
#ifndef EVENPARITY
#  define EVENPARITY  0
#  define ODDPARITY   1
#  define NOPARITY    3
#endif

/* --- UART Port Prefix --- */
#ifdef _WIN32
#  define MFLASH_PORT_PREFIX "\\\\.\\COM"
#  define MFlash_startPort2  MFlash_startPort
#else
#  define MFLASH_PORT_PREFIX "/dev/ttyUSB"
#endif

/* --- Buffer sizes --- */
#define MFLASH_BUFFER       (64U * 1024U)
#define UART_FIFO_TRIGGER   (16U)

/* --- SBL commands --- */
#define SBL_MFLASH_CMD_SEND_NEXT_PATCH   (6U)
#define SBL_MFLASH_CMD_TRANSFER_COMPLETE (4U)
#define SBL_MFLASH_CMD_HNDSKE_STEP1      "Mflash Begins!!\r\n"

/* --- Default file paths & offsets --- */
#define SBL_MFLASH_FILE   "/resources/sbl/sbl_mflash_tda3xx-canlab"

#define SBL_OFFSET        "0x00"
#define APPIMAGE_OFFSET   "0x80000"
#define TEC_OFFSET        "0x02C00000"
#define USER_OFFSET       "0x3AB0000" // "0x3B90000" // Tecless data R10:0x03B40000, R12:0x3B90000

/* ========================================================================== */
/*                                 Typedefs                                   */
/* ========================================================================== */
#ifdef _WIN32
typedef HANDLE  MFlash_Descriptor;
typedef DWORD   MFlash_Word;
#else
typedef int32_t MFlash_Descriptor;
typedef uint32_t MFlash_Word;
#endif

/* ========================================================================== */
/*                       Function Declarations                                */
/* ========================================================================== */

/* --- File utils --- */
uint32_t MFlash_getFileSize(char *fileName);
void     MFlash_putDelay(uint32_t mseconds);
uint32_t MFlash_getMinInt(uint32_t a, uint32_t b);

/* --- UART I/O --- */
MFlash_Descriptor MFlash_OpenPort(char *comPort, uint8_t parity, uint32_t baud);
MFlash_Descriptor MFlash_startPort(char *port, uint8_t parity, uint32_t baud);
MFlash_Descriptor MFlash_startPort2(char *port, uint8_t parity, uint32_t baud);

#ifdef _WIN32
uint32_t    MFlash_puts(MFlash_Descriptor hComm, uint8_t *lpBuffer, uint32_t size);
MFlash_Word MFlash_gets(MFlash_Descriptor hComm, uint8_t *buffer, uint32_t size);
void        MFlash_stopPort(MFlash_Descriptor hComm);
#else
uint32_t MFlash_puts(MFlash_Descriptor hComm, uint8_t *lpBuffer, uint32_t size);
uint32_t MFlash_gets(MFlash_Descriptor hComm, uint8_t *buffer, uint32_t size);
void     MFlash_stopPort(MFlash_Descriptor hComm);
#endif

/* --- Flash helpers --- */
void     MFlash_putChar(MFlash_Descriptor hComm, uint8_t count);
int      MFlash_getASICId(MFlash_Descriptor hComm);                     /* 0=ok, -1=timeout/err */
int      MFlash_getString(MFlash_Descriptor hComm, char *str, int timeoutMs);

uint8_t  MFlash_putFileInPatches(MFlash_Descriptor hComm, char *fileName, uint32_t size);
uint8_t  MFlash_putSblMflash(MFlash_Descriptor hComm, char *fileName, uint32_t size);

int      MFlash_RequestAsicPeriAndSendSBL(MFlash_Descriptor hComm, const char *sblMflashFile); /* 0=ok, -1=err. 내부에서 stopPort 호출 */
int      MFlash_DoSblHandshake(MFlash_Descriptor hComm);                /* 0=ok, -1=timeout/err */
void     PC_SendFile(MFlash_Descriptor hComm, const char *filePath, uint32_t offset);

/* --- Threads --- */
#ifdef _WIN32
DWORD WINAPI rxThreadWin(LPVOID arg);
DWORD WINAPI menuThreadWin(LPVOID arg);
#else
void* rxThread(void* arg);
void* menuThread(void* arg);
#endif

#endif // CANLAB_FLASH_H
