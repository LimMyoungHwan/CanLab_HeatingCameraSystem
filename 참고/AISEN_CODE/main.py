import tkinter as tk
from tkinter import ttk, filedialog
from tkinter.scrolledtext import ScrolledText
import serial
import threading
import cv2
import glob
import numpy as np
import time
import os
import sys
import traceback
import json
import atexit
import re
from PIL import Image, ImageTk
from matplotlib.figure import Figure
from concurrent.futures import ThreadPoolExecutor
from matplotlib.backends.backend_tkagg import FigureCanvasTkAgg
from datetime import datetime
from serial_code import SerialComm
from biassetting import setting
from serial_rw import SerialReadWrite
from datetime import datetime
from two_point_viewer import TECLESS


ANSI_ESCAPE_PATTERN = re.compile(r"\x1B\[[0-9;]*m")
ANSI_CODE_PATTERN = re.compile(r"\x1B\[([0-9;]*?)m")
ANSI_COLOR_TAG_MAP = {
    "31": "error",
}


def strip_ansi_codes(text: str) -> str:
    if not text:
        return text
    return ANSI_ESCAPE_PATTERN.sub("", text)


def parse_ansi_segments(text: str):
    if not text:
        return []
    segments = []
    last_index = 0
    current_tag = None
    for match in ANSI_CODE_PATTERN.finditer(text):
        start, end = match.span()
        if start > last_index:
            segments.append((text[last_index:start], current_tag))
        codes = match.group(1).split(";") if match.group(1) else []
        if not codes or "0" in codes:
            current_tag = None
        else:
            for code in codes:
                tag = ANSI_COLOR_TAG_MAP.get(code)
                if tag:
                    current_tag = tag
                    break
        last_index = end
    if last_index < len(text):
        segments.append((text[last_index:], current_tag))
    return segments


class AnsiStrippedStream:
    def __init__(self, stream):
        self.stream = stream

    def write(self, data):
        if not data:
            return 0
        cleaned = strip_ansi_codes(data)
        self.stream.write(cleaned)
        return len(data)

    def flush(self):
        self.stream.flush()


class TimestampedStream:
    def __init__(self, stream):
        self.stream = stream
        self._at_line_start = True

    def write(self, data):
        if not data:
            return 0
        text = data
        pieces = []
        idx = 0
        while idx < len(text):
            if self._at_line_start:
                pieces.append(datetime.now().strftime("[%Y-%m-%d %H:%M:%S] "))
                self._at_line_start = False
            newline_idx = text.find("\n", idx)
            if newline_idx == -1:
                pieces.append(text[idx:])
                break
            pieces.append(text[idx:newline_idx + 1])
            idx = newline_idx + 1
            self._at_line_start = True
        composed = "".join(pieces)
        self.stream.write(composed)
        return len(data)

    def flush(self):
        self.stream.flush()


class ConsoleTee:
    def __init__(self, *streams):
        self.streams = streams

    def write(self, data):
        for stream in self.streams:
            stream.write(data)
        self.flush()
        return len(data)

    def flush(self):
        for stream in self.streams:
            stream.flush()

class GuiConsoleStream:
    def __init__(self, text_widget):
        self.text_widget = text_widget
        self.text_widget.tag_config("error", foreground="red")

    def write(self, data):
        if not data:
            return
        segments = parse_ansi_segments(data)
        if not segments:
            return
        self.text_widget.after(0, self._append_segments, segments)

    def _append_segments(self, segments):
        self.text_widget.config(state="normal")
        for text, tag in segments:
            if not text:
                continue
            if tag:
                self.text_widget.insert(tk.END, text, tag)
            else:
                self.text_widget.insert(tk.END, text)
        self.text_widget.see(tk.END)
        self.text_widget.config(state="disabled")

    def flush(self):
        pass


def log_error(message: str) -> None:
    """Print error messages in red for easier visibility."""
    print(f"\033[31m{message}\033[0m")

class custom:
    def __init__(self, root):
        self.save_dir = None
        self.histogram_avg = 0
        self.camera_threads = {}
        self.camera_running = {}
        self.latest_frames = {}
        self.latest_frame_lock = threading.Lock()
        self.histogram_thread = None
        self.histogram_running = False
        self.histogram_cam_id = None
        self.histogram_window = None
        self.histogram_figure = None
        self.histogram_axis = None
        self.histogram_canvas = None
        self.histogram_line = None
        self.bias_dict = {}     
        self.root = root
        self.root.grid_rowconfigure(0, weight=1)
        self.root.grid_columnconfigure(0, weight=3)
        self.root.grid_columnconfigure(1, weight=1)
        self.setting = setting(app=self)
        self.camera = {}
        self.auto_bias = False
        self.serial_connections = {}
        # self.serial_save_list = {}
        self.shutter_ok = False
        self.checksnf = False
        self.camera_folder = None
        self.C_index = 0
        self.serial_poll_interval = 1.0  # seconds
        self.viewer_window = None
        self.viewer_app = None
        self.connected_cameras = []
        self.port_list = SerialComm().get_available_ports()
        # self.executor = ThreadPoolExecutor(max_workers=9)
        self.camera_widgets = {}
        self.camera_states = {}
        self.main_frame = tk.Frame(self.root, bg="black")
        self.main_frame.grid(row=0, column=0, sticky="nsew")
        self.control_frame = tk.Frame(self.root)
        self.control_frame.grid(row=0, column=1, sticky="nsew")
        for idx in range(3):
            self.main_frame.rowconfigure(idx, weight=1)
            self.main_frame.columnconfigure(idx, weight=1)
        self.control_frame.rowconfigure(9, weight=1)
        for col in range(4):
            self.control_frame.columnconfigure(col, weight=1)
        # 오른쪽 제어 영역 버튼을 보기 좋게 키우기 위한 기본 스타일
        self.button_style = ttk.Style(self.root)
        self.button_style.configure("TButton", font=("Arial", 12), padding=(9, 6))
        # self.cam_index_list = list(range(len(self.port_list)))
        self.cam_index_list = [0, 1, 2, 3, 4, 5, 6, 7, 8]
        self.frame_index_list = ["0","1","2","3","4","5","6","7","8"]
        # self.frame_index_list = [0, 1, 2, 3, 4, 5, 6, 7, 8]
        self.cam_index_label = tk.Label(self.control_frame, text="Camera ID:", font=("Arial", 12))
        self.cam_index_label.grid(row=0, column=0, padx=5, pady=5, sticky="w")
        self.frame_index = tk.StringVar()
        self.cam_combo = ttk.Combobox(self.control_frame, textvariable=self.frame_index, values=self.frame_index_list)
        self.cam_combo.grid(row=0, column=1, padx=5, pady=5, sticky="w")    
        self.cam_combo.current(0)
        self.cam_combo.bind("<<ComboboxSelected>>", self._on_frame_index_change)
        self._on_frame_index_change()
        self.cam_button = ttk.Button(
            self.control_frame,
            text="Connect",
            command=lambda: self._run_with_button_lock(self.cam_button, self.check_frame_num),
        )
        self.cam_button.grid(row=1, column=0, padx=5, pady=5, sticky="w")
        self.cam_disconnect_button = ttk.Button(
            self.control_frame,
            text="Disconnect",
            command=lambda: self._run_with_button_lock(
                self.cam_disconnect_button, self.disconnect_selected_camera
            ),
        )
        self.cam_disconnect_button.grid(row=1, column=1, padx=5, pady=5, sticky="w")
        self.port_refresh_button = ttk.Button(
            self.control_frame,
            text="UPDATE",
            command=lambda: self._run_with_button_lock(
                self.port_refresh_button, self.refresh_available_ports
            ),
        )
        self.port_refresh_button.grid(row=0, column=2, padx=5, pady=5, sticky="w")
        self.shutter_button = ttk.Button(
            self.control_frame,
            text="Shutter",
            command=lambda: self._run_with_button_lock(self.shutter_button, self.shutter_switch),
        )
        self.shutter_button.grid(row=1, column=2, padx=5, pady=5, sticky="w")
        self.save_button = ttk.Button(
            self.control_frame,
            text="저 장",
            command=lambda: self._run_with_button_lock(self.save_button, self.save_rawfile),
        )
        self.save_button.grid(row=8, column=1, padx=5, pady=5, sticky="w")
        self.validation_button = ttk.Button(
            self.control_frame,
            text="검 증",
            command=lambda: self._run_with_button_lock(
                self.validation_button, self.validation_check
            ),
        )
        self.validation_button.grid(row=8, column=0, padx=5, pady=5, sticky="w")
        self.set_save_dir_button = ttk.Button(
            self.control_frame,
            text="저장 폴더 변경",
            command=lambda: self._run_with_button_lock(
                self.set_save_dir_button, self.select_save_dir
            ),
        )
        self.set_save_dir_button.grid(row=8, column=2, padx=5, pady=5, sticky="w")
        self.bias_button = ttk.Button(
            self.control_frame,
            text="자동 바이어스",
            command=self._run_auto_bias_button_clicked,
        )
        self.bias_button.grid(row=5, column=0, padx=5, pady=5, sticky="w")
        self.bias_read_button = ttk.Button(
            self.control_frame,
            text="개별 설정값 불러오기",
            command=lambda: self._run_with_button_lock(
                self.bias_read_button, lambda: self.bias_read(self.C_index)
            ),
        )
        self.bias_read_button.grid(row=5, column=1, padx=5, pady=5, sticky="w")
        self.all_bias_read_button = ttk.Button(
            self.control_frame,
            text="전체 설정값 불러오기",
            command=lambda: self._run_with_button_lock(
                self.all_bias_read_button, self.all_bias_read
            ),
        )
        self.all_bias_read_button.grid(row=5, column=2, padx=5, pady=5, sticky="w")
        self.bias_check_button = ttk.Button(
            self.control_frame,
            text="현재 설정값",
            command=lambda: self._run_with_button_lock(
                self.bias_check_button, lambda: self.bias_check(self.C_index)
            ),
        )
        self.bias_check_button.grid(row=5, column=3, padx=5, pady=5, sticky="w")

        self.temp_range_label = tk.Label(self.control_frame, text="촬영온도 구간", font=("Arial", 12))
        self.temp_range_label.grid(row=3, column=0, padx=5, pady=5, sticky="w")

        self.tmp_range_list = ["LOW","MID","HIGH"]
        self.selected_temp_range = tk.StringVar()
        self.selected_temp_range_comdo = ttk.Combobox(self.control_frame, textvariable=self.selected_temp_range, values=self.tmp_range_list, state="readonly",width=10)
        self.selected_temp_range_comdo.grid(row=4, column=0, padx=5, pady=5,sticky="w")
        self.selected_temp_range.set("구간")
        #챔버 온도 리스트 생성 및 선택값 변수로 저장
        self.cham_range_label = tk.Label(self.control_frame, text="챔버 촬영 온도", font=("Arial", 12))
        self.cham_range_label.grid(row=3, column=1, padx=5, pady=5, sticky="w")

        self.chamber_tmp_list = ["-30","-10","10","25","40","55","70",]
        self.selected_chamber_temp = tk.StringVar()
        self.chamber_tmp_combo = ttk.Combobox(self.control_frame, textvariable=self.selected_chamber_temp, values=self.chamber_tmp_list, state="readonly",width=10)
        self.chamber_tmp_combo.grid(row=4, column=1, padx=5, pady=5,sticky="w")
        self.chamber_tmp_combo.set("챔버")

        self.bb_range_label = tk.Label(self.control_frame, text="블랙바디 촬영온도", font=("Arial", 12))
        self.bb_range_label.grid(row=3, column=2, padx=5, pady=5, sticky="w")

        #흑체 온도 리스트 생성 및 선택값 변수로 저장
        self.bb_temperature = ["cold","room","hot"]
        self.BB_tmp_list = ["10","20","room","70","80","shutter"]
        self.selected_BB_temp = tk.StringVar()
        self.BB_tmp_combo = ttk.Combobox(self.control_frame, textvariable=self.selected_BB_temp, values=self.bb_temperature, state="readonly",width=10)
        self.BB_tmp_combo.grid(row=4, column=2, padx=5, pady=5,sticky="w")
        self.BB_tmp_combo.set("블랙바디")
        self.log_console = ScrolledText(
            self.control_frame, width=70, height=25, state="disabled", wrap="word"
        )
        self.log_console.grid(row=6, column=0, columnspan=4, padx=5, pady=10, sticky="we")
        self.save_dir_var = tk.StringVar(value="Save Dir: (not selected)")
        self.save_dir_label = tk.Label(
            self.control_frame, textvariable=self.save_dir_var, font=("Arial", 10), anchor="w", justify="left", wraplength=520,
        )
        self.save_dir_label.grid(row=9, column=0, columnspan=4, padx=5, pady=5, sticky="we")

        self.histogram_button = ttk.Button(
            self.control_frame,
            text="Show Histogram",
            command=lambda: self._run_with_button_lock(
                self.histogram_button, self.show_histogram
            ),
        )
        self.histogram_button.grid(row=8, column=3, padx=5, pady=5, sticky="w")


        for i in self.cam_index_list:
            row = i // 3
            column = i % 3
            self.create_camera_panel(row=row, column=column, cam_id=i)
        # Tkinter 종료 이벤트 처리
        self.root.protocol("WM_DELETE_WINDOW", self.on_close)

    def _run_with_button_lock(self, button: ttk.Button, callback) -> None:
        if "disabled" in button.state():
            return
        button.state(["disabled"])
        try:
            callback()
        finally:
            if button.winfo_exists():
                button.state(["!disabled"])

    def _run_auto_bias_button_clicked(self) -> None:
        button = self.bias_button
        if "disabled" in button.state():
            return
        button.state(["disabled"])

        def _on_complete():
            if button.winfo_exists():
                button.state(["!disabled"])

        try:
            self.auto_bias_setting(self.connected_cameras, on_complete=_on_complete)
        except Exception:
            _on_complete()
            raise

    def show_histogram(self):

        cam_id = self._get_selected_cam_id()

        if cam_id is None:
            log_error("No camera selected for histogram display.")
            return
        camera = self.camera.get(cam_id)
        if not camera or not camera.isOpened():
            log_error(f"Camera {cam_id} is not connected for histogram display.")
            return
        if (
            self.histogram_running
            and self.histogram_cam_id == cam_id
            and self.histogram_thread
            and self.histogram_thread.is_alive()
        ):
            if self.histogram_window and self.histogram_window.winfo_exists():
                self.histogram_window.lift()
                self.histogram_window.focus_force()
            return
        self._stop_histogram_thread()
        self._ensure_histogram_window()
        self.histogram_running = True
        self.histogram_cam_id = cam_id
        self.histogram_thread = threading.Thread(
            target=self._histogram_loop, args=(cam_id,), daemon=True
        )
        self.histogram_thread.start()

    def _ensure_histogram_window(self):
        if self.histogram_window and self.histogram_window.winfo_exists():
            return
        self.histogram_window = tk.Toplevel(self.root)
        self.histogram_window.title("Histogram")
        self.histogram_window.geometry("900x420")
        self.histogram_window.protocol("WM_DELETE_WINDOW", self._close_histogram_window)
        self.histogram_figure = Figure(figsize=(9, 4), dpi=100)
        self.histogram_axis = self.histogram_figure.add_subplot(111)
        x_axis = np.arange(16384)
        (self.histogram_line,) = self.histogram_axis.plot(x_axis, np.zeros(16384), linewidth=0.8)
        self.histogram_axis.set_xlim(0, 16383)
        self.histogram_axis.set_ylim(0, 1)
        self.histogram_axis.set_xlabel("Pixel Intensity")
        self.histogram_axis.set_ylabel("Frequency")
        self.histogram_axis.grid(True, alpha=0.3)
        self.histogram_canvas = FigureCanvasTkAgg(self.histogram_figure, master=self.histogram_window)
        self.histogram_canvas.get_tk_widget().pack(fill=tk.BOTH, expand=True)
        self.histogram_canvas.draw_idle()

    def _histogram_loop(self, cam_id: int) -> None:
        while self.histogram_running and self.histogram_cam_id == cam_id:
            camera = self.camera.get(cam_id)
            if not camera or not camera.isOpened():
                break
            with self.latest_frame_lock:
                frame = self.latest_frames.get(cam_id)
                frame_copy = None if frame is None else frame.copy()
            if frame_copy is None:
                time.sleep(0.05)
                continue
            hist = np.bincount(frame_copy.ravel(), minlength=16384)
            self.root.after(0, self._render_histogram_canvas, cam_id, hist)
            time.sleep(0.1)
        self.root.after(0, self._on_histogram_loop_end, cam_id)

    def _on_histogram_loop_end(self, cam_id: int) -> None:
        if self.histogram_cam_id != cam_id:
            return
        if self.histogram_thread and self.histogram_thread.is_alive():
            return
        self.histogram_running = False
        self.histogram_thread = None

    def _render_histogram_canvas(self, cam_id: int, hist: np.ndarray) -> None:
        if not self.histogram_running or self.histogram_cam_id != cam_id:
            return
        if not self.histogram_window or not self.histogram_window.winfo_exists():
            return
        if self.histogram_line is None or self.histogram_axis is None or self.histogram_canvas is None:
            return
        self.histogram_line.set_ydata(hist)
        y_max = max(1, int(hist.max() * 1.05))
        self.histogram_axis.set_ylim(0, y_max)
        self.histogram_axis.set_title(f"Histogram for Camera {cam_id}")
        self.histogram_canvas.draw_idle()

    def _save_histogram_png(self, frame_id: int, frame: np.ndarray, output_path: str) -> None:
        frame = frame & 0x3FFF
        hist = np.bincount(frame.ravel(), minlength=16384)
        x_axis = np.arange(16384)

        figure = Figure(figsize=(9, 4), dpi=100)
        axis = figure.add_subplot(111)
        axis.plot(x_axis, hist, linewidth=0.8)
        axis.set_xlim(0, 16383)
        axis.set_ylim(0, max(1, int(hist.max() * 1.05)))
        axis.set_xlabel("Pixel Intensity")
        axis.set_ylabel("Frequency")
        axis.set_title(f"Histogram for Camera {frame_id}")
        axis.grid(True, alpha=0.3)
        figure.tight_layout()
        figure.savefig(output_path, dpi=100)

    def _stop_histogram_thread(self):
        self.histogram_running = False
        thread = self.histogram_thread
        if thread and thread.is_alive() and thread is not threading.current_thread():
            thread.join(timeout=0.5)
        self.histogram_thread = None
        self.histogram_cam_id = None

    def _close_histogram_window(self):
        self._stop_histogram_thread()
        if self.histogram_window and self.histogram_window.winfo_exists():
            self.histogram_window.destroy()
        self.histogram_window = None
        self.histogram_figure = None
        self.histogram_axis = None
        self.histogram_canvas = None
        self.histogram_line = None

    def refresh_available_ports(self):
        """Refresh the cached port list and update all port selection boxes."""
        updated_ports = SerialComm().get_available_ports()
        self.port_list = updated_ports
        for widgets in self.camera_widgets.values():
            port_combo = widgets.get("port_combo")
            selected_port_var = widgets.get("selected_port")
            if not port_combo or not selected_port_var:
                continue
            current_value = selected_port_var.get()
            port_combo["values"] = self.port_list
            if current_value in self.port_list:
                port_combo.set(current_value)
            elif self.port_list:
                port_combo.current(0)
            else:
                port_combo.set("")

    def create_camera_panel(self, row: int, column: int, cam_id: int) -> None:
        frame = tk.Frame(self.main_frame)
        frame.grid(row=row, column=column, padx=5, pady=5, sticky="nsew")
        for col in range(3):
            frame.columnconfigure(col, weight=1)
        selected_port = tk.StringVar()
        port_combo = ttk.Combobox(frame,textvariable=selected_port,values=self.port_list,state="readonly")
        port_combo.grid(row=0, column=0, padx=5, pady=5, sticky="w")
        if self.port_list:
            port_combo.current(0)
        serial_connect_button = ttk.Button(
            frame,
            text="Serial Connect",
            command=lambda fid=cam_id: self.connect_serial_for_camera(fid),
        )
        serial_connect_button.grid(row=0, column=1, padx=5, pady=5, sticky="w")
        serial_disconnect_button = ttk.Button(
            frame,
            text="Serial Disconnect",
            command=lambda fid=cam_id: self.disconnect_serial_for_camera(fid),
        )
        serial_disconnect_button.grid(row=0, column=2, padx=5, pady=5, sticky="w")

        cam_label = tk.Label(frame, text=f"Camera ID: {cam_id}", font=("Arial", 12))
        cam_label.grid(row=1, column=0, columnspan=3, padx=5, pady=5, sticky="w")
        camera_index = cam_id
        self.camera[camera_index] = None

        hist_label = tk.Label(frame, text="Frame Center", font=("Arial", 12))
        hist_label.grid(row=2, column=0, padx=5, pady=5, sticky="w")
        hist_value = 0
        hist_value_label = tk.Label(frame, text=hist_value, font=("Arial", 12))
        hist_value_label.grid(row=2, column=1, padx=5, pady=5, sticky="w")
        fpa_label = tk.Label(frame, text="FPA Temp", font=("Arial", 12))
        fpa_label.grid(row=3, column=0, padx=5, pady=5, sticky="w")
        fpa_value = 0
        fpa_value_label = tk.Label(frame, text=fpa_value, font=("Arial", 12))
        fpa_value_label.grid(row=3, column=1, padx=5, pady=5, sticky="w")
        shutter_label = tk.Label(frame, text="Shutter Action", font=("Arial", 12))
        shutter_label.grid(row=4, column=0, padx=5, pady=5, sticky="w")
        shutter_value = "-"
        shutter_value_label = tk.Label(frame, text=shutter_value, font=("Arial", 12))
        shutter_value_label.grid(row=4, column=1, padx=5, pady=5, sticky="w")

        serial_label = tk.Label(frame, text="Serial_Num", font=("Arial", 12))
        serial_label.grid(row=6, column=0, padx=5, pady=5, sticky="w")
        serial_value = "-"
        serial_num_value = tk.Label(frame, text=serial_value, font=("Arial", 12))
        serial_num_value.grid(row=6, column=1, padx=5, pady=5, sticky="w")

        product_label = tk.Label(frame, text="Product_Info", font=("Arial", 12))
        product_label.grid(row=7, column=0, padx=5, pady=5, sticky="w")
        product_value = tk.Entry(frame, font=("Arial", 12))
        product_value.grid(row=7, column=1, padx=5, pady=5, sticky="w")

        self.camera_widgets[camera_index] = {
            "frame": frame,
            "selected_port": selected_port,
            "port_combo": port_combo,
            "serial_connect_button": serial_connect_button,
            "serial_disconnect_button": serial_disconnect_button,
            "cam_index": cam_id,
            "cam_label": cam_label,
            "hist_label": hist_label,
            "hist_value": hist_value_label,
            "fpa_value_label": fpa_value_label,
            "shutter_label": shutter_label,
            "shutter_value": shutter_value_label,
            "serial_value_label": serial_num_value,
            "product_value": product_value,
        }

    def update_save_dir_label(self):
        if self.save_dir:
            self.save_dir_var.set(f"Save Dir: {self.save_dir}")
        else:
            self.save_dir_var.set("Save Dir: (not selected)")

    def select_save_dir(self):
        initial_dir = self.save_dir if self.save_dir and os.path.isdir(self.save_dir) else os.getcwd()
        selected_dir = filedialog.askdirectory(
            title="Select Save Directory",
            initialdir=initial_dir,
        )
        if selected_dir:
            self.save_dir = selected_dir
            self.update_save_dir_label()

    def check_frame_num(self):
        cam_id = self._get_selected_cam_id()
        if cam_id is None:
            return
        self.connected_cameras.append(cam_id)
        self.start_camera_stream(cam_id)

    def disconnect_selected_camera(self):
        cam_id = self._get_selected_cam_id()
        if cam_id is None:
            return
        try:
            self.connected_cameras.remove(cam_id)
        except ValueError as e:
            log_error(f"Camera {cam_id} was not in the connected cameras list: {e}")
            
        self.stop_camera_stream(cam_id)

    def connect_serial_for_camera(self, frame_id: int) -> None:
        widgets = self.camera_widgets.get(frame_id)
        if not widgets:
            log_error("Could not find widgets for the selected frame.")
            return
        selected_port = widgets["selected_port"].get()
        if not selected_port:
            log_error("Select a port to connect.")
            return
        if frame_id in self.serial_connections:
            log_error(f"Frame {frame_id} already has a serial connection. Reinitializing it.")
            self.disconnect_serial_for_camera(frame_id)
        serial_comm = SerialReadWrite()
        try:
            serial_comm.initialize(selected_port)
        except serial.SerialException as e:
            log_error(f"Failed to connect to serial port: {e}")
            return
        if not serial_comm.serial or not serial_comm.serial.is_open:
            log_error("Unable to open serial port.")
            return
        conn_state = {
            "serial_comm": serial_comm,
            "port": selected_port,
            "fpa_temp": 0.0,
            "fpa_raw": 0,
            "serial_num": "-",
            "shutter_value": 0,
            "stop_event": threading.Event(),
            "poll_thread": None,
        }
        self.serial_connections[frame_id] = conn_state
        widgets["serial_value_label"].config(text="-")
        widgets["fpa_value_label"].config(text="0")
        self.start_connection_polling(frame_id)

    def disconnect_serial_for_camera(self, frame_id: int) -> None:
        widgets = self.camera_widgets.get(frame_id)
        if not widgets:
            log_error("Could not find widgets for the selected frame.")
            return
        conn = self.serial_connections.get(frame_id)
        if not conn:
            log_error("No serial connection exists for this frame.")
            widgets["serial_value_label"].config(text="-")
            widgets["fpa_value_label"].config(text="0")
            return
        self.stop_connection_polling(frame_id)
        serial_comm = conn.get("serial_comm")
        if serial_comm:
            serial_comm.running = False
        if serial_comm and getattr(serial_comm, "serial", None):
            try:
                if serial_comm.serial.is_open:
                    serial_comm.serial.close()
            except serial.SerialException as e:
                log_error(f"Failed to close serial port: {e}")
        self.serial_connections.pop(frame_id, None)
        widgets["serial_value_label"].config(text="-")
        widgets["fpa_value_label"].config(text="0")

    def _on_frame_index_change(self, event=None):
        selected_index = self.cam_combo.current()
        if selected_index == -1:
            return
        self.C_index = selected_index

    def _get_selected_cam_id(self):
        selected_index = self.cam_combo.current()
        if selected_index == -1:
            selected_index = 0
        self.C_index = selected_index
        widgets = self.camera_widgets.get(self.C_index)
        if not widgets:
            log_error("Could not find widgets for the selected frame.")
            return None
        cam_value = widgets.get("cam_index")
        if cam_value is None:
            log_error("Invalid camera index.")
            return None
        try:
            cam_id = int(cam_value)
        except (TypeError, ValueError):
            log_error("Invalid camera index.")
            return None
        return cam_id
    
    def get_serial_connection(self, frame_id: int = None):
        if frame_id is None:
            frame_id = self.C_index
        return self.serial_connections.get(frame_id)
    
    def _get_serial_device(self, frame_id: int = None):
        conn = self.get_serial_connection(frame_id)
        if not conn:
            log_error("No serial connection is available.")
            return None, None
        serial_comm = conn.get("serial_comm")
        serial_obj = getattr(serial_comm, "serial", None) if serial_comm else None
        if not serial_comm or not serial_obj or not serial_obj.is_open:
            log_error("Serial port is not open.")
            return None, None
        return conn, serial_comm
    
    def _extract_payload_byte(self, packet: str) -> int:
        if not packet or len(packet) < 2:
            raise ValueError("Empty packet received from the serial interface.")
        return int(packet[-2:], 16)
    
    def _update_shutter_status(self, frame_id: int, status_text: str) -> None:
        widgets = self.camera_widgets.get(frame_id)
        if widgets:
            shutter_label = widgets.get("shutter_value")
            if shutter_label:
                shutter_label.after(0, lambda value=status_text, label=shutter_label: label.config(text=value))

    def _send_shutter_command(self, frame_id: int, data: int, status_text: str) -> bool:
        conn, serial_comm = self._get_serial_device(frame_id)
        if not serial_comm:
            return False
        try:
            serial_comm.send_data("OPERATE_CTRL", "SHUTTER", "WRITE", data)
            serial_comm.receive_data("SHUTTER")
        except Exception as exc:
            log_error(f"Failed to send shutter command: {exc}")
            return False
        conn["shutter_value"] = data
        self._update_shutter_status(frame_id, status_text)
        return True
    
    def get_current_serial_number(self, frame_id: int = None):
        conn = self.get_serial_connection(frame_id)
        if conn:
            return conn.get("serial_num")
        return None
    
    def start_connection_polling(self, frame_id: int) -> None:
        self.stop_connection_polling(frame_id)
        conn = self.serial_connections.get(frame_id)
        if not conn:
            return
        stop_event = conn["stop_event"]
        stop_event.clear()
        thread = threading.Thread(target=self._serial_polling_loop, args=(frame_id,), daemon=True)
        conn["poll_thread"] = thread
        thread.start()

    def stop_connection_polling(self, frame_id: int) -> None:
        conn = self.serial_connections.get(frame_id)
        if not conn:
            return
        stop_event = conn.get("stop_event")
        if stop_event:
            stop_event.set()
        poll_thread = conn.get("poll_thread")
        if poll_thread and poll_thread.is_alive():
            poll_thread.join(timeout=0.5)
        conn["poll_thread"] = None

    def _serial_polling_loop(self, frame_id: int) -> None:
        while True:
            conn = self.serial_connections.get(frame_id)
            if not conn:
                break
            stop_event = conn["stop_event"]
            if stop_event.is_set():
                break
            serial_comm = conn["serial_comm"]
            serial_obj = getattr(serial_comm, "serial", None)
            if not serial_obj or not serial_obj.is_open:
                break
            fpa_value, _ = self.get_fpa(frame_id=frame_id)
            serial_number = self.read_serial(frame_id=frame_id)
            # serial_number = "test"
            if stop_event.is_set() or frame_id not in self.serial_connections:
                break
            widgets = self.camera_widgets.get(frame_id)
            if widgets:
                fpa_label = widgets["fpa_value_label"]
                serial_label = widgets["serial_value_label"]
                if fpa_value is not None:
                    fpa_label.after(
                        0, lambda value=fpa_value, label=fpa_label: label.config(text=f"{value:.2f}")
                    )
                if serial_number:
                    serial_label.after(
                        0, lambda value=serial_number, label=serial_label: label.config(text=str(value))
                    )
            if stop_event.wait(self.serial_poll_interval):
                break

    def on_close(self):
        """Gracefully tear down the Tkinter UI."""
        print("UI shutdown requested. Cleaning up resources...")
        self.running = False
        self._close_histogram_window()
        self._close_two_point_viewer()
        for frame_id in list(self.serial_connections.keys()):
            self.disconnect_serial_for_camera(frame_id)
        self.root.quit()
        self.root.destroy()

    def start_camera_stream(self, cam_id: int) -> None:
        cam_state = self.camera_states.setdefault(cam_id, {"serial": False, "camera": False})
        if cam_state["camera"]:
            log_error(f"Camera {cam_id} is already connected.")
            return
        self.detect_camera(cam_id)
        cam_state["camera"] = True

    def stop_camera_stream(self, cam_id: int) -> None:
        cam_state = self.camera_states.setdefault(cam_id, {"serial": False, "camera": False})
        if not cam_state["camera"]:
            log_error(f"Camera {cam_id} is not connected.")
            return
        self.disconnect_camera(cam_id)
        cam_state["camera"] = False

    def detect_camera(self, cam_id):
        index = int(cam_id)
        if self.camera.get(index):
            self.stop_streaming(index)
            time.sleep(0.5)
        cap = cv2.VideoCapture(index, cv2.CAP_DSHOW)
        if cap is None or not cap.isOpened():
            log_error(f"Camera {index} could not be opened.")
            cap.release()
            return
        cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter.fourcc('Y', '1', '6', ' '))
        cap.set(cv2.CAP_PROP_CONVERT_RGB, 0)
        self.camera[index] = cap
        print(f"Camera {index} connected.")
        self.start_streaming(index)

    def disconnect_camera(self,cam_id):
        self.stop_streaming(cam_id)
        print(f"Camera {cam_id} disconnected.")

    def start_streaming(self, index):
        if self.camera[index] is None or not self.camera[index].isOpened():
            log_error(f"Camera_num{index} is not connected.")
            return
        if self.camera_running.get(index):
            return
        self.camera_running[index] = True
        thread = threading.Thread(
            target=self.update_camera, args=(index,), daemon=True
        )
        self.camera_threads[index] = thread
        thread.start()

    def stop_streaming(self, cam_id):
        if not self.camera_running.get(cam_id):
            return
        self.camera_running[cam_id] = False
        thread = self.camera_threads.get(cam_id)
        cap = self.camera.get(cam_id)
        if cap and cap.isOpened():
            time.sleep(0.1)
            cap.release()
        self.camera[cam_id] = None
        if thread and thread.is_alive():
            thread.join(timeout=0.5)
        with self.latest_frame_lock:
            self.latest_frames.pop(cam_id, None)
        if self.histogram_cam_id == cam_id:
            self._stop_histogram_thread()
        self.camera_threads.pop(cam_id, None)
        self.camera_running.pop(cam_id, None)

    def shutter_switch(self):
        self._on_frame_index_change()
        if self.shutter_ok:
            if self.shutter_close(frame_id=self.C_index):
                print(f"{self.C_index}_Camera_Shutter closed")
                self.shutter_ok = False
        else:
            if self.shutter_open(frame_id=self.C_index):
                print(f"{self.C_index}_Camera_Shutter opened") 
                self.shutter_ok = True

    def update_camera(self, index):
        while self.camera_running.get(index) and self.camera[index].isOpened():
            ret, frame = self.camera[index].read()
            if not ret:
                log_error(f"카메라 {index}: 프레임을 읽는 데 실패했습니다.")
                break

            # 프레임 데이터는 14비트(0-16383)를 사용합니다.
            frame = frame & 0x3FFF

            if frame is None or frame.size == 0:
                log_error("ERROR: 프레임이 비어 있어 중앙값을 계산할 수 없습니다.")
                continue
            with self.latest_frame_lock:
                self.latest_frames[index] = frame.copy()

            # 1. 히스토그램 계산
            # 14비트 데이터이므로 16384개의 bin을 사용합니다.
            hist, bin_edges = np.histogram(frame, bins=16384, range=(0, 16383))

            # 2. 히스토그램에서 중앙값 찾기
            # 누적 분포 함수(CDF) 계산
            cdf = np.cumsum(hist)
            
            # 전체 픽셀 수의 50% 지점 찾기
            median_point = cdf[-1] / 2
            
            # 50% 지점을 넘는 첫 번째 bin의 인덱스(밝기 값)를 찾습니다.
            # np.searchsorted는 정렬된 배열에서 값이 삽입될 위치를 찾아줍니다.
            median_value = np.searchsorted(cdf, median_point)

            # UI 업데이트
            widgets = self.camera_widgets.get(index)
            if not widgets:
                continue
            
            hist_label = widgets["hist_value"]
            # UI가 스레드-세이프하지 않으므로 after()를 사용해 메인 스레드에서 업데이트합니다.
            hist_label.after(0, lambda value=median_value, label=hist_label: label.config(text=f"{value}"))
        
    def save_rawfile(self):
        self.serial_save_list = {}
        for i in self.connected_cameras:
            print("Save start camera_num:", i)
            frame_id = i
            camera_name = self.get_current_serial_number(frame_id) or "UNKNOWN"
            self.serial_save_list.update({frame_id : camera_name})
        print(self.serial_save_list)

        for k, v in self.serial_save_list.items():

            temp_range = self.selected_temp_range.get()
            chamber = self.selected_chamber_temp.get()
            blackbody = self.selected_BB_temp.get()
            product_name = self.camera_widgets[k]["product_value"].get().strip() or "UNKNOWN"
            
            if product_name == "UNKNOWN":
                camera_product = v
            else:
                camera_product = v+"_"+product_name
            # now = datetime.now()
            # formatted_today = now.strftime("%Y%m%d")

            if temp_range == "LOW" and  blackbody == "cold":
                bb = "10"
            elif temp_range == "LOW" and  blackbody == "room":
                bb = "room"
            elif temp_range == "LOW" and  blackbody == "hot":
                bb = "70"
            
            if temp_range == "MID" and  blackbody == "cold":
                bb = "20"
            elif temp_range == "MID" and  blackbody == "room":
                bb = "room"
            elif temp_range == "MID" and  blackbody == "hot":
                bb = "80"
            
            if temp_range == "HIGH" and  blackbody == "cold":
                bb = "20"
            elif temp_range == "HIGH" and  blackbody == "room":
                bb = "room"
            elif temp_range == "HIGH" and  blackbody == "hot":
                bb = "80"
            
            Temp_dict = {
                "LOW" : "LN",
                "MID" : "RP",
                "HIGH" : "H1P",
            }
            Chamder_dict = {
                "-30": "N30",
                "-10": "N10",
                "10": "P10",
                "25": "P25",
                "40": "P40",
                "55": "P55",
                "70": "P70", 
            }
            Bb_dict = {
                "10" : "cold",
                "20" : "cold",
                "room" : "room",
                "70" : "hot",
                "80" : "hot"
            }
            camera = self.camera.get(k)
            if camera:
                if self.save_dir is None or len(self.save_dir)<= 1:
                    self.save_dir = filedialog.askdirectory(title="Select Save Directory")
                    save_dir = self.save_dir
                    self.update_save_dir_label()

                    if not self.save_dir:
                        return  # 사용자가 디렉토리 선택을 취소한 경우
                else:
                    save_dir = self.save_dir
                    self.update_save_dir_label()
                if Bb_dict.get(bb) == "room":
                    total_frames = 10
                else:
                    total_frames = 100
                folder_name = Temp_dict.get(temp_range)+ Chamder_dict.get(chamber)
                self.camera_folder = os.path.join(save_dir, camera_product, folder_name,Bb_dict.get(bb))
                if not os.path.exists(self.camera_folder):
                    os.makedirs(self.camera_folder)
                should_write_bias = bb in ("20", "10")
                should_save_histogram = bb in ("20", "10","70","80")
                bias_payload = self.bias_dict.get(k, self.bias_dict.get(str(k)))
                if should_write_bias and bias_payload is None:
                    log_error(f"[SAVE] Frame {k} bias 데이터가 없어 bias.json 저장을 건너뜁니다.")
                with self.latest_frame_lock:
                    histogram_frame = self.latest_frames.get(k)
                    histogram_frame = None if histogram_frame is None else histogram_frame.copy()
                if should_save_histogram:
                    if histogram_frame is not None:
                        hist_filename = os.path.join(self.camera_folder, f"BB{bb}_hist.png")
                        self._save_histogram_png(k, histogram_frame, hist_filename)
                    else:
                        log_error(f"[SAVE] Frame {k} BB{bb} histogram 저장 실패: 최신 프레임이 없습니다.")
                for j in range(total_frames):
                    ret, frame = camera.read() #frame numpy ndarray / 479,640 / uint16 
                    if not ret or frame is None:
                        continue
                    frame = frame & 0x3FFF
                    frame = frame.astype(np.int16)
                    conn = self.get_serial_connection(frame_id=k)
                    cached_fpa_raw = conn.get("fpa_raw") if conn else None
                    if cached_fpa_raw is None:
                        cached_fpa_raw = 0
                    frame[0,0] = int(cached_fpa_raw)
                    fpa_frame = frame.copy() # float32 / 479,640
                    filename = os.path.join(self.camera_folder, f"BB{bb}_{j:03d}.raw")
                    fpa_frame.tofile(filename)  # .raw
                if should_write_bias and bias_payload is not None:
                    with open(os.path.join(save_dir, camera_product, folder_name, "bias.json"), "w", encoding="utf-8") as f:
                        json.dump(bias_payload, f, indent=4, ensure_ascii=False)
            print("Save completed camera_num:", k)
            
    def validation_check(self):

        """Open the two-point viewer in a separate window."""
        if self.viewer_window and self.viewer_window.winfo_exists():
            if self.viewer_app and self.save_dir and os.path.isdir(self.save_dir):
                self.viewer_app.initial_directory = self.save_dir
                if not self.viewer_app.SELECTED_DIR:
                    self.viewer_app.set_selected_directory(self.save_dir)
            self.viewer_window.lift()
            self.viewer_window.focus_force()
            return

        self.viewer_window = tk.Toplevel(self.root)
        self.viewer_window.title("Two-Point Viewer")
        self.viewer_app = TECLESS(
            self.viewer_window,
            redirect_output=False,
            initial_directory=self.save_dir,
        )
        self.viewer_window.protocol("WM_DELETE_WINDOW", self._close_two_point_viewer)

    def _close_two_point_viewer(self):
        if self.viewer_app:
            try:
                self.viewer_app.restore_output()
            except Exception:
                pass
        if self.viewer_window and self.viewer_window.winfo_exists():
            self.viewer_window.destroy()
        self.viewer_window = None
        self.viewer_app = None

    def get_fpa(self, frame_id: int = None):
        conn, serial_comm = self._get_serial_device(frame_id)
        if not serial_comm:
            cached_temp = conn.get("fpa_temp") if conn else None
            cached_raw = conn.get("fpa_raw") if conn else None
            return cached_temp, cached_raw
        try:
            serial_comm.send_data("DETECTOR", "FPA_TEMP_MSB", "READ")
            msb_packet = serial_comm.receive_data("FPA_TEMP_MSB")
            serial_comm.send_data("DETECTOR", "FPA_TEMP_LSB", "READ")
            lsb_packet = serial_comm.receive_data("FPA_TEMP_LSB")
        except Exception as exc:
            log_error(f"Failed to read FPA temperature: {exc}")
            cached_temp = conn.get("fpa_temp") if conn else None
            cached_raw = conn.get("fpa_raw") if conn else None
            return cached_temp, cached_raw
        try:
            msb = self._extract_payload_byte(msb_packet)
            lsb = self._extract_payload_byte(lsb_packet)
        except ValueError as exc:
            log_error(f"Invalid FPA temperature payload: {exc}")
            cached_temp = conn.get("fpa_temp") if conn else None
            cached_raw = conn.get("fpa_raw") if conn else None
            return cached_temp, cached_raw
        raw_value = (msb << 8) | lsb
        self.checksnf = True
        temperature, raw_temp = self.calculate_fpa_temp(f"0x{raw_value:04X}")
        conn["fpa_temp"] = temperature
        conn["fpa_raw"] = raw_temp
        return temperature, raw_temp

    def shutter_temp(self, frame_id: int = None):
        conn = self.get_serial_connection(frame_id)
        if not conn:
            log_error("No serial connection available to read the shutter state.")
            return None
        return conn.get("shutter_value")
    
    def calculate_fpa_temp(self, temp: str):
        raw_hex = temp
        if not raw_hex.startswith("0x"):
            raw_hex = "0x" + raw_hex
        raw_decimal = int(raw_hex, 16) #    
        #raw_decimal = int(raw_hex, 16) *2# 
        # 2's complement 변환
        if raw_decimal > 32767:
            raw_decimal -= 65536
        # 온도 계산
        if self.checksnf:
            scale = 4.096 #4.096                        
            max_val = 32768
            v_temp = (raw_decimal / max_val) * scale
            temperature_celsius = -188.65 * v_temp + 415.48
            self.checksnf = False
        #temperature_celsius = raw_decimal
        return temperature_celsius, raw_decimal
    
    def shutter_open(self, frame_id: int = None):
        return self._send_shutter_command(frame_id, 1, "OPEN")
    
    def shutter_close(self, frame_id: int = None):
        return self._send_shutter_command(frame_id, 0, "CLOSE")

    def read_serial(self, frame_id: int = None):
        conn, serial_comm = self._get_serial_device(frame_id)
        if not serial_comm:
            return conn.get("serial_num") if conn else None
        command_order = ["SERIAL_NB_A", "SERIAL_NB_B", "SERIAL_NB_C", "SERIAL_NB_D"]
        payload = []
        try:
            for command in command_order:
                serial_comm.send_data("DETECTOR", command, "READ")
                packet = serial_comm.receive_data(command)
                payload.append(self._extract_payload_byte(packet))
        except Exception as exc:
            log_error(f"Failed to read camera serial number: {exc}")
            return conn.get("serial_num")
        try:
            _serial_num1 = (((payload[0] & 0x1F) << 8) | payload[1]) & 0x1FFF
            _serial_num2 = (payload[2] >> 2) & 0x1F
            _serial_num3 = (((payload[2] & 0x03) << 8) | payload[3]) & 0x3FF
        except (IndexError, TypeError, ValueError) as exc:
            log_error(f"Invalid serial payload: {exc}")
            return conn.get("serial_num")
        serial_number = f"{_serial_num1:04d}{_serial_num2:02d}{_serial_num3:03d}"
        conn["serial_num"] = serial_number
        return serial_number
    
    def auto_bias_setting(self, camera_num, on_complete=None):
        """자동 바이어스 탐색 루틴."""
        def _notify_complete():
            if not on_complete:
                return
            try:
                self.root.after(0, on_complete)
            except tk.TclError:
                pass

        print("[AUTO BIAS] 자동 바이어스 설정 시작.")
        connected = list(camera_num or [])
        if not connected:
            log_error("[AUTO BIAS] 연결된 카메라가 없습니다.")
            _notify_complete()
            return

        # 온도 구간별 허용 중앙값 범위 정의
        target_ranges = {
            "LOW": (8400, 8500, 8600),
            "MID": (4900, 5000, 5100),
            "HIGH": (4900, 5000, 5100),
        }
        temp_range = (self.selected_temp_range.get() or "").upper()
        if temp_range not in target_ranges:
            log_error(f"[AUTO BIAS] 잘못된 온도 구간 선택: {temp_range}")
            _notify_complete()
            return
        target_min, target, target_max = target_ranges.get(temp_range)

        def _write_bias_value(frame_id: int, value: int, register_name: str) -> bool:
            conn, serial_comm = self._get_serial_device(frame_id)
            # value = f"{value:04X}"
            print(value)
            if not serial_comm:
                log_error(f"[AUTO BIAS] Frame {frame_id}에 연결된 시리얼이 없습니다.")
                return False
            try:
                serial_comm.send_data("DETECTOR", register_name, "WRITE", value)
                serial_comm.receive_data(register_name)
                return True
            except Exception as exc:
                log_error(f"[AUTO BIAS] {register_name} 업데이트 실패(frame {frame_id}): {exc}")
                return False

        def _measure_median(frame_id: int, sample: int = 5):
            """지정한 카메라에서 여러 프레임을 읽어 중앙값을 계산한다."""
            cap = self.camera.get(frame_id)
            if not cap or not cap.isOpened():
                log_error(f"[AUTO BIAS] Camera {frame_id}가 열려있지 않습니다.")
                return None
            medians = []
            for _ in range(sample):
                ret, frame = cap.read()
                if not ret:
                    continue
                avg = np.mean((frame & 0x3FFF), dtype=np.int64)
                medians.append(float(avg))
                # medians.append(float(np.median(frame & 0x3FFF)))
                time.sleep(0.02)
            if not medians:
                log_error(f"[AUTO BIAS] Camera {frame_id}에서 유효한 프레임을 얻지 못했습니다.")
                return None
            return float(np.median(medians))

        def _binary_search(frame_id: int):
            """
            GSK_LSB만 사용하여 frame center/median 값을 목표 범위에 맞춘다.
            보정 시나리오:
            1. 0x00, 0xFF를 측정하여 LSB 증가 시 median 증가/감소 방향 판단
            2. 0x00, 0x10, 0x20, ... 0xF0 순서로 coarse search
            3. 방향에 따라 coarse 기준값 선택
            - 증가 방향: target_min보다 작고 가까운 값 선택
            - 감소 방향: target_max보다 크고 가까운 값 선택
            4. coarse search 중 목표 구간을 통과하면 탐색 종료
            - 증가 방향: median_value > target_max 이면 종료
            - 감소 방향: median_value < target_min 이면 종료
            5. 선택된 coarse 구간에서 0xN0 ~ 0xNF fine search
            6. 목표 범위에 들어오면 최종 선택
            7. 목표 범위에 못 들어오면 가장 가까운 LSB를 최종 선택
            """

            def _measure_after_lsb_write(lsb_value: int) -> float | None:
                """GSK_LSB write 후 안정화된 median 측정."""
                if not _write_bias_value(frame_id, lsb_value, "GSK_LSB"):
                    return None

                # 레지스터 반영 및 프레임 안정화 대기
                time.sleep(0.1)

                # 단일 프레임 값은 흔들릴 수 있으므로 여러 번 측정 후 중앙값 사용
                values = []
                for _ in range(5):
                    median_value = _measure_median(frame_id)
                    if median_value is not None:
                        values.append(median_value)
                    time.sleep(0.03)

                if not values:
                    return None

                values.sort()
                return values[len(values) // 2]

            def _calc_error(median_value: float) -> float:
                """
                target_min ~ target_max 안이면 error = 0.
                범위 밖이면 가까운 경계까지의 거리.
                """
                if target_min <= median_value <= target_max:
                    return 0.0
                if median_value < target_min:
                    return target_min - median_value
                return median_value - target_max

            search_results: dict[int, tuple[float, float]] = {}
            # key: LSB 값
            # value: (error, median_value)

            # ─────────────────────────────────────────────
            # 0. 방향 탐색: 0x00, 0xFF 측정
            # ─────────────────────────────────────────────
            median_00 = _measure_after_lsb_write(0x00)
            if median_00 is None:
                return False, None

            median_ff = _measure_after_lsb_write(0xFF)
            if median_ff is None:
                return False, None

            search_results[0x00] = (_calc_error(median_00), median_00)
            search_results[0xFF] = (_calc_error(median_ff), median_ff)

            is_increasing = median_ff >= median_00

            print(
                f"[AUTO BIAS] Cam {frame_id} 방향 탐색: "
                f"LSB 0x00 → median {median_00:.1f}, "
                f"LSB 0xFF → median {median_ff:.1f}, "
                f"방향={'증가' if is_increasing else '감소'}"
            )

            # ─────────────────────────────────────────────
            # 1. coarse search: 0x00, 0x10, ..., 0xF0
            # ─────────────────────────────────────────────
            coarse_candidates = list(range(0x00, 0x100, 0x10))

            selected_coarse = None

            best_approach_coarse = None
            best_approach_error = float("inf")

            best_overall_coarse = None
            best_overall_error = float("inf")

            print(f"[AUTO BIAS] Cam {frame_id} coarse search 시작")

            for lsb in coarse_candidates:
                # 0x00은 방향 탐색에서 이미 측정했으므로 재사용
                if lsb in search_results:
                    error, median_value = search_results[lsb]
                else:
                    median_value = _measure_after_lsb_write(lsb)
                    if median_value is None:
                        return False, None

                    error = _calc_error(median_value)
                    search_results[lsb] = (error, median_value)

                print(
                    f"[AUTO BIAS] Cam {frame_id} coarse / "
                    f"LSB=0x{lsb:02X} / median={median_value:.1f} / error={error:.1f}"
                )

                # fallback용: 전체 중 가장 가까운 coarse 저장
                if error < best_overall_error:
                    best_overall_error = error
                    best_overall_coarse = lsb

                # coarse 단계에서 이미 목표 범위에 들어온 경우
                # 이 경우 해당 coarse 구간에서 fine search 수행
                if error == 0.0:
                    selected_coarse = lsb

                    print(
                        f"[AUTO BIAS] Cam {frame_id} coarse 단계에서 목표 범위 도달. "
                        f"선택 구간=0x{selected_coarse:02X}~"
                        f"0x{min(0xFF, selected_coarse + 0x0F):02X}"
                    )
                    break

                # ─────────────────────────────────────────
                # 방향에 따른 coarse 후보 선택 및 조기 종료
                # ─────────────────────────────────────────
                if is_increasing:
                    # LSB 증가 → median 증가
                    # fine search를 0xN0 → 0xNF로 올릴 것이므로
                    # target_min보다 작고 가장 가까운 coarse를 선택
                    if median_value < target_min:
                        approach_error = target_min - median_value

                        if approach_error < best_approach_error:
                            best_approach_error = approach_error
                            best_approach_coarse = lsb

                    # 증가 방향에서 target_max를 넘으면 목표 구간을 통과한 것
                    # 이후 coarse는 더 볼 필요 없음
                    if median_value > target_max:
                        print(
                            f"[AUTO BIAS] Cam {frame_id} 증가 방향 coarse search에서 목표 구간 통과. "
                            f"탐색 종료. 현재 LSB=0x{lsb:02X}, "
                            f"median={median_value:.1f}, target_max={target_max}"
                        )
                        break

                else:
                    # LSB 증가 → median 감소
                    # fine search를 0xN0 → 0xNF로 올릴 것이므로
                    # target_max보다 크고 가장 가까운 coarse를 선택
                    if median_value > target_max:
                        approach_error = median_value - target_max

                        if approach_error < best_approach_error:
                            best_approach_error = approach_error
                            best_approach_coarse = lsb

                    # 감소 방향에서 target_min보다 작아지면 목표 구간을 통과한 것
                    # 이후 coarse는 더 볼 필요 없음
                    if median_value < target_min:
                        print(
                            f"[AUTO BIAS] Cam {frame_id} 감소 방향 coarse search에서 목표 구간 통과. "
                            f"탐색 종료. 현재 LSB=0x{lsb:02X}, "
                            f"median={median_value:.1f}, target_min={target_min}"
                        )
                        break

            # ─────────────────────────────────────────────
            # 2. coarse 선택 확정
            # ─────────────────────────────────────────────
            if selected_coarse is None:
                if best_approach_coarse is not None:
                    selected_coarse = best_approach_coarse

                    print(
                        f"[AUTO BIAS] Cam {frame_id} 방향 기준 coarse 선택 완료. "
                        f"선택 구간=0x{selected_coarse:02X}~"
                        f"0x{min(0xFF, selected_coarse + 0x0F):02X}, "
                        f"approach_error={best_approach_error:.1f}, "
                        f"방향={'증가' if is_increasing else '감소'}"
                    )

                else:
                    # 방향 기준 후보가 없으면 전체에서 가장 가까운 coarse 사용
                    # 예: 처음부터 목표 구간을 이미 지나친 경우
                    if best_overall_coarse is None:
                        log_error(f"[AUTO BIAS] Cam {frame_id} coarse 탐색 결과 없음.")
                        return False, None

                    selected_coarse = best_overall_coarse

                    print(
                        f"[AUTO BIAS] Cam {frame_id} 방향 기준 coarse 후보 없음. "
                        f"fallback 구간=0x{selected_coarse:02X}~"
                        f"0x{min(0xFF, selected_coarse + 0x0F):02X}, "
                        f"overall_error={best_overall_error:.1f}"
                    )

            # ─────────────────────────────────────────────
            # 3. fine search: 선택된 coarse 구간 내부 탐색
            # 예: selected_coarse = 0x50이면 0x50 ~ 0x5F 탐색
            # ─────────────────────────────────────────────
            fine_low = selected_coarse
            fine_high = min(0xFF, selected_coarse + 0x0F)

            print(
                f"[AUTO BIAS] Cam {frame_id} fine search 시작: "
                f"0x{fine_low:02X}~0x{fine_high:02X}"
            )

            final_lsb = None

            for lsb in range(fine_low, fine_high + 1):
                # coarse에서 이미 측정한 값이면 재사용
                if lsb in search_results:
                    error, median_value = search_results[lsb]
                else:
                    median_value = _measure_after_lsb_write(lsb)
                    if median_value is None:
                        return False, None

                    error = _calc_error(median_value)
                    search_results[lsb] = (error, median_value)

                print(
                    f"[AUTO BIAS] Cam {frame_id} fine / "
                    f"LSB=0x{lsb:02X} / median={median_value:.1f} / error={error:.1f}"
                )

                if error == 0.0:
                    final_lsb = lsb

                    print(
                        f"[AUTO BIAS] Cam {frame_id} fine 단계에서 목표 범위 도달. "
                        f"최종 LSB=0x{final_lsb:02X}, median={median_value:.1f}"
                    )
                    break

            # ─────────────────────────────────────────────
            # 4. 목표 범위 미도달 시 가장 가까운 LSB 선택
            # ─────────────────────────────────────────────
            if final_lsb is None:
                final_lsb = min(search_results, key=lambda k: search_results[k][0])
                final_error, final_median = search_results[final_lsb]

                log_error(
                    f"[AUTO BIAS] Cam {frame_id} 목표 범위 미도달. "
                    f"가장 가까운 LSB=0x{final_lsb:02X}, "
                    f"median={final_median:.1f}, error={final_error:.1f}로 설정합니다."
                )

                success = False

            else:
                success = True

            # ─────────────────────────────────────────────
            # 5. 최종 LSB 적용
            # ─────────────────────────────────────────────
            if not _write_bias_value(frame_id, final_lsb, "GSK_LSB"):
                return False, None

            final_error, final_median = search_results[final_lsb]

            print(
                f"[AUTO BIAS] Cam {frame_id} 최종 적용 완료: "
                f"GSK_LSB=0x{final_lsb:02X}, "
                f"median={final_median:.1f}, "
                f"error={final_error:.1f}, "
                f"success={success}"
            )

            return success, final_lsb

        def tint(cam_id):
            temp_range = self.selected_temp_range.get()
            if temp_range == "LOW":
                value = "273"
            elif temp_range == "MID":
                value = "273"
            else:  # HIGH
                value = "273"
            
            tintM = int(value[0],16)
            tintL = int(value[1:],16)
            _write_bias_value(cam_id, tintM,"TINT_MSB")
            _write_bias_value(cam_id, tintL,"TINT_LSB")

            print("tint_setting_---DONe:",value[0],value[1:])
            return value

        def cint( cam_id):
            temp_range = self.selected_temp_range.get()
            if temp_range == "LOW":
                value = int("93", 16)
                cintv = "93"
            elif temp_range == "MID":
                value = int("A3", 16)
                cintv = "A3"
            else:  # HIGH
                value = int("D3", 16)
                cintv = "D3"
            _write_bias_value(cam_id, value,"CINT")
            print("cint_setting_---DONe:",cintv)
            return cintv

        def gsk_msb(cam_id):
            value = 1
            _write_bias_value(cam_id, value,"GSK_MSB")
            print("gskm_setting_---DONe:",value)
            return value

        def gfid( cam_id):
            value = int("AE", 16)
            gfidv = "AE"
            _write_bias_value(cam_id, value,"GFID")
            print("gfid_setting_---DONe:",value)
            return gfidv

        def _worker():
            try:
                self.auto_bias = False
                for cam_id in connected:
                    if cam_id not in self.connected_cameras:
                        log_error(f"[AUTO BIAS] Cam {cam_id}는 더 이상 연결되어 있지 않습니다.")
                        continue
                    cint_value = cint(cam_id)
                    tint_value= tint(cam_id)
                    gskM_value = gsk_msb(cam_id)
                    gfid_value = gfid(cam_id)
                    success, gskL_value = _binary_search(cam_id)
                    if gskL_value:
                        gsklv = f"{gskL_value:02X}"
                    if not success:
                        log_error(f"[AUTO BIAS] Cam {cam_id} 보정 실패.")
                        continue
                    self.bias_dict[cam_id] = {
                            "CINT" : cint_value,
                            "TINT" : tint_value,
                            "GSK_MSB"  : gskM_value,
                            "GSK_LSB" : gsklv,
                            "GFID" : gfid_value
                        }

                self.auto_bias = True
                print("[AUTO BIAS] 모든 카메라 보정을 완료했습니다.")
            finally:
                _notify_complete()

        threading.Thread(target=_worker, daemon=True).start()

    def _write_bias_value(self, frame_id: int, value: int, register_name: str) -> bool:
        conn, serial_comm = self._get_serial_device(frame_id)
        if not serial_comm:
            log_error(f"[AUTO BIAS] Frame {frame_id}에 연결된 시리얼이 없습니다.")
            return False
        try:
            serial_comm.send_data("DETECTOR", register_name, "WRITE", int(value) & 0xFF)
            serial_comm.receive_data(register_name)
            print(f"[AUTO BIAS]{register_name} 업데이트(frame {frame_id})")
            return True
        except Exception as exc:
            log_error(f"[AUTO BIAS]{register_name} 업데이트 실패(frame {frame_id}): {exc}")
            return False

    def _parse_bias_int(self, value, field_name: str) -> int:
        if isinstance(value, int):
            return value
        if isinstance(value, str):
            cleaned = value.strip()
            if not cleaned:
                raise ValueError(f"{field_name} is empty.")
            return int(cleaned, 16)
        raise ValueError(f"{field_name} has unsupported type: {type(value).__name__}")

    def _apply_bias_data(self, frame_id: int, json_data: dict) -> bool:
        try:
            cint_raw = json_data["CINT"]
            tint_raw = str(json_data["TINT"]).strip()
            gsk_msb_raw = json_data["GSK_MSB"]
            gsk_lsb_raw = json_data["GSK_LSB"]
            gfid_raw = json_data["GFID"]
            if len(tint_raw) < 2:
                raise ValueError("TINT must contain both MSB/LSB values.")
            cintv = self._parse_bias_int(cint_raw, "CINT")
            tintmv = self._parse_bias_int(tint_raw[0], "TINT_MSB")
            tintlv = self._parse_bias_int(tint_raw[1:], "TINT_LSB")
            gskmv = self._parse_bias_int(gsk_msb_raw, "GSK_MSB")
            gsklv = self._parse_bias_int(gsk_lsb_raw, "GSK_LSB")
            gfidv = self._parse_bias_int(gfid_raw, "GFID")
        except (KeyError, TypeError, ValueError) as exc:
            log_error(f"[AUTO BIAS] bias.json 파싱 실패(frame {frame_id}): {exc}")
            return False

        self.bias_dict[frame_id] = {
            "CINT": f"{cintv:02X}",
            "TINT": tint_raw.upper(),
            "GSK_MSB": gskmv,
            "GSK_LSB": f"{gsklv:02X}",
            "GFID": f"{gfidv:02X}",
        }

        writes = [
            self._write_bias_value(frame_id, cintv, "CINT"),
            self._write_bias_value(frame_id, tintmv, "TINT_MSB"),
            self._write_bias_value(frame_id, tintlv, "TINT_LSB"),
            self._write_bias_value(frame_id, gskmv, "GSK_MSB"),
            self._write_bias_value(frame_id, gsklv, "GSK_LSB"),
            self._write_bias_value(frame_id, gfidv, "GFID"),
        ]
        return all(writes)

    def _find_bias_json_for_serial(self, root_dir: str, serial_number: str):
        temp_dict = {
            "LOW": "LNN30",
            "MID": "RPP10",
            "HIGH": "H1PP40",
        }                       
        temp_range = self.selected_temp_range.get()
        preferred_folder = None
        if temp_range in temp_dict:
            preferred_folder = temp_dict[temp_range]

        matched_bias_files = []
        try:
            entries = sorted(
                [entry for entry in os.scandir(root_dir) if entry.is_dir()],
                key=lambda item: item.name.lower(),
            )
        except OSError as exc:
            log_error(f"[AUTO BIAS] 폴더 탐색 실패: {exc}")
            return None

        for entry in entries:
            folder_name = entry.name
            if not (
                folder_name == serial_number
                or folder_name.startswith(f"{serial_number}_")
            ):
                continue

            if preferred_folder:
                preferred_path = os.path.join(entry.path, preferred_folder, "bias.json")
                if os.path.isfile(preferred_path):
                    return preferred_path

            found = glob.glob(os.path.join(entry.path, "**", "bias.json"), recursive=True)
            if found:
                matched_bias_files.extend(found)

        if not matched_bias_files:
            return None
        return sorted(matched_bias_files)[0]

    def bias_read(self, selected_index):
        try:
            bias_path = filedialog.askopenfilename(title="Select bias Directory")
            if not bias_path:
                log_error(f"[AUTO BIAS] 바이어스 파일 선택 중 취소")
                return  # 사용자가 파일 선택을 취소한 경우
        except FileNotFoundError as e:
            log_error(f"[AUTO BIAS] 바이어스 파일 선택 중 오류 발생: {e}")
            return
        except Exception as e:
            log_error(f"[AUTO BIAS] 바이어스 파일 선택 중 알 수 없는 오류 발생: {e}")
            return

        try:
            with open(bias_path, "r", encoding="utf-8") as f:
                json_data = json.load(f)
        except Exception as exc:
            log_error(f"[AUTO BIAS] bias.json 파일 열기 실패: {exc}")
            return

        if self._apply_bias_data(selected_index, json_data):
            print(f"[AUTO BIAS] bias 적용 완료(frame {selected_index}) - {bias_path}")

    def all_bias_read(self):
        connected = list(self.connected_cameras or [])

        root_dir = filedialog.askdirectory(title="Select Root Directory With Serial Folders")
        if not root_dir:
            log_error("[AUTO BIAS] 폴더 선택 중 취소")
            return

        success_count = 0
        for frame_id in connected:
            serial_num = self.get_current_serial_number(frame_id) or self.read_serial(frame_id)
            if not serial_num:
                log_error(f"[AUTO BIAS] Frame {frame_id}의 시리얼 번호를 확인할 수 없습니다.")
                continue

            bias_path = self._find_bias_json_for_serial(root_dir, serial_num)
            if not bias_path:
                log_error(
                    f"[AUTO BIAS] 시리얼 {serial_num}(frame {frame_id})에 해당하는 bias.json을 찾지 못했습니다."
                )
                continue
            try:
                with open(bias_path, "r", encoding="utf-8") as f:
                    json_data = json.load(f)
            except Exception as exc:
                log_error(f"[AUTO BIAS] bias.json 로드 실패({bias_path}): {exc}")
                continue

            if self._apply_bias_data(frame_id, json_data):
                success_count += 1
                print(f"[AUTO BIAS] Camera {frame_id} Bias read 완료\n" )
                print(f"[AUTO BIAS] Bias Read 경로 {bias_path}\n")
        print(f"[AUTO BIAS] All bias read 완료: {success_count}/{len(connected)}")

    def bias_check(self, selected_index):
        def _read_bias_value(frame_id: int, register_name: str) -> bool:
            """지정한 카메라에 GSK_LSB 값을 기록"""
            conn, serial_comm = self._get_serial_device(frame_id)
            if not serial_comm:
                log_error(f"[AUTO BIAS] Frame {frame_id}에 연결된 시리얼이 없습니다.")
                return False
            try:
                serial_comm.send_data("DETECTOR", register_name, "READ")
                data = serial_comm.receive_data(register_name)
                print(f"[BIAS CHECK]{register_name} - {data[12:]} - (camera {frame_id})")
                return True
            except Exception as exc:
                log_error(f"[AUTO BIAS]{register_name} 업데이트 실패(camera {frame_id}): {exc}")
                return False

        _read_bias_value(selected_index,"CINT")
        _read_bias_value(selected_index,"TINT_MSB")
        _read_bias_value(selected_index,"TINT_LSB")
        _read_bias_value(selected_index,"GSK_MSB")
        _read_bias_value(selected_index,"GSK_LSB")
        _read_bias_value(selected_index,"GFID")

def custom_exit(*args, **kwargs):
    log_error("WARNING: sys.exit() was called; dumping the stack trace.")
    traceback.print_stack()  
    raise RuntimeError("sys.exit() 호출 감지됨")  # 예외를 발생시켜 종료 방지

# 메인 루프 시작
if __name__ == "__main__":
    log_dir = os.path.join(os.getcwd(), "./logs")
    os.makedirs(log_dir, exist_ok=True)
    log_timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    main_log_path = os.path.join(log_dir, f"main_log_{log_timestamp}.txt")
    main_log_file = open(main_log_path, "w", encoding="utf-8")
    log_file_stream = TimestampedStream(AnsiStrippedStream(main_log_file))

    original_stdout = sys.stdout
    original_stderr = sys.stderr
    console_stdout = TimestampedStream(original_stdout)
    console_stderr = TimestampedStream(original_stderr)
    sys.stdout = ConsoleTee(console_stdout, log_file_stream)
    sys.stderr = ConsoleTee(console_stderr, log_file_stream)

    def _close_main_log():
        try:
            main_log_file.close()
        except Exception:
            pass

    atexit.register(_close_main_log)

    root = tk.Tk()
    root.title("CBS014d")
    root.geometry("1920x1080")
    app = custom(root)
    gui_stream = TimestampedStream(GuiConsoleStream(app.log_console))
    sys.stdout = ConsoleTee(console_stdout, log_file_stream, gui_stream)
    sys.stderr = ConsoleTee(console_stderr, log_file_stream, gui_stream)
    print(f"[LOG] Main session log: {main_log_path}")
    sys.exit = custom_exit  # sys.exit() 감지
    root.mainloop()
