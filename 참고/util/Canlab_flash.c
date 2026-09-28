/*
 *  Copyright (C) 2017 Texas Instruments Incorporated - http://www.ti.com/
 *
 *  Redistribution and use in source and binary forms, with or without
 *  modification, are permitted provided that the following conditions
 *  are met:
 *
 *    Redistributions of source code must retain the above copyright
 *    notice, this list of conditions and the following disclaimer.
 *
 *    Redistributions in binary form must reproduce the above copyright
 *    notice, this list of conditions and the following disclaimer in the
 *    documentation and/or other materials provided with the
 *    distribution.
 *
 *    Neither the name of Texas Instruments Incorporated nor the names of
 *    its contributors may be used to endorse or promote products derived
 *    from this software without specific prior written permission.
 *
 *  THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
 *  "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
 *  LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
 *  A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
 *  OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
 *  SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
 *  LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
 *  DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
 *  THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 *  (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
 *  OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 *
 */

 /**
 * \file   canlab_flash.c
 *
 * \brief  This file contains the program for the Mflash PC side.
           These APIs are used for configuration
 *         of instance, transmission and reception of data.
 **/

/* ========================================================================== */
/*                             Include Files                                  */
/* ========================================================================== */
#include "Canlab_flash.h"

#ifndef _WIN32
#  include <sys/select.h>
#endif

/* ========================================================================== */
/*                      Logging Callback Implementation                       */
/* ========================================================================== */
MFlashLogCallback g_logCb = NULL;
static MFlashProgressCallback g_progCb = NULL;

void MFlash_SetLogCallback(MFlashLogCallback cb) { g_logCb = cb; }
void MFlash_SetProgressCallback(MFlashProgressCallback cb) { g_progCb = cb; }


/* ========================================================================== */
/*                             Global Variables                               */
/* ========================================================================== */
volatile uint8_t menuChoice = 0;   // 1=Erase, 2=Write, 3=Exit
volatile int exitFlag = 0;         // 종료 요청 여부
volatile int writeAbortFlag = 0;   // GUI에서 Write 중단 요청 시 1
volatile int ackReceived = 0;
volatile int complete = 0;

char file[4][300];
char offset[4][15];

#ifdef _WIN32
HANDLE hThreadRx = NULL;
#else
pthread_t tidrx = 0;
#endif

/* ========================================================================== */
/*                          Function Definitions                              */
/* ========================================================================== */

/**
 * \brief  Used to calculate the filesize of a given file
 */
uint32_t MFlash_getFileSize(char *fileName)
{
    struct stat st;
    if(stat(fileName, &st) < 0)
    {
        MFLASH_PRINTF("\n[PC][error] File Not Found: %s\n", fileName);
        exit(-1);
    }
    uint32_t size = st.st_size;
    return size;
}

/**
 * \brief  Put a delay of mseconds milliseconds
 */
void MFlash_putDelay(uint32_t mseconds)
{
    clock_t goal = mseconds + clock();
    while(goal > clock()){};
}

/**
 * \brief  Used to find the smaller unsigned integer
 */
uint32_t MFlash_getMinInt(uint32_t a, uint32_t b)
{
    if(a > b)
    {
        return b;
    }
    return a;
}

#ifdef _WIN32
/**
 * \brief  Writes lpBuffer to the hComm port
 */
uint32_t MFlash_puts(MFlash_Descriptor hComm, uint8_t *lpBuffer,
                     uint32_t bytestoWrite)
{
    // No of bytes written to the port
    MFlash_Word dNoOfBytesWritten = 0U;
    WriteFile(hComm,                // Handle to the Serial port
            lpBuffer,               // Data to be written to the port
            bytestoWrite,           //No of bytes to write
            &dNoOfBytesWritten,     //Bytes written
            NULL);
    return dNoOfBytesWritten;
}

/**
 * \brief  Start a serial communication port with given parity and baud
 */
MFlash_Descriptor MFlash_startPort(char *port, uint8_t parity, uint32_t baud)
{
    MFlash_Descriptor hComm;
    hComm = CreateFileA(port,
                        GENERIC_READ | GENERIC_WRITE,
                        0,
                        NULL,
                        OPEN_EXISTING,
                        0,
                        NULL);

    // Initializing DCB structure
    DCB dcbSerialParams;
    dcbSerialParams.DCBlength = sizeof(dcbSerialParams);

    GetCommState(hComm, &dcbSerialParams);

    dcbSerialParams.BaudRate = baud;        // Setting BaudRate = baud
    dcbSerialParams.ByteSize = 8U;           // Setting ByteSize = 8
    dcbSerialParams.StopBits = ONESTOPBIT;  // Setting StopBits = 1
    dcbSerialParams.Parity   = parity;      // Setting Parity = parity

    SetCommState(hComm, &dcbSerialParams);

    COMMTIMEOUTS timeouts = {0};
    timeouts.ReadIntervalTimeout         = 50;
    timeouts.ReadTotalTimeoutConstant    = 50;
    timeouts.ReadTotalTimeoutMultiplier  = 10;
    timeouts.WriteTotalTimeoutConstant   = 50;
    timeouts.WriteTotalTimeoutMultiplier = 10;
    SetCommTimeouts(hComm, &timeouts);

    GetCommState(hComm, &dcbSerialParams);
    MFLASH_PRINTF("\n     Baud     = %d", dcbSerialParams.BaudRate);
    MFLASH_PRINTF("\n     Parity   = %d", dcbSerialParams.Parity);
    MFLASH_PRINTF("\n     StopBits = %d", dcbSerialParams.StopBits);
    MFLASH_PRINTF("\n     ByteSize = %d", dcbSerialParams.ByteSize);

    PurgeComm(hComm, PURGE_RXABORT);

    return hComm;
}

/**
 * \brief  Used to get a string of given lenght from the COM Port
 */
MFlash_Word MFlash_gets(MFlash_Descriptor hComm, uint8_t *buffer,
                        uint32_t noOfBytestoRead)
{
    MFlash_Word noOfBytesRead;
    ReadFile(hComm,            //Handle of the Serial port
              buffer,           //temporary character
              noOfBytestoRead,  //Size of tempChar
              &noOfBytesRead,   //Number of bytes read
              NULL);
    return noOfBytesRead;
}

/**
 * \brief  1 byte read with timeout.
 * \return  1 = read 1 byte, 0 = timeout, -1 = fatal (handle closed, device gone)
 *
 * Windows: COMMTIMEOUTS (50ms total/Read) 기반으로 누적 대기.
 */
static int MFlash_getByteTimeout(MFlash_Descriptor hComm, uint8_t *out, int timeoutMs)
{
    int waited = 0;
    while (waited < timeoutMs) {
        MFlash_Word n = 0;
        BOOL bok = ReadFile(hComm, out, 1, &n, NULL);
        if (!bok)        return -1;   /* handle invalid, device removed 등 */
        if (n == 1)      return 1;
        waited += 50;                  /* COMMTIMEOUTS.ReadTotalTimeoutConstant */
    }
    return 0;
}

/**
 * \brief  Used to free the port handle
 */
void MFlash_stopPort(MFlash_Descriptor hComm)
{
    CloseHandle(hComm);
}

#else
/**
 * \brief  Writes lpBuffer to the hComm port
 */
uint32_t MFlash_puts(int32_t hComm, uint8_t *lpBuffer,
                     uint32_t noOfBytesToWrite)
{
    // No of bytes written to the port
    uint32_t dNoOfBytesWritten = 0U;
    dNoOfBytesWritten = write(hComm, lpBuffer, noOfBytesToWrite);
    return dNoOfBytesWritten;
}

/**
 * \brief  Start a serial communication port with given parity and baud
 */
int32_t MFlash_startPort(char *port, uint8_t parity, uint32_t baud)
{
    struct termios tty;
    int32_t fd;

    fd = open(port, O_RDWR | O_NOCTTY | O_SYNC);
    if (fd < 0)
    {
        MFLASH_PRINTF("error opening %s", port);
        return fd;
    }

    if(tcgetattr(fd, &tty) != 0)
    {
        MFLASH_PRINTF("Error getting the attributes of the port\n");
        return -1;
    }

    if(baud == B115200)
    {
        cfsetospeed(&tty, baud);
        cfsetispeed(&tty, baud);
    }
    else
    {
        MFLASH_PRINTF("Not supported by this API\n");
        return -1;
    }
    fcntl(fd, F_SETFL, 0);

    tty.c_iflag &= ~(IGNBRK | BRKINT | PARMRK | ISTRIP | INLCR | IGNCR | ICRNL | IXON);
    tty.c_lflag &= ~(ECHO | ECHONL | ICANON | ISIG | IEXTEN);

    tty.c_oflag &= ~OPOST;

    tty.c_cc[VMIN] = 1;
    tty.c_cc[VTIME] = 1;

    if(parity == EVENPARITY)
    {
        tty.c_cflag |= (CLOCAL | CREAD);
        tty.c_cflag |= PARENB;
        tty.c_cflag &= ~CSTOPB;
        tty.c_cflag &= ~CSIZE;
        tty.c_cflag |= CS8;
        tty.c_cflag &= ~CRTSCTS;
    }
    else
    {
        MFLASH_PRINTF("Invalid parity type\n");
    }

    if (tcsetattr(fd, TCSANOW, &tty) != 0U)
    {
        MFLASH_PRINTF("error from tcsetattr");
        return -1;
    }

    sleep(2U);
    tcflush(fd, TCIOFLUSH);
    return fd;
}

int32_t MFlash_startPort2(char *port, uint8_t parity, uint32_t baud)
{
    struct termios tty;
    struct serial_struct serinfo;
    int32_t fd;

    fd = open(port, O_RDWR | O_NOCTTY | O_SYNC);
    if (fd < 0)
    {
        MFLASH_PRINTF("error opening %s", port);
        return fd;
    }

    if(tcgetattr(fd, &tty) != 0)
    {
        MFLASH_PRINTF("Error getting the attributes of the port\n");
        close(fd);
        return -1;
    }

    if(baud == B115200 || baud == B921600)
    {
        cfsetospeed(&tty, baud);
        cfsetispeed(&tty, baud);
    }
    else if(baud == BAUD_RATE_3686400 || baud == BAUD_RATE_12000000)
    {
        serinfo.reserved_char[0] = 0U;
        ioctl(fd, TIOCGSERIAL, &serinfo);
        serinfo.flags = (serinfo.flags & ~ASYNC_SPD_MASK) | ASYNC_SPD_CUST;
        serinfo.custom_divisor = (serinfo.baud_base + (baud / 2U)) / baud;

        ioctl(fd, TIOCSSERIAL, &serinfo);
        ioctl(fd, TIOCGSERIAL, &serinfo);
        cfsetospeed(&tty, B38400);
        cfsetispeed(&tty, B38400);
        cfmakeraw(&tty);
    }

    // Enable receiver & local mode
    tty.c_cflag |= (CLOCAL | CREAD);

    // Parity
    if (parity == NOPARITY)
    {
        tty.c_cflag &= ~PARENB;
        tty.c_cflag &= ~CSTOPB;
        tty.c_cflag &= ~CSIZE;
        tty.c_cflag |= CS8;
    }
    else
    {
        MFLASH_PRINTF("Invalid parity type\n");
    }

    // Flow control
    tty.c_iflag &= ~(IXON | IXOFF | IXANY); // disable sw flow
    tty.c_cflag &= ~CRTSCTS;                // disable hw flow

    // timeout
    tty.c_cc[VMIN]  = 0;   // read byte count
    tty.c_cc[VTIME] = 10;  // timeout (1.0초)

    if (tcsetattr(fd, TCSANOW, &tty) != 0U)
    {
        MFLASH_PRINTF("error from tcsetattr");
        close(fd);
        return -1;
    }

    sleep(1U);
    tcflush(fd, TCIOFLUSH);

    return fd;
}


/**
 * \brief  Used to get a string of given lenght from the COM Port
 */
uint32_t MFlash_gets(MFlash_Descriptor  hComm, uint8_t *buffer,
                    uint32_t dNoOFBytestoRead)
{
    uint32_t dNoOFBytesRead = read(hComm, buffer, dNoOFBytestoRead);
    return dNoOFBytesRead;
}

/**
 * \brief  1 byte read with timeout.
 * \return  1 = read 1 byte, 0 = timeout, -1 = fatal (fd closed, device gone)
 *
 * Linux: select() 로 readability 대기 후 read().
 */
static int MFlash_getByteTimeout(MFlash_Descriptor hComm, uint8_t *out, int timeoutMs)
{
    fd_set rfds;
    struct timeval tv;

    FD_ZERO(&rfds);
    FD_SET(hComm, &rfds);
    tv.tv_sec  = timeoutMs / 1000;
    tv.tv_usec = (timeoutMs % 1000) * 1000;

    int rv = select(hComm + 1, &rfds, NULL, NULL, &tv);
    if (rv < 0)  return -1;   /* fd closed, EINTR 포함 — 호출자가 처리 */
    if (rv == 0) return 0;    /* timeout */

    ssize_t n = read(hComm, out, 1);
    if (n <= 0) return -1;    /* EOF / device gone */
    return 1;
}

/**
 * \brief  Used to free the port handle
 */
void MFlash_stopPort(MFlash_Descriptor  hComm)
{
    close(hComm);
}
#endif

/**
 * \brief  Used to send the file to UART in chunks
 */

/**
 * \brief  Read the 70 character ASCII ID from the RBL.
 * \return 0 on success, -1 on timeout / fatal read error.
 *
 *  per-byte timeout 만으로는 garbage byte 가 연속 도착할 때 무한루프 → total deadline 적용.
 *  - 첫 0x04 byte 도달까지 : 5s 총 한도
 *  - 나머지 69 byte 수집  : 5s 총 한도
 */
int MFlash_getASICId(MFlash_Descriptor hComm)
{
    uint8_t temp = 0;
    int i = 69;
    int rc;
    time_t deadline;

    /* 첫 byte: 0x04 도달 또는 5s 후 포기 */
    deadline = time(NULL) + 5;
    while (1) {
        if (time(NULL) >= deadline) {
            MFLASH_PRINTF("\n[PC][ERROR] ASIC ID timeout — no 0x04 from board (5s budget).\n");
            return -1;
        }
        rc = MFlash_getByteTimeout(hComm, &temp, 500);
        if (rc < 0) {
            MFLASH_PRINTF("\n[PC][ERROR] ASIC ID read error (port closed or device removed).\n");
            return -1;
        }
        if (rc == 0) continue;
        if (temp == 4) break;
        /* 다른 byte 는 무시하고 deadline 안에서 0x04 계속 탐색 */
    }
    MFLASH_PRINTF("[RBL]%02X\t", temp);

    /* 나머지 69 byte: 5s 총 한도 */
    deadline = time(NULL) + 5;
    while (i) {
        if (time(NULL) >= deadline) {
            MFLASH_PRINTF("\n[PC][ERROR] ASIC ID stream timeout (%d bytes remaining).\n", i);
            return -1;
        }
        rc = MFlash_getByteTimeout(hComm, &temp, 500);
        if (rc < 0) {
            MFLASH_PRINTF("\n[PC][ERROR] ASIC ID stream read error.\n");
            return -1;
        }
        if (rc == 0) continue;
        MFLASH_PRINTF("[RBL]%02X\t", temp);
        i--;
    }
    return 0;
}


/* ========================================================================== */
/*                          Logging Functions                                 */
/* ========================================================================== */
void MFlash_LogPrintf(const char *fmt, ...)
{
    char buf[256];
    va_list args;
    va_start(args, fmt);
    vsnprintf(buf, sizeof(buf), fmt, args);
    va_end(args);

    static char rblLine[1024];
    static int rblPos = 0;

    if (strncmp(buf, "[RBL]", 5) == 0) {
        int n = snprintf(rblLine + rblPos, sizeof(rblLine) - rblPos, "%s ", buf);
        if (n > 0) rblPos += n;

        if (rblPos > 900) {
            if (g_logCb) g_logCb(rblLine);
            rblPos = 0;
            rblLine[0] = '\0';
        }
    } else {
        if (rblPos > 0) {
            if (g_logCb) g_logCb(rblLine);
            rblPos = 0;
            rblLine[0] = '\0';
        }
        if (g_logCb) g_logCb(buf);
    }
}




/**
 * \brief  Write sbl_mflash to the UART Port
 */
uint8_t MFlash_putSblMflash(MFlash_Descriptor hComm, char *fileName,
                            uint32_t size)
{
    uint8_t buffer[MFLASH_BUFFER];
    int32_t countSize;
    uint32_t sizeToSend;
    FILE *ptr;

    ptr = fopen(fileName, "rb");
    MFLASH_PRINTF("\n[PC] File Size  = %d", size );

    countSize = size;
    while(countSize > 0U)
    {
        sizeToSend = MFlash_getMinInt(MFLASH_BUFFER, countSize);
        size_t readn = fread(buffer, 1U, sizeToSend, ptr);
        if (readn == 0) break;

        MFlash_puts(hComm, buffer, sizeToSend);
        countSize = countSize - sizeToSend;

        uint32_t PerTransferred = ((size - countSize) * 100.0 / size);
        if( PerTransferred > 10U)
        {
            if (g_progCb) g_progCb(PerTransferred);  // Qt GUI 쪽으로 전달
            PerTransferred = 0U;
        }
    }

    if (g_progCb) g_progCb(0);

    return 1;
}


/**
 * \brief  Read a line from the SBL (starts with 'M', terminated by '\n').
 * \param  timeoutMs  전체 함수의 total deadline (byte 당이 아니라 라인 1개에 허용된 총 시간)
 * \return  0 on success, -1 on timeout / fatal read error.
 *
 *  per-byte timeout 만 두면 garbage 가 연속으로 흘러올 때 무한루프이므로
 *  total deadline 으로 보호한다. 각 read 자체는 짧게 (500ms) 폴링.
 */
int MFlash_getString(MFlash_Descriptor hComm, char *str, int timeoutMs)
{
    uint8_t byteRead;
    int rc;
    time_t deadline = time(NULL) + (timeoutMs / 1000) + 1;

    /* 'M' 으로 시작하는 라인을 만날 때까지 */
    while (1) {
        if (time(NULL) >= deadline) return -1;
        rc = MFlash_getByteTimeout(hComm, &byteRead, 500);
        if (rc < 0) return -1;
        if (rc == 0) continue;
        if (byteRead == 'M') break;
    }
    *str++ = (char)byteRead;

    /* '\n' 까지 수집 */
    while (1) {
        if (time(NULL) >= deadline) return -1;
        rc = MFlash_getByteTimeout(hComm, &byteRead, 500);
        if (rc < 0) return -1;
        if (rc == 0) continue;
        *str++ = (char)byteRead;
        if (byteRead == '\n') break;
    }
    *str = '\0';
    return 0;
}

/* ========================================================================== */
/*                          Step 1: UART 포트 열기                            */
/* ========================================================================== */
MFlash_Descriptor MFlash_OpenPort(char *comPort, uint8_t parity, uint32_t baud)
{
    MFlash_Descriptor hComm = MFlash_startPort(comPort, parity, baud);

#ifdef _WIN32
    if (hComm == INVALID_HANDLE_VALUE || hComm == NULL) {
        MFLASH_PRINTF("[PC] Error in opening serial port %s\n", comPort);
        return INVALID_HANDLE_VALUE;
    }
#else
    if (hComm < 0) {
        MFLASH_PRINTF("[PC] Error in opening serial port %s\n", comPort);
        return -1;
    }
#endif

    MFLASH_PRINTF("[PC] Opening serial port successful (%s).\n", comPort);
    fflush(stdout);
    return hComm;
}

/* ========================================================================== */
/*                          Step 2: 펌웨어 파일 로드                           */
/* ========================================================================== */
uint8_t MFlash_LoadFirmwareFiles(char file[4][300], char offset[4][15])
{
    uint8_t numFiles = 0U;

/*
    strcpy(file[numFiles], SBL_LOCATION);
    strcpy(offset[numFiles], SBL_OFFSET);
    numFiles++;

    strcpy(file[numFiles], APPIMAGE_LOCATION);
    strcpy(offset[numFiles], APPIMAGE_OFFSET);
    numFiles++;

    strcpy(file[numFiles], TEC_LOCATION);
    strcpy(offset[numFiles], TEC_OFFSET);
    numFiles++;

    strcpy(file[numFiles], USER_LOCATION);
    strcpy(offset[numFiles], USER_OFFSET);
    numFiles++;
*/
    if (numFiles == 0U) {
        MFLASH_PRINTF("[PC] ERROR: No file specified\n");
        return 0U;
    }

    for (uint8_t i = 0U; i < numFiles; i++) {
        MFLASH_PRINTF("[PC] File[%d]   : %s\n", i, file[i]);
        MFLASH_PRINTF("[PC] Offset[%d] : %s\n", i, offset[i]);
    }

    return numFiles;
}

/* ========================================================================== */
/*                          Step 3: ASIC, PERI, SBL_MFLASH                    */
/* ========================================================================== */
/**
 * \return 0 on success, -1 on failure (잘못된 포트 / 보드 무응답 등).
 *         성공/실패 모두 내부에서 MFlash_stopPort 를 수행하므로 호출자는
 *         별도 close 가 필요 없음.
 */
int MFlash_RequestAsicPeriAndSendSBL(MFlash_Descriptor hComm,
                                     const char *sblMflashFile)
{
    int rc = 0;
    uint32_t asicRequest = ASIC_REQ;
    uint32_t peripherialBootRequest = PERI_REQ;
    uint32_t sblMflashSize;

    /* 3-1. ASIC ID 요청/응답 — 잘못된 포트면 여기서 5s timeout 으로 빠져나감 */
    if (MFlash_getASICId(hComm) < 0) { rc = -1; goto out; }
    MFLASH_PRINTF("\n[PC] Requesting the ASIC ID\n");
    MFlash_puts(hComm, (uint8_t *)(&asicRequest), 4U);
    if (MFlash_getASICId(hComm) < 0) { rc = -1; goto out; }

    /* 3-2. PERIPHERAL Boot 요청 */
    MFLASH_PRINTF("\n[PC] Requesting PERI_REQ mode");
    MFlash_puts(hComm, (uint8_t *)(&peripherialBootRequest), 4U);

    /* 3-3. SBL_MFLASH 전송 */
    MFLASH_PRINTF("\n[PC] Sending SBL_MFLASH filesize.");
    sblMflashSize = MFlash_getFileSize((char*)sblMflashFile);
    MFLASH_PRINTF("\n[PC] Size of sbl_mflash = %d", sblMflashSize );
    MFlash_puts(hComm, (uint8_t *)(&sblMflashSize), 4U);

    MFLASH_PRINTF("\n[PC] Sending SBL_MFLASH... Please wait");
    time_t begin = time(NULL);
    MFlash_putSblMflash(hComm, (char*)sblMflashFile, sblMflashSize);
    time_t end = time(NULL);
    MFLASH_PRINTF("\n[PC] Transfer Complete. Time = %.3f", (double)(end - begin));

    MFlash_putDelay(2000U);

out:
    MFlash_stopPort(hComm);
    return rc;
}

/* ========================================================================== */
/*                          Step 4: SBL Handshake                             */
/* ========================================================================== */
/**
 * \return 0 on success, -1 on timeout (보드가 mflash SBL 로 부팅 안 됨 등).
 *
 *  Step1 은 SBL boot log 가 먼저 와도 무시하고 "Mflash Begins!!" 라인을 기다리지만
 *  garbage 가 무한히 흘러오는 경우에 대비해 outer 루프에도 total deadline 적용.
 */
int MFlash_DoSblHandshake(MFlash_Descriptor hComm)
{
    char sblMflashHndShk[100];
    uint8_t byteRead;
    int rc;

    MFLASH_PRINTF("\n[PC] Waiting for SBL handshake banner...\n");

    /* Handshake Step 1: "Mflash Begins!!\r\n" 라인 대기 (총 15s 한도) */
    time_t step1Deadline = time(NULL) + 15;
    while (1) {
        if (time(NULL) >= step1Deadline) {
            MFLASH_PRINTF("\n[PC][ERROR] Handshake step1 total timeout (15s) — 'Mflash Begins!!' not received.\n");
            return -1;
        }
        if (MFlash_getString(hComm, sblMflashHndShk, 5000) < 0) {
            MFLASH_PRINTF("\n[PC][ERROR] Handshake step1 line read timeout/error.\n");
            return -1;
        }
        if (strcmp(sblMflashHndShk, SBL_MFLASH_CMD_HNDSKE_STEP1) == 0) {
            uint8_t choice[2];
            choice[0] = '1';
            MFlash_puts(hComm, choice, 1U);
            break;
        }
        /* SBL 가 다른 라인 먼저 보낼 수 있음 → 무시하고 다음 라인 시도 */
    }

    /* Handshake Step 2: 'r' byte 대기 (5s) */
    while (1) {
        rc = MFlash_getByteTimeout(hComm, &byteRead, 5000);
        if (rc < 0) {
            MFLASH_PRINTF("\n[PC][ERROR] Handshake step2 read error.\n");
            return -1;
        }
        if (rc == 0) {
            MFLASH_PRINTF("\n[PC][ERROR] Handshake step2 timeout — no 'r' from SBL.\n");
            return -1;
        }
        if (byteRead == 'r') break;
    }

    MFLASH_PRINTF("\n[PC] SBL handshake completed.");
    return 0;
}

void* rxThread(void* arg)
{
    MFlash_Descriptor hComm = *(MFlash_Descriptor*)arg;
    uint8_t ch;

    static char rxLineBuf[1024];
    static int rxPos = 0;

    /* 새 세션 시작 시 line buffer 잔존 byte 제거 (이전 abort 등으로
     * '\n' 도달 전에 끊긴 데이터가 남아있으면 다음 세션 첫 라인이
     * 깨져 보이는 원인이 됨) */
    rxPos = 0;
    rxLineBuf[0] = '\0';

    while (!exitFlag) {
#ifdef _WIN32
        DWORD bytesRead;
        if (ReadFile(hComm, &ch, 1, &bytesRead, NULL) && bytesRead == 1) {
#else
        if (read(hComm, &ch, 1) == 1) {
#endif
            if (ch == SBL_MFLASH_CMD_SEND_NEXT_PATCH) {
                ackReceived = 1;
            } else if (ch == SBL_MFLASH_CMD_TRANSFER_COMPLETE) {
                complete = 1;
            } else {
                // 라인 버퍼링 적용
                if (ch == '\r') {
                    // 무시 (CR 단독은 건너뛰기)
                } else if (ch == '\n') {
                    rxLineBuf[rxPos] = '\0';
                    if (g_logCb && rxPos > 0) {
                        g_logCb(rxLineBuf);
                    }
                    rxPos = 0;
                } else {
                    if (rxPos < (int)sizeof(rxLineBuf) - 1) {
                        rxLineBuf[rxPos++] = (char)ch;
                    }
                }
            }
        } else {
#ifdef _WIN32
            Sleep(1);
#else
            usleep(1000);
#endif
        }
    }
    return NULL;
}

void PC_SendFile(MFlash_Descriptor hComm, const char *filePath, uint32_t offset)
{
    FILE *fp = fopen(filePath, "rb");
    if (!fp) {
        MFLASH_PRINTF("[PC][ERROR] Cannot open %s\n", filePath);
        return;
    }

    uint32_t filesize = MFlash_getFileSize((char*)filePath);
    MFLASH_PRINTF("[PC] Send file: %s (size=%u, offset=0x%X)\n",
           filePath, filesize, offset);

    // filesize 전송
    MFlash_puts(hComm, (uint8_t*)&filesize, sizeof(filesize));

    // offset 전송
    MFlash_puts(hComm, (uint8_t*)&offset, sizeof(offset));

    uint8_t buffer[MFLASH_BUFFER];
    uint32_t sent = 0;
    uint8_t percount = 0;
    int isFirstAck = 1;

    /* --- 대기 타임아웃 (초) ---
     * 첫 ACK : SBL이 해당 영역 ERASE 완료 후 전송 → 영역 크기에 비례 (base 60s + 10s/MB)
     *          예) 15MB TEC 영역 → 210s. QSPI 섹터 erase 가 누적되어 큰 영역은 분 단위.
     * 청크 ACK : 페이지 write 단위라 빠름 (고정)
     * Complete : 전체 verify → 영역 크기에 비례 (base 30s + 5s/MB)
     */
    const uint32_t filesizeMB     = filesize / (1024U * 1024U);
    const int FIRST_ACK_TIMEOUT_S = 60 + (int)filesizeMB * 10;
    const int CHUNK_ACK_TIMEOUT_S = 10;
    const int DONE_TIMEOUT_S      = 30 + (int)filesizeMB * 5;

    MFLASH_PRINTF("[PC] Erasing target region... please wait.\n");

    while (sent < filesize) {
        int timeoutS = isFirstAck ? FIRST_ACK_TIMEOUT_S : CHUNK_ACK_TIMEOUT_S;
        time_t waitStart = time(NULL);
        time_t nextProgressLog = waitStart + 5;       /* 첫 진행 로그 시점 */
        int chunkWarnLogged = 0;

        while (!ackReceived) {
            if (writeAbortFlag) {
                MFLASH_PRINTF("[PC] Write aborted by user (sent=%u/%u, offset=0x%X).\n",
                              sent, filesize, offset);
                fclose(fp);
                if (g_progCb) g_progCb(0);
                return;
            }
            #ifdef _WIN32
                Sleep(1);
            #else
                usleep(1000);
            #endif

            time_t now = time(NULL);
            time_t elapsed = now - waitStart;
            if (isFirstAck && now >= nextProgressLog) {
                MFLASH_PRINTF("[PC] Erase in progress (%ld s elapsed, timeout=%ds)...\n",
                              (long)elapsed, timeoutS);
                nextProgressLog = now + 30;  /* 30s 주기 반복 */
            } else if (!isFirstAck && !chunkWarnLogged && elapsed >= 3) {
                MFLASH_PRINTF("[PC][WARN] Chunk ACK wait >3s (sent=%u/%u, offset=0x%X)\n",
                              sent, filesize, offset);
                chunkWarnLogged = 1;
            }
            if (elapsed >= timeoutS) {
                MFLASH_PRINTF("[PC][ERROR] %s ACK timeout after %ds (sent=%u/%u, offset=0x%X). Aborting.\n",
                              isFirstAck ? "First" : "Chunk",
                              timeoutS, sent, filesize, offset);
                /* PC/SBL phase 가 어긋났으므로 이후 파일도 진행 금지.
                 * Mflashtab 의 외부 루프가 writeAbortFlag 를 확인하고 break. */
                writeAbortFlag = 1;
                fclose(fp);
                if (g_progCb) g_progCb(0);
                return;
            }
        }
        ackReceived = 0;
        if (isFirstAck) {
            MFLASH_PRINTF("[PC] Erase done, starting data transfer.\n");
            isFirstAck = 0;
        }

        // === 블록 크기 결정 ===
        uint32_t chunk = MFlash_getMinInt(MFLASH_BUFFER, filesize - sent);
        size_t readn = fread(buffer, 1, chunk, fp);
        if (readn == 0) break;

        // 16바이트 정렬 (UART FIFO_TRIGGER)
        if (readn % UART_FIFO_TRIGGER != 0) {
            uint8_t extra = UART_FIFO_TRIGGER - (readn % UART_FIFO_TRIGGER);
            memset(buffer + readn, 0, extra);
            readn += extra;
        }

        MFlash_puts(hComm, buffer, (uint32_t)readn);
        sent += chunk;

        uint8_t percent = (sent * 100.0 / filesize);
        if (percent >= percount) {
            if (g_progCb) g_progCb(percent);  // Qt GUI 쪽으로 전달
            percount += 5;
        }
    }

    fclose(fp);

    MFLASH_PRINTF("[PC] All data sent (%u bytes). Waiting complete signal...\n", sent);

    {
        time_t doneStart = time(NULL);
        while (!complete) {
            if (writeAbortFlag) {
                MFLASH_PRINTF("[PC] Write aborted by user (waiting complete, offset=0x%X).\n", offset);
                if (g_progCb) g_progCb(0);
                return;
            }
#ifdef _WIN32
            Sleep(1);
#else
            usleep(1000);
#endif
            if (time(NULL) - doneStart >= DONE_TIMEOUT_S) {
                MFLASH_PRINTF("[PC][ERROR] Complete signal timeout after %ds (offset=0x%X). Aborting.\n",
                              DONE_TIMEOUT_S, offset);
                /* verify 응답 누락 → SBL 상태 불명, 이후 파일 진행 금지 */
                writeAbortFlag = 1;
                if (g_progCb) g_progCb(0);
                return;
            }
        }
    }
    complete = 0; // 다음 파일 전송 대비 초기화

    if (g_progCb) g_progCb(0);

    MFLASH_PRINTF("[PC] Write File Completed (Verified).\n");
}


void* menuThread(void *arg)
{
    MFlash_Descriptor hComm = *(MFlash_Descriptor*)arg;
    char inputBuf[16];

    while (!exitFlag)
    {
        MFLASH_PRINTF("\n[PC] Select Operation:\n");
        MFLASH_PRINTF("1. Erase QSPI\n");
        MFLASH_PRINTF("2. Write & Verify\n");
        MFLASH_PRINTF("3. Exit\n");
        MFLASH_PRINTF("Enter choice: ");
        fflush(stdout);

        if (fgets(inputBuf, sizeof(inputBuf), stdin) != NULL) {
            uint8_t choice;
            if (sscanf(inputBuf, "%hhu", &choice) == 1) {
                if (choice == 1) {
                    // ERASE
                    uint8_t cmd = 1U;
                    MFlash_puts(hComm, &cmd, 1);
                }
                else if (choice == 2) {
                    // WRITE
                    MFLASH_PRINTF("\n[PC] Select File to Update:\n");
                    MFLASH_PRINTF("1. SBL \n");
                    MFLASH_PRINTF("2. AppImage \n");
                    MFLASH_PRINTF("3. TECLESS \n");
                    MFLASH_PRINTF("4. USER \n");
                    MFLASH_PRINTF("5. All files \n");
                    MFLASH_PRINTF("Enter choice: ");
                    fflush(stdout);

                    if (fgets(inputBuf, sizeof(inputBuf), stdin) != NULL) {
                        int subChoice;
                        if (sscanf(inputBuf, "%d", &subChoice) == 1) {
                            if (subChoice >= 1 && subChoice <= 4) {
                                int idx = subChoice - 1;
                                uint8_t cmd = 2U;
                                MFlash_puts(hComm, &cmd, 1);

                                uint32_t ofs = strtoul(offset[idx], NULL, 0);
                                PC_SendFile(hComm, file[idx], ofs);
                            }
                            else if (subChoice == 5) {
                                for (int i = 0; i < 4; i++) {
                                    uint8_t cmd = 2U;
                                    MFlash_puts(hComm, &cmd, 1);

                                    uint32_t ofs = strtoul(offset[i], NULL, 0);
                                    PC_SendFile(hComm, file[i], ofs);

                                    #ifdef _WIN32
                                        Sleep(100);     // 100ms
                                    #else
                                        usleep(100000); // 100,000µs = 100ms
                                    #endif
                                }
                                MFLASH_PRINTF("[PC] All 4 files programmed.\n");
                            }
                            else if (subChoice == 6) {
                                MFLASH_PRINTF("[PC] Back to main menu.\n");
                            }
                            else {
                                MFLASH_PRINTF("[PC] Invalid sub-choice.\n");
                            }
                        }
                    }
                }
                else if (choice == 3) {
                    uint8_t cmd = 3U;   // EXIT
                    MFlash_puts(hComm, &cmd, 1);
                    MFLASH_PRINTF("[PC] Exit selected.\n");
                    exitFlag = 1;
                    break;
                }
                else {
                    MFLASH_PRINTF("[PC] Invalid choice.\n");
                }
            }
        }
    }
    return NULL;
}

/**
 * \brief  Print Uasge in case of an error
 */

#ifndef USE_AS_LIBRARY
int main(void)
{
    uint8_t stopByteFlag;
    MFlash_Descriptor hComm;

    char com[50] = MFLASH_PORT_PREFIX;
    strcat(com, UART_PORT_NUMBER);

    /* Step 1: 포트 열기 */
    hComm = MFlash_OpenPort(com, EVENPARITY, BAUD_RATE_115200);
    if (hComm < 0) return 0;

    /* Step 2: 펌웨어 파일 로드 */
    uint8_t numFiles = MFlash_LoadFirmwareFiles(file, offset);
    if (numFiles == 0U) return 0;

    /* Step 3: ASIC + PERI + SBL_MFLASH */
    MFlash_RequestAsicPeriAndSendSBL(hComm, SBL_MFLASH_FILE);

    /* Step 4: 포트 재열기 + Handshake */
    MFLASH_PRINTF("\n[PC] Opening port for sbl_mflash.");
    hComm = MFlash_startPort2(com, NOPARITY, BAUD_RATE_12000000);

    if(hComm < 0) {
        MFLASH_PRINTF("\n[PC] Error in opening serial port.");
        return 0U;
    }

    MFlash_DoSblHandshake(hComm);

    MFLASH_PRINTF("\n[PC] sbl_mflash switch On Request Sent.");
    fflush(stdout);

    // Step 5: 수신 스레드 시작
    #ifdef _WIN32
    hThreadRx = CreateThread(NULL, 0, rxThreadWin, (void*)&hComm, 0, NULL);
    #else
    pthread_create(&tidrx, NULL, rxThread, (void*)&hComm);
    #endif

    MFlash_putDelay(2000U);

    // Step 6: 메뉴 입력 스레드 시작
    #ifdef _WIN32
    hThreadMenu = CreateThread(NULL, 0, menuThreadWin, (void*)&hComm, 0, NULL);
    #else
    pthread_create(&tidMenu, NULL, menuThread, (void*)&hComm);
    #endif

    // Step 7: 실제 파일 전송 루프
    stopByteFlag = 1U;
    while (!exitFlag && stopByteFlag != 0U)
    {
        #ifdef _WIN32
            Sleep(100);     // 100ms
        #else
            usleep(100000); // 100,000µs = 100ms
        #endif
    }

    // 종료 처리
    exitFlag = 1;

    #ifdef _WIN32
    WaitForSingleObject(hThreadRx, INFINITE);
    WaitForSingleObject(hThreadMenu, INFINITE);
    CloseHandle(hThreadRx);
    CloseHandle(hThreadMenu);
    #else
    pthread_join(tidrx, NULL);
    pthread_join(tidMenu, NULL);
    #endif

    MFlash_stopPort(hComm);
    MFLASH_PRINTF("\n[PC] ##############!!!!mflash shutting down!!!!#############");

    return 0;
}
#endif

#ifdef _WIN32
DWORD WINAPI rxThreadWin(LPVOID arg) {
    return (DWORD)(uintptr_t)rxThread(arg);   // Linux용 rxThread 재사용
}

DWORD WINAPI menuThreadWin(LPVOID arg) {
    return (DWORD)(uintptr_t)menuThread(arg); // Linux용 menuThread 재사용
}
#endif

#if 0
/* UART TX */
static void uart_tx(MFlash_Descriptor hComm, const uint8_t *buf, uint32_t len)
{
    MFlash_puts(hComm, (uint8_t*)buf, len);
}

/* UART RX (blocking) */
static uint32_t uart_rx(MFlash_Descriptor hComm, uint8_t *buf, uint32_t len)
{
    uint32_t got = 0;
    while (got < len) {
        got += MFlash_gets(hComm, buf + got, len - got);
    }
    return got;
}

void UartLoopbackTest(MFlash_Descriptor hComm)
{
    uint8_t txBuf[] = "HELLO_UART\n";
    uint8_t rxBuf[64];

    tcflush(hComm, TCIOFLUSH);  // 송신 전만 flush

    MFLASH_PRINTF("[PC] Sending: %s", txBuf);
    uart_tx(hComm, txBuf, strlen((char*)txBuf));

    // 100ms 안에 들어오는 모든 RX 읽기
    int rlen = read(hComm, rxBuf, sizeof(rxBuf)-1);
    if (rlen > 0) {
        rxBuf[rlen] = '\0';
        MFLASH_PRINTF("[PC] Received: %s\n", rxBuf);
    } else {
        MFLASH_PRINTF("[PC] No response.\n");
    }
}
#endif
