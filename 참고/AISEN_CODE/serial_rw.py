import time
import numpy as np
from serial_code import SerialComm


class SerialReadWrite(SerialComm):
    def __init__(self):
        super().__init__()
        #serial
        self.receive_serial_num = None
        #fpa
        self.fpa_stable_sec = 1
        self.receive_fpa_val = None
        #bias
        self.receive_bias_status = None
        self.selcted_temp_range = None
        self.done_bias_setting = False
        #shutter
        self.send_msg_shutter = False
        self.receive_shutter_status = None
        #shutter temp
        self.receive_shutter_temp = None
        
    def set_camera_operation(self, status):
        assert status == "START" or status == 'STOP'
        self.send_data("OPERATE_CTRL", "CAEMRA", "WRITE", status)

    def get_cam_serial(self):
        command_order = ["SERIAL_NB_A", "SERIAL_NB_B", "SERIAL_NB_C", "SERIAL_NB_D"]
        received_data = []
        for command in command_order:
            self.send_data("DETECTOR", command, "READ")
            received_data.append(self.receive_data(command))  # 예: '434c0000010014'

        # --- 추가: HEX 문자열 → 페이로드 1바이트로 변환 (순서 유지) ---
        # 가정: 각 응답의 마지막 1바이트가 A,B,C,D 페이로드임
        payload = []
        for s in received_data:
            b = bytes.fromhex(s)
            payload.append(b[-1])  # 맨 끝 바이트만 사용 (순서: A,B,C,D)

        # --- 원본 연산식은 그대로, 단 payload 사용 ---
        _serial_num1 = (((payload[0] & 0x1F) << 8) | payload[1]) & 0x1FFF
        _serial_num2 = (payload[2] >> 2) & 0x1F
        _serial_num3 = (((payload[2] & 0x03) << 8) | payload[3]) & 0x3FF

        self.receive_serial_num = f'{_serial_num1:04d}{_serial_num2:02d}{_serial_num3:03d}'
        print(self.receive_serial_num)

        # return self.receive_serial_num   

    def get_fpa_temp(self):
        pass

    





    def get_fpa_temp(self):

            recived_fpa_data = []
            self.send_data("DETECTOR", "FPA_TEMP_MSB", "READ")
            recived_fpa_data.append(self.receive_data("FPA_TEMP_MSB"))
            self.send_data("DETECTOR", "FPA_TEMP_LSB", "READ")
            recived_fpa_data.append(self.receive_data("FPA_TEMP_LSB"))
            self.fpa_temp_msb = recived_fpa_data[0][-2:]
            self.fpa_temp_lsb = recived_fpa_data[1][-2:]

            raw_hex = (int(self.fpa_temp_msb, 16) << 8) | int(self.fpa_temp_lsb, 16)

            # 16진수 문자열을 10진수로 변환
            raw_decimal = raw_hex #원본코드
            #raw_decimal = int(raw_hex, 16) *2# 임시사용
            
            # 부호 비트 처리 (16비트 부호 있는 값)
            if raw_decimal > 32767:
                raw_decimal -= 65536

            # # 전압 계산 (±4.096V, 16비트 해상도)
            scale = 4.096 #4.096                        
            max_val = 32768
            v_temp = (raw_decimal / max_val) * scale
            temperature_celsius = -187.37 * v_temp + 412.5
            print(temperature_celsius)            # self.send_msg_fpa = True
            self.receive_fpa_val = raw_decimal
            return self.receive_fpa_val
    
    def _calculate_temp(self, temp):
        if isinstance(temp, str):
            raw_hex = temp
            if not raw_hex.startswith("0x"):
                raw_hex = "0x" + raw_hex

            # 16진수 문자열을 10진수로 변환
            raw_decimal = int(raw_hex, 16)
            # 부호 비트 처리 (16비트 부호 있는 값)
            if raw_decimal > 32767:
                raw_decimal -= 65536
        if isinstance(temp, np.uint16) or isinstance(temp, np.int16) or isinstance(temp, int):
            raw_decimal = temp
        # # 전압 계산 (±4.096V, 16비트 해상도)
        scale = 4.096
        max_val = 32768
        v_temp = (raw_decimal / max_val) * scale
        # print(v_temp)
        temperature_celsius = -188.65 * v_temp + 415.48

        return temperature_celsius, raw_decimal

    def _get_temperature_range(self):
        #new
        all_temp_range = []
        tamp_select_map = []
        for k, v in self.TEMP_INFO_DICT[self.receive_serial_num].items():
            all_temp_range.extend(v)
            tamp_select_map.extend([k]*len(v))
        all_temp_range = np.array(all_temp_range)

        selected_indx = abs(self.receive_fpa_val[1] - all_temp_range).argmin()
        self.selcted_temp_range = tamp_select_map[selected_indx]

    def get_shutter_temp(self):
        if not self.send_msg_shutter_temp:
             # wait till fpa temp data received
            start_time = time.time()
            while self.send_msg_fpa and (time.time() - start_time < self.receive_timeout):
                if self.receive_msg_fpa:
                    self.receive_msg_fpa = False
                    break
                else:
                    time.sleep(0.01)
            # wait till shutter action data received
            start_time = time.time()
            while self.send_msg_shutter and (time.time() - start_time < self.receive_timeout):
                if self.receive_msg_shutter:
                    self.receive_msg_shutter = False
                    break
                else:
                    time.sleep(0.01)

            self.send_data("READ_Shutter", self._ADDRESS_DICT['SHUTTER_TEMP'], self._REGISTER_DICT['SHUTTER_TEMP'])
            self.send_msg_shutter_temp = True
    
    def calculate_shutter_temp(self, temp:str):
        raw_hex = temp
        if not raw_hex.startswith("0x"):
            raw_hex = "0x" + raw_hex
        
        raw_decimal = int(raw_hex, 16)
        raw_decimal = raw_decimal >> 4
        if raw_decimal > 32767:
            raw_decimal -= 65536

        sutter_temp = format(raw_decimal,'012b')
        if sutter_temp[0] == '1':
            inverted = raw_decimal ^ 0xFFF
            # 2의 보수: +1
            abs_val = (inverted + 1) & 0xFFF
            temperature_S = abs_val* -0.0625
        else:
            temperature_S = raw_decimal * 0.0625

        return temperature_S
    
    def set_temperature_bias_auto(self):

        self._get_temperature_range()
        assert self.selcted_temp_range is not None, 'FPA value required'
        print(f'[BIAS] AUTO BIAS SETTING: {self.selcted_temp_range} temperature', end='')
        selected_bias = self._TEMP_BIAS_DICT[self.receive_serial_num][self.selcted_temp_range]
        bias_name_list = list(selected_bias.keys())
        bias_val_list = list(selected_bias.values())
        bias_indx = 0
        while bias_indx < len(bias_name_list):
            if not self.send_msg_bias:
                self.send_data("WRITE", self._ADDRESS_DICT['BIAS'], self._REGISTER_DICT[bias_name_list[bias_indx]], bias_val_list[bias_indx])
                self.send_msg_bias = True
            start_time = time.time()
            while self.send_msg_bias and (time.time() - start_time < self.receive_timeout):
                if self.receive_msg_bias:
                    break
                else:
                    print('.', end='')
                    time.sleep(0.01)
            self.receive_msg_bias = False
            print('o', end='')
            bias_indx += 1
        print('  done')
        self.done_bias_setting = True
    
    def set_temperature_bias_manual(self, temp_range):
        self.selcted_temp_range = temp_range
        print(f'[BIAS] MANUAL BIAS SETTING: {self.selcted_temp_range} temperature', end='')
        selected_bias = self._TEMP_BIAS_DICT[self.receive_serial_num][self.selcted_temp_range]
        bias_name_list = list(selected_bias.keys())
        bias_val_list = list(selected_bias.values())
        bias_indx = 0
        while bias_indx < len(bias_name_list):
            if not self.send_msg_bias:
                self.send_data("WRITE", self._ADDRESS_DICT['BIAS'], self._REGISTER_DICT[bias_name_list[bias_indx]], bias_val_list[bias_indx])
                self.send_msg_bias = True
            start_time = time.time()
            while self.send_msg_bias and (time.time() - start_time < self.receive_timeout):
                if self.receive_msg_bias:
                    break
                else:
                    print('.', end='')
                    time.sleep(0.01)
            self.receive_msg_bias = False
            print('o', end='')
            bias_indx += 1
        print('  done')
        self.done_bias_setting = True

    def set_bias_single_auto(self, bias_name):
        selected_bias = self._TEMP_BIAS_DICT[self.receive_serial_num][self.selcted_temp_range]
        bias_val = selected_bias[bias_name]
        self.send_data(
            "WRITE", 
            self._ADDRESS_DICT['BIAS'], 
            self._REGISTER_DICT[bias_name], bias_val)
        return bias_val

    def set_bias_single_manual(self, bias_name, bias_value):
        bit=16
        if bias_name == 'CINT':
            bit = 8
        self.send_data("WRITE", self._ADDRESS_DICT['BIAS'], self._REGISTER_DICT[bias_name], int(bias_value, 16))
        
    def get_current_temperature_interval(self):
        return self.TEMP_INFO_DICT[self.receive_serial_num][self.selcted_temp_range]

    # def check_shutter_action_type(self):
    #     if self.receive_serial_num is None:
    #         return
    #     type2_list = ['000000002', '000000003']
    #     type3_list = ['000000000', '522414128', '000000001', '526403014', '521224015']
    #     if self.receive_serial_num in type2_list:
    #         self.shutter_type = 2
    #     elif self.receive_serial_num in type3_list:
    #         self.shutter_type = 3
    #     else:
    #         self.shutter_type = 1

    def shutter_toggle(self, command_status=None):
        self.send_msg_shutter = False
        if not self.send_msg_shutter:
            if command_status is not None:
                if 'close' in command_status or 'CLOSE' in command_status:
                    self.send_data('OPERATE_CTRL', 'SHUTTER', 'WRITE', 0)
                    self.receive_data('SHUTTER')
                elif 'open' in command_status or 'OPEN' in command_status:
                    self.send_data('OPERATE_CTRL', 'SHUTTER', 'WRITE', 1)
                    self.receive_data('SHUTTER')
            self.send_msg_shutter = True
        if self.receive_shutter_status is not None:
            if 'OPEN' in self.receive_shutter_status or 'CLOSE' in self.receive_shutter_status:
                return self.receive_shutter_status




