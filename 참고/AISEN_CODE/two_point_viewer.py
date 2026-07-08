import tkinter as tk
from tkinter import filedialog, ttk
from PIL import Image, ImageTk
import os
import sys
import numpy as np
import pandas as pd
import cv2
from scipy.ndimage import median_filter as scipy_median
import re


# --------- 콘솔 로그 리디렉션 ---------
class TextRedirector:
    def __init__(self, root, text_widget, tag="stdout"):
        self.text_widget = text_widget
        self.tag = tag
        self.gui = root

    def write(self, string, tag=None):
        if '\r' in string:
            self.text_widget.delete("end-1l", "end")
            string = string.replace('\r', '\n')
        self.text_widget.insert(tk.END, string, tag or self.tag)
        self.text_widget.see(tk.END)
        self.gui.root.update_idletasks()

    def flush(self):
        pass


# --------- 기본 UI ---------
class ThermalUI:
    def __init__(self, root, redirect_output=True, initial_directory=None):
        self.root = root
        self.redirect_output = redirect_output
        self.initial_directory = initial_directory
        self._stdout_redirector = None
        self._stderr_redirector = None
        self._original_stdout = None
        self._original_stderr = None
        self.root.title("THERMAL TECLESS PARAM ESTIMATION")
        self.root.geometry("1200x800")

        # 상태값
        self.SELECTED_DIR = ""
        self.serial_folders = []
        self.temperature_folders = []
        self.selected_temperature = None

        # 결과(단일뷰 네비)
        self.canvas_shape = (640, 480)
        self.current_index = 0
        self.result_images = []         # PIL.Image
        self.result_full_arrays = []    # full NUC 배열
        self.result_serial_names = []   # 시리얼 폴더명
        self.result_status_rows = []    # 표 출력용: (serial, netd, n_cold, n_hot, n_room, cpx00_med, status)

        self.build_top_panel()
        self.build_single_view()
        self.build_log_box()

        if self.initial_directory and os.path.isdir(self.initial_directory):
            self.set_selected_directory(self.initial_directory)

        # 키보드 좌우 이동
        self.root.bind("<Left>", lambda e: self.show_prev())
        self.root.bind("<Right>", lambda e: self.show_next())

    def build_top_panel(self):
        frame_top = tk.Frame(self.root)
        frame_top.pack(pady=10, anchor='w', fill='x')

        btn_open = tk.Button(frame_top, text="OPEN", width=10, command=self.select_directory)
        btn_open.grid(row=0, column=0, padx=5)

        tk.Label(frame_top, text="Serial num").grid(row=0, column=1)
        self.entry_serial = tk.Entry(frame_top, width=50, bg="gray")
        self.entry_serial.grid(row=0, column=2, padx=5, sticky='w')
        self.entry_serial.config(state="readonly")

        self.btn_start = tk.Button(frame_top, text="START", width=10, command=self.tecless_param_run, state='disabled')
        self.btn_start.grid(row=1, column=0, padx=5, pady=5)

        tk.Label(frame_top, text="Temperature").grid(row=1, column=1)
        self.combo_temperature = ttk.Combobox(frame_top, values=[], width=20, state="readonly")
        self.combo_temperature.grid(row=1, column=2, padx=5, sticky='w')
        self.combo_temperature.bind("<<ComboboxSelected>>", self.check_start_ready)

    def build_single_view(self):
        self.viewer_frame = tk.LabelFrame(self.root, text="TWO-POINT NUC RESULT (Single View)", bg="#ddd")
        self.viewer_frame.pack(padx=10, pady=5, fill='x')

        header_row = tk.Frame(self.viewer_frame, bg="#ddd")
        header_row.pack(fill='x', padx=10, pady=(10, 0))
        self.lbl_title = tk.Label(header_row, text="(SERIAL) - (STATUS)", font=("", 12, "bold"), bg="#ddd")
        self.lbl_title.pack(side='left')

        canvas_row = tk.Frame(self.viewer_frame, bg="#ddd")
        canvas_row.pack(pady=(10, 10))
        self.single_canvas = tk.Canvas(
            canvas_row,
            width=self.canvas_shape[0],
            height=self.canvas_shape[1],
            bg="black",
            bd=0,
            highlightthickness=0,
            relief="flat"
        )
        self.single_canvas.pack()

        nav_row = tk.Frame(self.viewer_frame, bg="#ddd")
        nav_row.pack(pady=(0, 10))
        tk.Button(nav_row, text="◀ Prev", width=10, command=self.show_prev).pack(side='left', padx=10)
        tk.Button(nav_row, text="Next ▶", width=10, command=self.show_next).pack(side='left', padx=10)

    def build_log_box(self):
        frame = tk.Frame(self.root)
        frame.pack(fill='both', expand=True, padx=10, pady=(5, 5), side='bottom')

        self.log_text = tk.Text(
            frame,
            height=11,
            bg='white',
            fg='black',
            insertbackground='black',
            font=("Consolas", 11)
        )
        self.log_text.pack(fill='both', expand=True)
        self.log_text.tag_config("stdout", foreground="black")
        self.log_text.tag_config("stderr", foreground="red")
        self.log_text.tag_config("error", foreground="red")

        self._stdout_redirector = TextRedirector(self, self.log_text, "stdout")
        self._stderr_redirector = TextRedirector(self, self.log_text, "stderr")
        if self.redirect_output:
            self._original_stdout = sys.stdout
            self._original_stderr = sys.stderr
            sys.stdout = self._stdout_redirector
            sys.stderr = self._stderr_redirector

    def _log(self, *args, **kwargs):
        sep = kwargs.pop("sep", " ")
        end = kwargs.pop("end", "\n")
        tag = kwargs.pop("tag", None)
        if end is None:
            end = ""
        text = sep.join(str(arg) for arg in args)
        payload = text if (end and text.endswith(end)) else (text + end)
        if self._stdout_redirector:
            self._stdout_redirector.write(payload, tag=tag)
        if not self.redirect_output:
            print(*args, sep=sep, end=end)

    def restore_output(self):
        if self._original_stdout is not None:
            sys.stdout = self._original_stdout
            self._original_stdout = None
        if self._original_stderr is not None:
            sys.stderr = self._original_stderr
            self._original_stderr = None

    def shutdown(self):
        self.restore_output()
        try:
            self.root.destroy()
        except Exception:
            pass

    # ========== 공용 동작 ==========
    def set_selected_directory(self, directory):
        self.SELECTED_DIR = directory

        serials = [
            d for d in os.listdir(self.SELECTED_DIR)
            if d and not (d.startswith("_") or d.startswith("."))
            and os.path.isdir(os.path.join(self.SELECTED_DIR, d))
        ]

        self.serial_folders = [os.path.join(self.SELECTED_DIR, d) for d in serials]
        self.temperature_folders = self._gather_temperature_folders()

        self.combo_temperature["values"] = self.temperature_folders
        self.combo_temperature.set("")

        self.entry_serial.config(state="normal")
        self.entry_serial.delete(0, tk.END)
        self.entry_serial.insert(0, " / ".join(serials))
        self.entry_serial.config(state="readonly")

        self._log(f"[INFO] directory selected: {self.SELECTED_DIR}")
        self._log(f"[INFO] Found serial folders: {serials}")
        self._log(f"[INFO] Found temperature folders: {self.temperature_folders}")
        self.check_start_ready()

    def select_directory(self):
        initial_dir = self.SELECTED_DIR or self.initial_directory
        if not initial_dir or not os.path.isdir(initial_dir):
            initial_dir = os.getcwd()

        selected_dir = filedialog.askdirectory(initialdir=initial_dir)
        if not selected_dir:
            return

        self.initial_directory = selected_dir
        self.set_selected_directory(selected_dir)

    def _gather_temperature_folders(self):
        temp_union = set()
        for serial_path in self.serial_folders:
            if not os.path.isdir(serial_path):
                continue
            temps = [
                f for f in os.listdir(serial_path)
                if os.path.isdir(os.path.join(serial_path, f))
                and f[:1].upper() in ("L", "R", "H")
            ]
            temp_union.update(temps)
        return sorted(list(temp_union))

    def check_start_ready(self, event=None):
        self.btn_start.config(state='normal' if self.combo_temperature.get() else 'disabled')

    def _render_current(self):
        if not self.result_images:
            self.single_canvas.delete("all")
            self.lbl_title.config(text="(결과 없음)")
            return

        self.current_index = max(0, min(self.current_index, len(self.result_images) - 1))
        idx = self.current_index

        img = self.result_images[idx]
        serial_name = self.result_serial_names[idx]

        netd_str = "N/A"
        status_str = ""

        if idx < len(self.result_status_rows):
            # (serial, netd, n_cold, n_hot, n_room, cpx00_med, status)
            _, netd_value, _, _, _, _, status_str = self.result_status_rows[idx]
            try:
                netd_str = f"{float(netd_value):.4f}"
            except (TypeError, ValueError):
                netd_str = "N/A"

        label_text = f"[{idx+1}/{len(self.result_images)}] {serial_name} - "
        if status_str:
            label_text += f"({status_str})"

        self.lbl_title.config(text=label_text)

        w, h = img.size
        self.single_canvas.config(width=w, height=h)

        photo = ImageTk.PhotoImage(img)
        self.single_canvas.delete("all")
        self.single_canvas.create_image(0, 0, anchor='nw', image=photo)
        self.single_canvas.image = photo  # GC 방지

    def show_prev(self):
        if not self.result_images:
            return
        self.current_index = (self.current_index - 1) % len(self.result_images)
        self._render_current()

    def show_next(self):
        if not self.result_images:
            return
        self.current_index = (self.current_index + 1) % len(self.result_images)
        self._render_current()

    def tecless_param_run(self):
        if not self.SELECTED_DIR:
            self._log("[ERROR] directory is not selected")
            return
        self._log("\n[RUN] Tecless param estimation start")
        self.run_processing()

    # run_processing()은 서브클래스에서 구현


# --------- 처리 로직 ---------
class TECLESS(ThermalUI):
    def __init__(self, root, redirect_output=True, initial_directory=None):
        super().__init__(
            root=root,
            redirect_output=redirect_output,
            initial_directory=initial_directory,
        )

        self.serial_num = None
        self.collect_DP_per_img = False

        self.dp_det_alpha = 5.0
        self.dp_cor_kernel = 7

        self.Width = 640
        self.Height = 480
        self.canvas_shape = (self.Width, self.Height)

        self.poly = 3
        self.data_count = 5

        self.temp_fname = {'L': 'L', 'M': 'R', 'H': 'H1'}
        self.temp_bbody = {'L': [10, 70], 'M': [20, 80], 'H': [20, 80]}

        self.is_raw_full = True
        self.last_missing_info = ""

        self.netd = []

        self._fpa_a = -187.37
        self._fpa_b = 412.5
        self._fpa_scale = 4.096
        self._fpa_maxval = 32768.0

    def calculate_fpa_temp(self, temp, check_snf=False, input_base="auto"):
        if isinstance(temp, (bytes, bytearray)):
            s = temp.decode().strip()
        else:
            s = str(temp).strip()

        def parse_u16_from_string(ss):
            s2 = ss.lower().strip()
            if s2.startswith("0x"):
                return int(s2, 16)
            if re.search(r"[a-f]", s2):
                return int(s2, 16)
            if re.fullmatch(r"\d+", s2):
                return int(s2, 10)
            return int(s2, 16)

        if isinstance(temp, int) and input_base != "hex":
            raw_u16 = temp
        else:
            if input_base == "hex":
                s_hex = s[2:] if s.lower().startswith("0x") else s
                raw_u16 = int(s_hex, 16)
            elif input_base == "dec":
                raw_u16 = int(s, 10)
            else:
                raw_u16 = parse_u16_from_string(s)

        raw_u16 &= 0xFFFF
        raw_s16 = (raw_u16 + 2**15) % 2**16 - 2**15

        v_temp = (raw_s16 / self._fpa_maxval) * self._fpa_scale
        temperature_celsius = self._fpa_a * v_temp + self._fpa_b
        return float(temperature_celsius)

    def detect_dead_pixels_coords(self, frame):
        median_dc_9x9 = scipy_median(frame, size=9)
        _mean = cv2.boxFilter(frame.astype(np.float32), ddepth=-1, ksize=(81, 81))
        _mean_sq = cv2.boxFilter((frame.astype(np.float32) ** 2), ddepth=-1, ksize=(81, 81))
        std_dc_81x81 = np.sqrt(_mean_sq - _mean ** 2)

        dead_pixel_mask = np.abs(frame - median_dc_9x9) > (self.dp_det_alpha * std_dc_81x81)

        y_indices, x_indices = np.where(dead_pixel_mask)
        for y, x in zip(y_indices, x_indices):
            if x + 1 < frame.shape[1]:
                dead_pixel_mask[y, x + 1] = True
            if x + 2 < frame.shape[1]:
                dead_pixel_mask[y, x + 2] = True
        return dead_pixel_mask

    def merge_dead_pixels_coords(self, DP_mask_collected):
        lynred_csv_path = os.path.join('dead_pixel_masks', f'{self.serial_num}', f'{self.serial_num}.csv')
        if os.path.isfile(lynred_csv_path):
            df = pd.read_csv(lynred_csv_path)
            lynred_mask = np.zeros((480, 640), dtype=bool)
            lynred_dead_pixel_position = list(zip(df['Ligne'], df['Colonne']))
            lynred_dead_pixel_position = [(row - 1, col - 1) for (row, col) in lynred_dead_pixel_position]
            if len(lynred_dead_pixel_position) > 0:
                lynred_y, lynred_x = zip(*lynred_dead_pixel_position)
                for i in range(len(lynred_x)):
                    lynred_mask[lynred_y[i], lynred_x[i]] = True
            total_pixel_mask = DP_mask_collected | lynred_mask
            return total_pixel_mask
        else:
            return DP_mask_collected

    def correct_dead_pixels(self, frame, dead_pixel_mask, dp_cor_size):
        hlaf_ksize = dp_cor_size // 2
        frame_corrected = frame.copy()
        for y, x in np.argwhere(dead_pixel_mask):
            xst = x - hlaf_ksize
            xend = x + hlaf_ksize + 1
            yst = y - hlaf_ksize
            yend = y + hlaf_ksize + 1
            if x - hlaf_ksize <= 0:
                xst = 0
                xend = xst + dp_cor_size
            if x + hlaf_ksize + 1 >= frame.shape[1] - 1:
                xend = frame.shape[1] - 1
                xst = xend - dp_cor_size
            if y - hlaf_ksize <= 0:
                yst = 0
                yend = yst + dp_cor_size
            if y + hlaf_ksize + 1 >= frame.shape[0] - 1:
                yend = frame.shape[0] - 1
                yst = yend - dp_cor_size
            frame_corrected[y, x] = scipy_median(frame[yst:yend, xst:xend], dp_cor_size).mean()
        return frame_corrected

    def thresh_plateau_hist_eq(self, frame):
        if frame.dtype in (np.int16, np.float64, np.float32):
            bins = 2 ** 14
            frame = frame.clip(0, 2 ** 14 - 1).astype(np.int16)
        elif frame.dtype == np.uint8:
            bins = 256
        else:
            bins = 2 ** 14
            frame = frame.astype(np.int16).clip(0, 2 ** 14 - 1)

        hist_input = cv2.calcHist([frame.astype(np.uint16)], [0], None, [bins], [0, bins]).squeeze(1)
        hist_clip = hist_input.clip(0, 100)

        cdf = np.cumsum(hist_clip)
        cdf_msk = np.ma.masked_equal(cdf, 0)
        cdf_msk = (cdf_msk - cdf_msk.min()) / (cdf_msk[-1] - cdf_msk.min()) * 255
        cdf = np.ma.filled(cdf_msk, 0)
        out = cdf[frame]
        return out.astype(np.uint8)

    def _load_raw_image(self, path):
        with open(path, "rb") as raw_img:
            arr = np.fromfile(raw_img, dtype='int16', sep="")
        if arr.shape[0] != self.Width * self.Height:
            _raw_img = np.zeros((self.Height, self.Width), dtype=np.float32)
            _raw_img[:self.Height - 1, :] = arr.reshape(self.Height - 1, self.Width)
            _raw_img[self.Height - 1, :] = _raw_img[self.Height - 2, :]
            return _raw_img.astype(np.float32)
        else:
            arr = np.reshape(arr, [self.Height, self.Width])
            return arr.astype(np.float32)

    def _get_raw_images(self, path):
        self.is_raw_full = True
        self.last_missing_info = ""

        serial_name = os.path.basename(os.path.dirname(path))
        folder_names = os.listdir(path)

        cold_folder_name = [fname for fname in folder_names if fname == 'cold']
        hot_folder_name = [fname for fname in folder_names if fname == 'hot']
        raw_folder_name = [fname for fname in folder_names if fname == 'room']

        missing = []
        if len(cold_folder_name) == 0:
            missing.append("cold")
        if len(hot_folder_name) == 0:
            missing.append("hot")
        if len(raw_folder_name) == 0:
            missing.append("room")

        if missing:
            missing_str = ", ".join(missing)
            self.is_raw_full = False
            self.last_missing_info = missing_str

            if hasattr(self, "problem_folders"):
                self.problem_folders.append((serial_name, path, f"BB Folder: {missing_str}"))

            raw = np.zeros((self.Height, self.Width), dtype=np.float32)
            return raw, raw, raw, 0, 0, 0, float("nan")

        cold_folder_name = cold_folder_name[0]
        hot_folder_name = hot_folder_name[0]
        raw_folder_name = raw_folder_name[0]

        cold_raw_file_names = sorted(os.listdir(os.path.join(path, cold_folder_name)))
        hot_raw_file_names = sorted(os.listdir(os.path.join(path, hot_folder_name)))
        raw_img_file_names = sorted(os.listdir(os.path.join(path, raw_folder_name)))

        n_cold = len(cold_raw_file_names)
        n_hot = len(hot_raw_file_names)
        n_room = len(raw_img_file_names)

        data_len = min(n_cold, n_hot)

        if n_cold != 100 or n_hot != 100 or n_room == 0:
            if hasattr(self, "problem_folders"):
                self.problem_folders.append((serial_name, path, f"Frame: cold={n_cold}, hot={n_hot}, room={n_room}"))

            raw = np.zeros((self.Height, self.Width), dtype=np.float32)
            return raw, raw, raw, n_cold, n_hot, n_room, float("nan")

        cold_full = np.zeros((data_len, self.Height, self.Width), dtype=np.float32)
        hot_full = np.zeros((data_len, self.Height, self.Width), dtype=np.float32)

        Csave_pixel_mask = np.zeros((self.Height, self.Width), dtype=bool)
        Hsave_pixel_mask = np.zeros((self.Height, self.Width), dtype=bool)

        cold_px00_values = []

        # Cold 평균
        for i, filename in enumerate(cold_raw_file_names[:data_len]):
            _path = os.path.join(path, cold_folder_name, filename)
            cold_full[i] = self._load_raw_image(_path)

            cold_px00_values.append(float(cold_full[i, 0, 0]))

            if self.collect_DP_per_img:
                _Cdead_pixel_mask = self.detect_dead_pixels_coords(cold_full[i])
                Csave_pixel_mask = Csave_pixel_mask | _Cdead_pixel_mask
            elif i == 0:
                _Cdead_pixel_mask = self.detect_dead_pixels_coords(cold_full[i])
                Csave_pixel_mask = Csave_pixel_mask | _Cdead_pixel_mask

        cold_avg_raw = cold_full.mean(axis=0)

        # Hot 평균
        for i, filename in enumerate(hot_raw_file_names[:data_len]):
            _path = os.path.join(path, hot_folder_name, filename)
            hot_full[i] = self._load_raw_image(_path)

            if self.collect_DP_per_img:
                _Hdead_pixel_mask = self.detect_dead_pixels_coords(hot_full[i])
                Hsave_pixel_mask = Hsave_pixel_mask | _Hdead_pixel_mask
            elif i == 0:
                _Hdead_pixel_mask = self.detect_dead_pixels_coords(hot_full[i])
                Hsave_pixel_mask = Hsave_pixel_mask | _Hdead_pixel_mask

        hot_avg_raw = hot_full.mean(axis=0)

        self.netd.append(self.get_NETD(cold_full, hot_full, 20, 80))

        # cold (0,0) 중앙값 -> FPA temp 변환(계수 세팅되어 있을 때만)
        try:
            med_raw = int(np.median(np.array(cold_px00_values, dtype=np.float32)))
            try:
                cold_px00_median = self.calculate_fpa_temp(f"0x{int(med_raw) & 0xFFFF:04X}")
            except Exception:
                cold_px00_median = float(med_raw)
        except Exception:
            cold_px00_median = float("nan")

        # 메타 픽셀 보정
        cold_avg_raw[0, 0:2] = cold_avg_raw[0, 2:4]
        hot_avg_raw[0, 0:2] = hot_avg_raw[0, 2:4]

        save_pixel_mask = Csave_pixel_mask | Hsave_pixel_mask
        self.dead_pixel_mask = self.merge_dead_pixels_coords(save_pixel_mask)

        # Room 한 장
        _path = os.path.join(path, raw_folder_name, raw_img_file_names[0])
        _raw_img = self._load_raw_image(_path)
        _raw_img[0, 0:2] = _raw_img[0, 2:4]

        cold_raw = self.correct_dead_pixels(cold_avg_raw, self.dead_pixel_mask, self.dp_cor_kernel)
        hot_raw = self.correct_dead_pixels(hot_avg_raw, self.dead_pixel_mask, self.dp_cor_kernel)
        raw_img = self.correct_dead_pixels(_raw_img, self.dead_pixel_mask, self.dp_cor_kernel)

        return cold_raw, hot_raw, raw_img, n_cold, n_hot, n_room, cold_px00_median

    def get_NETD(self, cold_full, hot_full, cold_temp, hot_temp):
        cold_avg_frame = np.mean(cold_full, axis=0)
        hot_avg_frame = np.mean(hot_full, axis=0)

        pixel_responsivity = (hot_avg_frame - cold_avg_frame) / (hot_temp - cold_temp)
        overall_responsivity = np.mean(pixel_responsivity)

        sigma_LSB_hot = np.std(hot_full, axis=0)
        sigma_LSB_cold = np.std(cold_full, axis=0)

        netd_map_hot = sigma_LSB_hot / overall_responsivity
        netd_map_cold = sigma_LSB_cold / overall_responsivity

        overall_netd = np.nanmean((netd_map_hot + netd_map_cold) / 2) * 1000
        return overall_netd

    def _get_two_point_vals_canlab(self, cold_ref, hot_ref):
        diff_ref = hot_ref - cold_ref
        hot_ref_avg = hot_ref.mean()
        cold_ref_avg = cold_ref.mean()

        msk = diff_ref <= 0
        diff_ref[msk] = 1e-6

        ref_gain = (hot_ref_avg - cold_ref_avg) / diff_ref
        ref_offset = cold_ref_avg - ref_gain * cold_ref
        return ref_gain, ref_offset

    def run_processing(self):
        self.result_images = []
        self.result_full_arrays = []
        self.result_serial_names = []
        self.result_status_rows = []
        self.current_index = 0
        self.netd = []
        self.problem_folders = []

        self.selected_temperature = self.combo_temperature.get()
        if not self.selected_temperature:
            self._log("[ERROR] Temperature folder not selected.")
            self._render_current()
            return

        # =========================================================
        # 1) 각 시리얼 처리
        #    result_status_rows: (serial, netd, n_cold, n_hot, n_room, fpa_med, status)
        # =========================================================
        for _, serial_path in enumerate(self.serial_folders):
            serial_name = os.path.basename(serial_path)
            temp_path = os.path.join(serial_path, self.selected_temperature)

            # Temp folder 없음
            if not os.path.exists(temp_path):
                reason = "MISSING: Temp Folder"
                self.problem_folders.append((serial_name, temp_path, reason))

                self.result_serial_names.append(serial_name)
                self.result_full_arrays.append(None)
                self.result_images.append(Image.fromarray(np.zeros((self.Height, self.Width), dtype=np.uint8)))

                self.result_status_rows.append((serial_name, float("nan"), 0, 0, 0, float("nan"), reason))
                continue

            cold_avg, hot_avg, room_avg, n_cold, n_hot, n_room, fpa_med = self._get_raw_images(temp_path)

            # BB 폴더 누락
            if not self.is_raw_full:
                status = f"MISSING: {self.last_missing_info}"

                self.result_serial_names.append(serial_name)
                self.result_full_arrays.append(None)
                self.result_images.append(Image.fromarray(np.zeros((self.Height, self.Width), dtype=np.uint8)))

                self.result_status_rows.append((serial_name, float("nan"), int(n_cold), int(n_hot), int(n_room), float(fpa_med), status))
                continue

            # 프레임 개수 문제 등으로 raw=0 케이스
            if cold_avg.sum() == 0 and hot_avg.sum() == 0 and room_avg.sum() == 0:
                status = f"FRAME: cold={n_cold}, hot={n_hot}, room={n_room}"

                self.result_serial_names.append(serial_name)
                self.result_full_arrays.append(None)
                self.result_images.append(Image.fromarray(np.zeros((self.Height, self.Width), dtype=np.uint8)))

                self.result_status_rows.append((serial_name, float("nan"), int(n_cold), int(n_hot), int(n_room), float(fpa_med), status))
                continue

            gain, offset = self._get_two_point_vals_canlab(cold_avg, hot_avg)
            nuc = room_avg * gain + offset

            img_arr = self.thresh_plateau_hist_eq(nuc)
            pil_img = Image.fromarray(img_arr)

            netd_value = self.netd[-1]

            self.result_serial_names.append(serial_name)
            self.result_full_arrays.append(nuc)
            self.result_images.append(pil_img)

            self.result_status_rows.append((serial_name, float(netd_value), int(n_cold), int(n_hot), int(n_room), float(fpa_med), "OK"))

        # =========================================================
        # 2) NETD / FPA outlier 판정
        #    - NETD: others_mean + 100 초과 → "ERROR: NETD"
        #    - FPA : others_mean + 20 초과 → "ERROR: FPA"
        # =========================================================
        netd_list = []
        fpa_list = []

        for (_, netd_value, _, _, _, fpa_value, _) in self.result_status_rows:
            try:
                netd_list.append(float(netd_value))
            except Exception:
                netd_list.append(np.nan)
            try:
                fpa_list.append(float(fpa_value))
            except Exception:
                fpa_list.append(np.nan)

        new_rows = []
        for i, row in enumerate(self.result_status_rows):
            serial, netd_value, n_cold, n_hot, n_room, fpa_value, status = row

            # ---- NETD ERROR ----
            netd_err = False
            try:
                v = float(netd_value)
                if not np.isnan(v):
                    others = [x for j, x in enumerate(netd_list) if j != i and not np.isnan(x)]
                    if others and v > float(np.mean(others)) + 100:
                        netd_err = True
            except Exception:
                pass

            # ---- FPA ERROR ----
            fpa_err = False
            try:
                t = float(fpa_value)
                if not np.isnan(t):
                    others = [x for j, x in enumerate(fpa_list) if j != i and not np.isnan(x)]
                    if others and t > float(np.mean(others)) + 20:
                        fpa_err = True
            except Exception:
                pass

            tags = []
            if netd_err:
                tags.append("NETD")
            if fpa_err:
                tags.append("FPA")

            if tags:
                status = (status + " | " + " | ".join(tags)) if status else " | ".join(tags)

            new_rows.append((serial, netd_value, n_cold, n_hot, n_room, fpa_value, status))

        self.result_status_rows = new_rows

        # =========================================================
        # 3) 렌더 + 표 출력
        # =========================================================
        self._render_current()

        colnames = ["Serial", "NETD", "#Cold", "#Hot", "#Room", "FPA median", "Status"]
        col_widths = [10, 10, 10, 10, 10, 12, 40]

        header = "| " + " | ".join(name.ljust(col_widths[i]) for i, name in enumerate(colnames)) + " |"
        sep = "|" + "|".join("-" * (col_widths[i] + 2) for i in range(len(col_widths))) + "|"

        self._log(header)
        self._log(sep)

        for serial_name, netd_value, n_cold, n_hot, n_room, fpa_med, status in self.result_status_rows:
            try:
                netd_str = f"{float(netd_value):.2f}"
            except Exception:
                netd_str = str(netd_value)

            try:
                fpa_str = f"{float(fpa_med):.2f}"
            except Exception:
                fpa_str = str(fpa_med)

            row = "| " + " | ".join([
                str(serial_name).ljust(col_widths[0]),
                netd_str.rjust(col_widths[1]),
                str(int(n_cold)).rjust(col_widths[2]),
                str(int(n_hot)).rjust(col_widths[3]),
                str(int(n_room)).rjust(col_widths[4]),
                fpa_str.rjust(col_widths[5]),
                (status or "").ljust(col_widths[6]),
            ]) + " |"
            self._log(row)
        
        # =========================================================
        # 4) ✅ 에러 폴더(시리얼) 요약: "시리얼: 오류내용" 한줄씩
        #    - OK만 있는 애들은 출력 안 함
        #    - status에 들어있는 내용을 정리해서 출력
        # =========================================================
        err_lines = []
        for serial_name, _, _, _, _, _, status in self.result_status_rows:
            st = (status or "").strip()

            # OK만이면 패스
            if st == "" or st == "OK":
                continue

            # 보기 좋게: "OK | ERROR: NETD" 같은 케이스면 OK 제거
            parts = [p.strip() for p in st.split("|") if p.strip()]
            parts = [p for p in parts if p != "OK"]

            if not parts:
                continue

            err_lines.append(f"{serial_name}: " + " | ".join(parts))

        if err_lines:
            self._log("\n[ERROR SUMMARY]", tag="error")
            for line in err_lines:
                self._log(f" - {line}", tag="error")
        else:
            self._log("\n[ERROR SUMMARY] None")


if __name__ == "__main__":
    root = tk.Tk()
    app = TECLESS(root, redirect_output=True)
    root.protocol("WM_DELETE_WINDOW", app.shutdown)
    root.mainloop()
