import serial
import serial.tools.list_ports
import threading
import traceback
import os
import logging
import yaml
from path_util import get_resource_path

class SerialComm:

    """ 시리얼 통신을 관리하는 클래스 C#코드와 동일"""

    def __init__(self):
        self.port_list = self.get_available_ports() 
        self.baud_rate = 115200
        self.serial = None 
        self.running = False
        self.data_len = 7

        self.DATA_PACKET = self.load_data_packet(os.path.join(get_resource_path('core\\serial'), 'data_packet.yaml'))

    def load_data_packet(self, path):
        with open(path, "r", encoding="utf-8") as f:
            data_packet = yaml.safe_load(f)
        return data_packet
    
    def get_available_ports(self):
        """ 사용 가능한 시리얼 포트를 검색 """
        ports = list(serial.tools.list_ports.comports())
        return [port.device for port in ports]

    def initialize(self,selected_port):
        """ 시리얼 포트 초기화 및 연결 """
        if not self.port_list:
            print("[ERROR] 시리얼 포트가 설정되지 않았습니다.")
            return

        if self.serial and self.serial.is_open:
            print(f'close serial port {selected_port}')
            self.running = False
            self.serial.close()  # 기존 포트가 열려 있으면 닫기
        try:
            self.serial = serial.Serial(selected_port, self.baud_rate, timeout=1)
            self.running = True
            print(f'[INFO] open serial port {selected_port}')
        except serial.SerialException as e:
            self.running = False
            print(f"[ERROR] 시리얼 포트 연결 실패1: {e}")
            
    def read_data(self):
        """ 시리얼 데이터를 지속적으로 수신하는 함수 (백그라운드 스레드에서 실행) """
        while self.running:
            try:
                # 시리얼 포트가 존재하지 않거나 닫힌 경우 루프 탈출
                if not self.serial or not hasattr(self.serial, "is_open") or not self.serial.is_open:
                    print("WARNING: Serial port is closed. Stopping receive loop.")
                    break

                data = self.serial.readline().decode("utf-8", errors="ignore").strip()
                # try:
                #     value = int(data, 16)       # 16진수 문자열 → 정수
                #     value = value & 0x3FFF      # 하위 14비트만 추출
                #     # print(value)
                # except ValueError:
                #     print("Invalid hex data:", data)
                if data:
                    print(f"Received data: {data}")
                    if self.data_received_callback:
                        self.data_received_callback(data)

            except serial.SerialTimeoutException:
                pass  # 타임아웃 발생 시 무시

            except AttributeError as e:
                print(f"Exception (AttributeError): {e}")
                traceback.print_exc()  # 예외 전체 출력

                break  # 더 이상 시리얼 객체가 없으면 루프 종료

            except TypeError as e:
                print(f"Exception (TypeError): {e}")
                traceback.print_exc()  # 예외 전체 출력

                break  # NoneType 접근 방지

            except Exception as e:
                print(f"Exception (Other): {e}")
                traceback.print_exc()  # 예외 전체 출력

                break  # 기타 예외 발생 시 루프 종료

    def open(self):
        """ 시리얼 포트를 여는 함수 """
        if self.serial and not self.serial.is_open:
            try:
                self.serial.open()
                print("Serial port opened.")
            except Exception as e:
                logging.error(traceback.format_exc())
                print(f"Failed to open serial port: {e}")

    def close(self):
        """ 시리얼 포트를 닫는 함수 """
        self.running = False
        if self.serial and self.serial.is_open:
            self.serial.close()
            print("Serial port closed.")

    def send_data(self, command1, command2, rw, data=0x00):
        tx_message = bytearray(7)
        tx_message[0:2] = self.DATA_PACKET.get('HEADER')
        tx_message[2] = self.DATA_PACKET.get('MAIN_ID').get(command1)
        tx_message[3] = self.DATA_PACKET.get('SUB_ID').get(command1).get(command2)
        tx_message[4] = self.DATA_PACKET.get('RW').get(rw)
        if command2 == "SHUTTER":
            tx_message[6] = data if data==0x00 else self.DATA_PACKET.get('DATA').get(command1).get(command2).get(data)
        else:
            tx_message[6] = data
        if self.serial and self.serial.is_open:
            try:
                if command2 == "SERIAL_NB_A" or command2 == "SERIAL_NB_B" or command2 == "SERIAL_NB_C" or command2 == "SERIAL_NB_D" or command2 == "FPA_TEMP_MSB" or command2 == "FPA_TEMP_LSB":
                    self.serial.write(tx_message)
                    # print(f"[SERIAL] {command1} {command2} send: {tx_message.hex()}")
                    return tx_message
                else:
                    self.serial.write(tx_message)
                    # print(f"[SERIAL] {command1} {command2} send: {tx_message.hex()}")
                    return tx_message
            except Exception as e:
                print(e)
                print(f"[SERIAL] Fail to send data {self.receive_serial_num}/{self.receive_serial_num}/{self.receive_serial_num}/{self.receive_serial_num}")
        else:
            print(f"[SERIAL] Serial is not connected")
        
    def receive_data(self, data_type):
        while True:
            if self.serial.in_waiting >= self.data_len:
                data = self.serial.read(self.data_len) 
                # print(f"[SERIAL] {data_type} received: {data}")
                break
        # return int.from_bytes(data[2:], byteorder='big')
        return data.hex()